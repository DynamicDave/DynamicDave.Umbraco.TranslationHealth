using DynamicDave.Umbraco.TranslationHealth.Sites;

namespace DynamicDave.Umbraco.TranslationHealth.Tests.Fakes;

internal sealed class FakeSiteSource : ISiteSource
{
    public List<SiteInfo> Sites { get; } = [];

    public string Default { get; set; } = "en-US";

    public List<string> Cultures { get; } = ["en-US", "nl-NL"];

    public int ClearCacheCalls { get; private set; }

    public IReadOnlyList<SiteInfo> GetSites() => Sites;

    public string DefaultCulture() => Default;

    public IReadOnlyList<string> AllCultures() => Cultures;

    public void ClearCache() => ClearCacheCalls++;
}
