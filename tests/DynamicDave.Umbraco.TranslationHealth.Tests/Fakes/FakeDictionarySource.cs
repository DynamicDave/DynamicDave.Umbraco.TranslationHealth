using DynamicDave.Umbraco.TranslationHealth.Dictionary;

namespace DynamicDave.Umbraco.TranslationHealth.Tests.Fakes;

internal sealed class FakeDictionarySource : IDictionarySource
{
    public List<DictionaryEntry> Entries { get; } = [];

    public IReadOnlyList<DictionaryEntry> GetAll() => [.. Entries];

    public Task SetValuesAsync(Guid id, IReadOnlyDictionary<string, string> values, Guid userKey)
    {
        var index = Entries.FindIndex(e => e.Id == id);
        var merged = new Dictionary<string, string>(Entries[index].Values, StringComparer.OrdinalIgnoreCase);
        foreach (var (culture, value) in values)
        {
            if (string.IsNullOrEmpty(value))
            {
                merged.Remove(culture);
            }
            else
            {
                merged[culture] = value;
            }
        }

        Entries[index] = Entries[index] with { Values = merged };
        return Task.CompletedTask;
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
