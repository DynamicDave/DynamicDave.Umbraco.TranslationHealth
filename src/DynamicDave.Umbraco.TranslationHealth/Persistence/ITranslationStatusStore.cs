using DynamicDave.Umbraco.TranslationHealth.Status;

namespace DynamicDave.Umbraco.TranslationHealth.Persistence;

/// <summary>The indexed translation status: one row per document and one per document language.</summary>
public interface ITranslationStatusStore
{
    IReadOnlyList<VariantRow> GetVariants(Guid documentKey);

    DocumentRow? GetDocument(Guid key);

    /// <summary>Upserts the document and replaces its variants, in one scope.</summary>
    void Save(IndexResult result);

    /// <summary>Upserts one variant of an already indexed document.</summary>
    void SaveVariant(VariantRow row);

    /// <summary>Removes the documents and their variants.</summary>
    void Remove(IReadOnlyCollection<Guid> documentKeys);

    /// <summary>Removes every document that is not in <paramref name="keepKeys"/>, with its variants.</summary>
    void RemoveExcept(IReadOnlySet<Guid> keepKeys);

    int Count();
}
