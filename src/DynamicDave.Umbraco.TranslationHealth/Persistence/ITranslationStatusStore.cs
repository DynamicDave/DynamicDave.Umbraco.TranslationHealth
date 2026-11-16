using DynamicDave.Umbraco.TranslationHealth.Status;

namespace DynamicDave.Umbraco.TranslationHealth.Persistence;

/// <summary>The indexed translation status: one row per document and one per document language.</summary>
public interface ITranslationStatusStore
{
    IReadOnlyList<VariantRow> GetVariants(Guid documentKey);

    /// <summary>The variants of several documents (chunked internally); documents without rows map to an empty list.</summary>
    IReadOnlyDictionary<Guid, IReadOnlyList<VariantRow>> GetVariants(IReadOnlyCollection<Guid> documentKeys);

    /// <summary>The keys of all stored documents.</summary>
    IReadOnlyList<Guid> GetKeys();

    /// <summary>
    /// Replaces the rows of several documents and removes others, in ONE write transaction that starts with a write
    /// (a transaction that reads first can fail on SQLite when another connection commits in between).
    /// </summary>
    void SaveMany(IReadOnlyList<IndexResult> results, IReadOnlyCollection<Guid> removals);

    /// <summary>Upserts one variant of an already indexed document.</summary>
    void SaveVariant(VariantRow row);
}
