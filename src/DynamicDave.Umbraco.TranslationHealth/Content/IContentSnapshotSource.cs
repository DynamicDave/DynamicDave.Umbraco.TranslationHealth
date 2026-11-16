namespace DynamicDave.Umbraco.TranslationHealth.Content;

/// <summary>Reads documents as <see cref="DocumentSnapshot"/>s; the only place that talks to Umbraco's content services.</summary>
public interface IContentSnapshotSource
{
    /// <summary>The snapshot of one document, or null when it is invariant, trashed, excluded or does not exist.</summary>
    DocumentSnapshot? Get(Guid key);

    /// <summary>
    /// The first <paramref name="pageSize"/> culture-variant documents outside the recycle bin and not of an excluded type
    /// with an id above <paramref name="afterId"/>, ordered by id. Keyset paging: the caller passes the id of the last
    /// document it got, so neither moves nor deletions in between make it skip a document.
    /// </summary>
    /// <param name="remaining">How many such documents there are above <paramref name="afterId"/>, this page included.</param>
    IReadOnlyList<DocumentSnapshot> GetPage(int afterId, int pageSize, out long remaining);

    /// <summary>The keys of all documents below a document (not the document itself), also for a document in the recycle bin.</summary>
    IReadOnlyList<Guid> DescendantKeys(Guid key);

    /// <summary>Drops cached knowledge about document types; call when a document type changed.</summary>
    void ClearCache();
}
