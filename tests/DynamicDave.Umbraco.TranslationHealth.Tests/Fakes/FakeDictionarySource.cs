using DynamicDave.Umbraco.TranslationHealth.Dictionary;

namespace DynamicDave.Umbraco.TranslationHealth.Tests.Fakes;

internal sealed class FakeDictionarySource : IDictionarySource
{
    public List<DictionaryEntry> Entries { get; } = [];

    public IReadOnlyList<DictionaryEntry> GetAll() => [.. Entries];

    public Task<DictionaryEntry?> GetAsync(Guid id) => Task.FromResult(Entries.FirstOrDefault(e => e.Id == id));

    public Task<bool> HasChildrenAsync(Guid id) => Task.FromResult(Entries.Any(e => e.ParentId == id));

    public Task<SetValuesResult> SetValuesAsync(Guid id, IReadOnlyList<ValueChange> changes, Guid userKey)
    {
        var index = Entries.FindIndex(e => e.Id == id);
        if (index < 0)
        {
            return Task.FromResult(new SetValuesResult([], changes.Select(c => c.Culture).ToList()));
        }

        var merged = new Dictionary<string, string>(Entries[index].Values, StringComparer.OrdinalIgnoreCase);
        var written = new List<WrittenValue>();
        var conflicts = new List<string>();
        foreach (var change in changes)
        {
            var current = DictionaryValues.NonEmpty(merged, change.Culture);
            if (!DictionaryValues.SameValue(current, change.Expected))
            {
                conflicts.Add(change.Culture);
                continue;
            }

            if (string.IsNullOrEmpty(change.NewValue))
            {
                merged.Remove(change.Culture);
            }
            else
            {
                merged[change.Culture] = change.NewValue;
            }

            written.Add(new WrittenValue(change.Culture, current, change.NewValue));
        }

        Entries[index] = Entries[index] with { Values = merged };
        return Task.FromResult(new SetValuesResult(written, conflicts));
    }

    public Task<Guid> CreateAsync(string key, Guid? parentId, IReadOnlyDictionary<string, string> values, Guid userKey)
    {
        var id = Guid.NewGuid();
        var kept = values.Where(v => !string.IsNullOrEmpty(v.Value)).ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
        Entries.Add(new DictionaryEntry(id, key, parentId, kept));
        return Task.FromResult(id);
    }

    public Task DeleteAsync(Guid id, Guid userKey)
    {
        Entries.RemoveAll(e => e.Id == id);
        return Task.CompletedTask;
    }
}
