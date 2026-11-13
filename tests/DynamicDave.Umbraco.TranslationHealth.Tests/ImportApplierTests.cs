using DynamicDave.Umbraco.TranslationHealth.Dictionary;
using DynamicDave.Umbraco.TranslationHealth.Files;
using DynamicDave.Umbraco.TranslationHealth.Importing;
using DynamicDave.Umbraco.TranslationHealth.Tests.Fakes;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class ImportApplierTests
{
    private static readonly IReadOnlySet<string> Cultures = new HashSet<string> { "en-US", "nl-NL", "de-DE" };
    private readonly FakeDictionarySource _dictionary = new();
    private readonly FakeImportHistory _history = new();

    private ImportApplier Applier(IDictionarySource? source = null) =>
        new(source ?? _dictionary, _history, new ManualTimeProvider(new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero)));

    private DictionaryEntry Add(string key, Guid? parent = null, params (string Culture, string Value)[] values)
    {
        var entry = new DictionaryEntry(Guid.NewGuid(), key, parent,
            values.ToDictionary(v => v.Culture, v => v.Value, StringComparer.OrdinalIgnoreCase));
        _dictionary.Entries.Add(entry);
        return entry;
    }

    private ImportPlan Plan(ImportOptions options, string fileName, params ImportRow[] rows) =>
        ImportPlanner.Plan(Guid.NewGuid(), fileName, new ImportFile(ImportFileFormat.Csv, rows, [], null, null),
            _dictionary.GetAll(), "en-US", Cultures, Cultures, options);

    private static ImportRow Row(int number, string key, params (string Culture, string Value)[] values) =>
        new(number, key, values.ToDictionary(v => v.Culture, v => v.Value, StringComparer.OrdinalIgnoreCase), null);

    private static IReadOnlySet<int> All(ImportPlan plan) => plan.Items.Select(i => i.Id).ToHashSet();

    [Fact]
    public async Task Applies_selected_items_and_records_history()
    {
        Add("A", null, ("en-US", "Hello"), ("nl-NL", "Oud"));
        var plan = Plan(new ImportOptions(true, false), "f.csv", Row(2, "A", ("nl-NL", "Nieuw"), ("de-DE", "Hallo")));

        var result = await Applier().ApplyAsync(plan, All(plan), Guid.NewGuid(), "Dave");

        Assert.Equal(2, result.Applied);
        Assert.Equal(0, result.Skipped);
        Assert.All(result.Items, i => Assert.Equal(ApplyOutcome.Applied, i.Outcome));
        var values = _dictionary.Entries.Single().Values;
        Assert.Equal("Nieuw", values["nl-NL"]);
        Assert.Equal("Hallo", values["de-DE"]);

        var record = _history.Get(result.ImportKey)!;
        Assert.Equal("Dave", record.UserName);
        Assert.Equal("Csv", record.Format);
        Assert.True(record.Overwrite);
        Assert.Equal(2, record.Applied);
        var rows = _history.Rows(result.ImportKey);
        Assert.Contains(rows, r => r.Culture == "nl-NL" && r.OldValue == "Oud" && r.NewValue == "Nieuw" && !r.KeyCreated);
        Assert.Contains(rows, r => r.Culture == "de-DE" && r.OldValue is null && r.NewValue == "Hallo");
    }

    [Fact]
    public async Task Unselected_items_are_not_written()
    {
        Add("A", null, ("en-US", "Hello"));
        var plan = Plan(new ImportOptions(false, false), "f.csv", Row(2, "A", ("nl-NL", "Hallo"), ("de-DE", "Hallo")));
        var selected = new HashSet<int> { plan.Items[0].Id };

        var result = await Applier().ApplyAsync(plan, selected, Guid.NewGuid(), "Dave");

        Assert.Equal(1, result.Applied);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(ApplyOutcome.Skipped, result.Items.Single(i => i.ItemId == plan.Items[1].Id).Outcome);
        Assert.False(_dictionary.Entries.Single().Values.ContainsKey("de-DE"));
        Assert.Equal(1, _history.Get(result.ImportKey)!.Skipped);
    }

    [Fact]
    public async Task Value_changed_since_test_run_is_changed()
    {
        var entry = Add("A", null, ("en-US", "Hello"), ("nl-NL", "Oud"));
        var plan = Plan(new ImportOptions(true, false), "f.csv", Row(2, "A", ("nl-NL", "Nieuw"), ("de-DE", "Hallo")));
        _dictionary.Entries[0] = entry with { Values = new Dictionary<string, string> { ["en-US"] = "Hello", ["nl-NL"] = "Iemand anders" } };

        var result = await Applier().ApplyAsync(plan, All(plan), Guid.NewGuid(), "Dave");

        Assert.Equal(ApplyOutcome.Changed, result.Items.Single(i => i.ItemId == plan.Items[0].Id).Outcome);
        Assert.Equal(ApplyOutcome.Applied, result.Items.Single(i => i.ItemId == plan.Items[1].Id).Outcome);
        Assert.Equal("Iemand anders", _dictionary.Entries.Single().Values["nl-NL"]);
        Assert.Equal(1, result.Applied);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public async Task Line_ending_differences_are_not_a_change()
    {
        Add("A", null, ("en-US", "Hello"), ("nl-NL", "a\r\nb"));
        var plan = Plan(new ImportOptions(true, false), "f.csv", Row(2, "A", ("nl-NL", "x")));
        _dictionary.Entries[0] = _dictionary.Entries[0] with { Values = new Dictionary<string, string> { ["en-US"] = "Hello", ["nl-NL"] = "a\nb" } };

        var result = await Applier().ApplyAsync(plan, All(plan), Guid.NewGuid(), "Dave");

        Assert.Equal(1, result.Applied);
    }

    [Fact]
    public async Task New_keys_are_created_under_longest_prefix()
    {
        var footer = Add("Footer", null, ("en-US", "Footer"));
        var plan = Plan(new ImportOptions(false, true), "f.csv",
            Row(2, "Footer.Contact.Fax", ("nl-NL", "Fax"), ("de-DE", "Fax")),
            Row(3, "Footer.Contact", ("nl-NL", "Contact")));

        var result = await Applier().ApplyAsync(plan, All(plan), Guid.NewGuid(), "Dave");

        Assert.Equal(3, result.Applied);
        var contact = _dictionary.Entries.Single(e => e.Key == "Footer.Contact");
        var fax = _dictionary.Entries.Single(e => e.Key == "Footer.Contact.Fax");
        Assert.Equal(footer.Id, contact.ParentId);
        Assert.Equal(contact.Id, fax.ParentId);
        Assert.Equal(2, fax.Values.Count);
        Assert.All(_history.Rows(result.ImportKey), r => Assert.True(r.KeyCreated));
    }

    [Fact]
    public async Task New_key_that_exists_by_apply_time_is_changed()
    {
        var plan = Plan(new ImportOptions(false, true), "f.csv", Row(2, "Late", ("nl-NL", "Laat")));
        Add("Late", null, ("en-US", "Late"));

        var result = await Applier().ApplyAsync(plan, All(plan), Guid.NewGuid(), "Dave");

        Assert.Equal(ApplyOutcome.Changed, result.Items.Single().Outcome);
        Assert.Equal(0, result.Applied);
        Assert.Equal(0, _history.SaveCount);
    }

    [Fact]
    public async Task Failed_write_keeps_history_of_what_was_written()
    {
        Add("A", null, ("en-US", "a"));
        Add("B", null, ("en-US", "b"));
        var plan = Plan(new ImportOptions(false, false), "f.csv", Row(2, "A", ("nl-NL", "1")), Row(3, "B", ("nl-NL", "2")));
        var failing = new FailingSource(_dictionary, failOnSecondSet: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Applier(failing).ApplyAsync(plan, All(plan), Guid.NewGuid(), "Dave"));

        var record = Assert.Single(_history.List(0, 10).Items);
        Assert.Equal(1, record.Applied);
        Assert.Equal("A", Assert.Single(_history.Rows(record.Key)).Key);
    }

    [Fact]
    public async Task Failure_before_any_write_saves_no_history()
    {
        Add("A", null, ("en-US", "a"));
        var plan = Plan(new ImportOptions(false, false), "f.csv", Row(2, "A", ("nl-NL", "1")));
        var failing = new FailingSource(_dictionary, failOnFirstSet: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Applier(failing).ApplyAsync(plan, All(plan), Guid.NewGuid(), "Dave"));

        Assert.Equal(0, _history.SaveCount);
    }

    [Fact]
    public async Task Long_file_and_user_names_are_truncated()
    {
        Add("A", null, ("en-US", "a"));
        var plan = Plan(new ImportOptions(false, false), new string('f', 400), Row(2, "A", ("nl-NL", "1")));

        var result = await Applier().ApplyAsync(plan, All(plan), Guid.NewGuid(), new string('u', 400));

        var record = _history.Get(result.ImportKey)!;
        Assert.Equal(255, record.FileName.Length);
        Assert.Equal(255, record.UserName.Length);
    }

    private sealed class FailingSource(FakeDictionarySource inner, bool failOnFirstSet = false, bool failOnSecondSet = false) : IDictionarySource
    {
        private int _sets;

        public IReadOnlyList<DictionaryEntry> GetAll() => inner.GetAll();

        public Task SetValuesAsync(Guid id, IReadOnlyDictionary<string, string> values, Guid userKey)
        {
            _sets++;
            if ((failOnFirstSet && _sets == 1) || (failOnSecondSet && _sets == 2))
            {
                throw new InvalidOperationException("Umbraco refused");
            }

            return inner.SetValuesAsync(id, values, userKey);
        }

        public Task<Guid> CreateAsync(string key, Guid? parentId, IReadOnlyDictionary<string, string> values, Guid userKey)
            => inner.CreateAsync(key, parentId, values, userKey);

        public Task DeleteAsync(Guid id, Guid userKey) => inner.DeleteAsync(id, userKey);
    }
}
