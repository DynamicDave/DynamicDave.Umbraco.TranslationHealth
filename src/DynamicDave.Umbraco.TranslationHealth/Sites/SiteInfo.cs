namespace DynamicDave.Umbraco.TranslationHealth.Sites;

/// <summary>
/// A site (root document) and the languages it uses. <see cref="Cultures"/> lists the cultures of its domains
/// (wildcards included, case-insensitive distinct) and is empty when <see cref="HasDomains"/> is false, which means
/// "all languages". <see cref="MissingDefaultCulture"/>: the site has domains but none for the default culture.
/// </summary>
public record SiteInfo(
    Guid RootKey,
    int RootId,
    string Name,
    IReadOnlyList<string> Cultures,
    bool HasDomains,
    bool MissingDefaultCulture);
