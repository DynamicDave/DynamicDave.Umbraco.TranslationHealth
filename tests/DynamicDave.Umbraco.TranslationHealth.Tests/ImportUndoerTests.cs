using DynamicDave.Umbraco.TranslationHealth.Dictionary;
using DynamicDave.Umbraco.TranslationHealth.Files;
using DynamicDave.Umbraco.TranslationHealth.Importing;
using DynamicDave.Umbraco.TranslationHealth.Persistence;
using DynamicDave.Umbraco.TranslationHealth.Tests.Fakes;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class ImportUndoerTests
{
    private static readonly IReadOnlySet<string> Cultures = new HashSet<string> { "en-US", "nl-NL", "de-DE" };
    private readonly FakeDictionarySource _dictionary = new();
    private readonly FakeImportHistory _history = new();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero));

    private DictionaryEntry Add(string key, params (string Culture, string Value)[] values)
    {
        var entry = new DictionaryEntry(Guid.NewGuid(), key, null,
            values.ToDictionary(v => v.Culture, v => v.Value, StringComparer.OrdinalIgnoreCase));
        _dictionary.Entries.Add(entry);
        return entry;
    }

    private static ImportRow Row(int number, string key, params (string Culture, string Value)[] values) =>
        new(number, key, values.ToDictionary(v => v.Culture, v => v.Value, StringComparer.OrdinalIgnoreCase), null);

    private async Task<Guid> Apply(bool createKeys, params ImportRow[] rows)
    {
        var plan = ImportPlanner.Plan(Guid.NewGuid(), "f.csv", new ImportFile(ImportFileFormat.Csv, rows, [], null, null),
            _dictionary.GetAll(), "en-US", Cultures, Cultures, new ImportOptions(true, createKeys));
        var result = await new ImportApplier(_dictionary, _history, _time)
            .ApplyAsync(plan, plan.Items.Select(i => i.Id).ToHashSet(), Guid.NewGuid(), "Dave");
        return result.ImportKey;
    }

    private ImportUndoer Undoer() => new(_dictionary, _history, _time);

    private static string? Value(DictionaryEntry entry, string culture) => entry.Values.GetValueOrDefault(culture);

    [Fact]
    public async Task Undo_restores_old_values_and_removes_created_keys()
    {
        Add("A", ("en-US", "Hello"), ("nl-NL", "Oud"));
        var key = await Apply(true, Row(2, "A", ("nl-NL", "Nieuw"), ("de-DE", "Hallo")), Row(3, "New", ("nl-NL", "Nieuw")));

        var result = await Undoer().UndoAsync(key, Guid.NewGuid(), "Dave");

        Assert.Equal(2, result.Restored);
        Assert.Equal(1, result.KeysRemoved);
        Assert.Empty(result.LeftAlone);
        var entry = Assert.Single(_dictionary.Entries);
        Assert.Equal("Oud", Value(entry, "nl-NL"));
        Assert.Null(Value(entry, "de-DE"));
        Assert.Equal(ImportState.Undone, _history.Get(key)!.State);
    }

    [Fact]
    public async Task Undo_leaves_values_changed_since_the_import()
    {
        Add("A", ("en-US", "Hello"), ("nl-NL", "Oud"));
        var key = await Apply(true, Row(2, "A", ("nl-NL", "Nieuw"), ("de-DE", "Hallo")), Row(3, "New", ("nl-NL", "Nieuw")));
        _dictionary.Entries[0] = _dictionary.Entries[0] with
        {
            Values = new Dictionary<string, string> { ["en-US"] = "Hello", ["nl-NL"] = "Later bewerkt", ["de-DE"] = "Hallo" },
        };
        var created = _dictionary.Entries.Single(e => e.Key == "New");
        _dictionary.Entries[_dictionary.Entries.IndexOf(created)] = created with
        {
            Values = new Dictionary<string, string> { ["nl-NL"] = "Nieuw", ["de-DE"] = "Erbij" },
        };

        var result = await Undoer().UndoAsync(key, Guid.NewGuid(), "Dave");

        Assert.Equal(1, result.Restored);
        Assert.Equal(0, result.KeysRemoved);
        Assert.Equal(2, result.LeftAlone.Count);
        Assert.Contains(result.LeftAlone, r => r.Key == "A" && r.Culture == "nl-NL");
        Assert.Contains(result.LeftAlone, r => r.Key == "New");
        Assert.Equal("Later bewerkt", Value(_dictionary.Entries[0], "nl-NL"));
        Assert.Null(Value(_dictionary.Entries[0], "de-DE"));
        Assert.Contains(_dictionary.Entries, e => e.Key == "New");
    }

    [Fact]
    public async Task Undo_keeps_created_key_that_got_children_later()
    {
        var key = await Apply(true, Row(2, "Footer", ("nl-NL", "Voet")));
        var footer = _dictionary.Entries.Single();
        _dictionary.Entries.Add(new DictionaryEntry(Guid.NewGuid(), "Other", footer.Id, new Dictionary<string, string>()));

        var result = await Undoer().UndoAsync(key, Guid.NewGuid(), "Dave");

        Assert.Equal(0, result.KeysRemoved);
        Assert.Single(result.LeftAlone);
        Assert.Equal(2, _dictionary.Entries.Count);
    }

    [Fact]
    public async Task Undo_removes_created_parent_and_child_together()
    {
        var key = await Apply(true, Row(2, "G.H", ("nl-NL", "H")), Row(3, "G", ("nl-NL", "G")));

        var result = await Undoer().UndoAsync(key, Guid.NewGuid(), "Dave");

        Assert.Equal(2, result.KeysRemoved);
        Assert.Empty(_dictionary.Entries);
    }

    [Fact]
    public async Task Undo_twice_is_refused()
    {
        Add("A", ("en-US", "Hello"));
        var key = await Apply(false, Row(2, "A", ("nl-NL", "Nieuw")));
        await Undoer().UndoAsync(key, Guid.NewGuid(), "Dave");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Undoer().UndoAsync(key, Guid.NewGuid(), "Dave"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Undoer().UndoAsync(Guid.NewGuid(), Guid.NewGuid(), "Dave"));
    }
}
