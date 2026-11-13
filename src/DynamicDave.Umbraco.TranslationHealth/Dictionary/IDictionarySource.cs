namespace DynamicDave.Umbraco.TranslationHealth.Dictionary;

/// <summary>One culture to write: only applied while the stored value still equals <see cref="Expected"/> (null = no value).</summary>
public sealed record ValueChange(string Culture, string? Expected, string NewValue);

public sealed record WrittenValue(string Culture, string? OldValue, string NewValue);

/// <summary>What a compare-and-set did: the values written (with the value that was really there before) and the cultures whose value no longer matched.</summary>
public sealed record SetValuesResult(IReadOnlyList<WrittenValue> Written, IReadOnlyList<string> Conflicts);

public interface IDictionarySource
{
    /// <summary>Every dictionary item with its non-empty values, in one read.</summary>
    IReadOnlyList<DictionaryEntry> GetAll();

    /// <summary>The item as it is now, or null when it is gone.</summary>
    Task<DictionaryEntry?> GetAsync(Guid id);

    Task<bool> HasChildrenAsync(Guid id);

    /// <summary>
    /// Loads the item fresh and writes only the cultures whose current value still equals <see cref="ValueChange.Expected"/>
    /// (an empty new value removes the translation). A missing item gives all cultures as conflicts. Throws
    /// <see cref="InvalidOperationException"/> (including the Umbraco status) when Umbraco refuses the write.
    /// </summary>
    Task<SetValuesResult> SetValuesAsync(Guid id, IReadOnlyList<ValueChange> changes, Guid userKey);

    /// <summary>Creates a key under an optional parent and returns its id. Empty values are not stored.</summary>
    Task<Guid> CreateAsync(string key, Guid? parentId, IReadOnlyDictionary<string, string> values, Guid userKey);

    Task DeleteAsync(Guid id, Guid userKey);
}
