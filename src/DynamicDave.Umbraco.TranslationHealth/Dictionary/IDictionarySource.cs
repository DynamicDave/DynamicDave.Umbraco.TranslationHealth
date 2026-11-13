namespace DynamicDave.Umbraco.TranslationHealth.Dictionary;

public interface IDictionarySource
{
    /// <summary>Every dictionary item with its non-empty values, in one read.</summary>
    IReadOnlyList<DictionaryEntry> GetAll();

    /// <summary>Sets each given culture on the item; an empty string removes that translation. Throws <see cref="InvalidOperationException"/> (including the Umbraco status) when Umbraco refuses.</summary>
    Task SetValuesAsync(Guid id, IReadOnlyDictionary<string, string> values, Guid userKey);

    /// <summary>Creates a key under an optional parent and returns its id. Empty values are not stored.</summary>
    Task<Guid> CreateAsync(string key, Guid? parentId, IReadOnlyDictionary<string, string> values, Guid userKey);

    Task DeleteAsync(Guid id, Guid userKey);
}
