namespace DynamicDave.Umbraco.TranslationHealth.Dictionary;

/// <summary>One dictionary item. <see cref="Values"/> maps a culture (case-insensitive) to its non-empty value; a culture without a value is missing.</summary>
public sealed record DictionaryEntry(Guid Id, string Key, Guid? ParentId, IReadOnlyDictionary<string, string> Values);
