namespace DynamicDave.Umbraco.TranslationHealth.Content;

public enum VariantBaseState
{
    NotCreated,
    Draft,
    Published,
    PublishedPendingChanges,
}

/// <summary>One culture of a document. <c>Fingerprint</c> is the draft fingerprint, null when the culture is not created.</summary>
public record CultureSnapshot(
    string Culture,
    VariantBaseState State,
    DateTime? UpdateDateUtc,
    DateTime? PublishDateUtc,
    string? Name,
    string? Fingerprint);

/// <summary>
/// Everything the indexer needs to know about a document. <c>Path</c> is the Umbraco id path (<c>-1,1057,1060</c>);
/// <c>SortPath</c> holds the sort orders of the ancestors-or-self, zero-padded to 5 digits and joined with a dot.
/// </summary>
public record DocumentSnapshot(
    Guid Key,
    int Id,
    Guid? ParentKey,
    string Path,
    string SortPath,
    int Level,
    string ContentTypeAlias,
    IReadOnlyList<CultureSnapshot> Cultures);
