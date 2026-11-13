namespace DynamicDave.Umbraco.TranslationHealth.Sites;

public interface ISiteSource
{
    IReadOnlyList<SiteInfo> GetSites();

    string DefaultCulture();

    IReadOnlyList<string> AllCultures();

    /// <summary>Drops the cached sites and languages; call when domains or languages change.</summary>
    void ClearCache();
}
