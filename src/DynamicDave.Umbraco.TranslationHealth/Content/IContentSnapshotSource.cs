namespace DynamicDave.Umbraco.TranslationHealth.Content;

/// <summary>Reads documents as <see cref="DocumentSnapshot"/>s; the only place that talks to Umbraco's content services.</summary>
public interface IContentSnapshotSource
{
    /// <summary>The snapshot of one document, or null when it is invariant, trashed, excluded or does not exist.</summary>
    DocumentSnapshot? Get(Guid key);

    /// <summary>One page of all culture-variant documents outside the recycle bin and not of an excluded type, ordered by path.</summary>
    IReadOnlyList<DocumentSnapshot> GetPage(long pageIndex, int pageSize, out long total);

    /// <summary>The keys of all documents below a document (not the document itself).</summary>
    IReadOnlyList<Guid> DescendantKeys(Guid key);
}
