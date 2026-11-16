namespace DynamicDave.Umbraco.TranslationHealth.Indexing;

/// <summary>
/// A language of a document cannot be marked up to date: it has no row, is not created, is the default language, or is
/// not outdated when that is required. Other failures of a mark are not this type.
/// </summary>
public sealed class NotOutdatedException(Guid documentKey, string culture)
    : InvalidOperationException($"Language '{culture}' of document {documentKey} cannot be marked up to date.");
