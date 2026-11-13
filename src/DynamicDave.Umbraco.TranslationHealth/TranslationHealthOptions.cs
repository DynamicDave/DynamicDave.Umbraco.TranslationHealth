namespace DynamicDave.Umbraco.TranslationHealth;

/// <summary>Settings from the <c>DynamicDave:TranslationHealth</c> configuration section.</summary>
public class TranslationHealthOptions
{
    /// <summary>Aliases of document types that are left out of the overview and the exports (case-insensitive).</summary>
    public string[] ExcludedDocumentTypes { get; set; } = [];
}
