using DynamicDave.Umbraco.TranslationHealth.Sites;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class SiteResolverTests
{
    private static SiteInfo Site(int id, params string[] cultures) =>
        new(Guid.NewGuid(), id, "Site " + id, cultures, cultures.Length > 0, false);

    [Fact]
    public void Top_level_site_without_domains_uses_all_languages()
    {
        var sites = new[] { Site(10), Site(11, "en-US") };

        var site = SiteResolver.SiteFor("-1,10,40", sites);

        Assert.Equal(10, site!.RootId);
        Assert.Empty(site.Cultures);
    }

    [Fact]
    public void Nested_root_with_domains_wins_over_top_level()
    {
        var sites = new[] { Site(10), Site(20, "nl-NL") };

        Assert.Equal(20, SiteResolver.SiteFor("-1,10,20,30", sites)!.RootId);
    }

    [Fact]
    public void Unknown_path_gives_null()
    {
        var sites = new[] { Site(10) };

        Assert.Null(SiteResolver.SiteFor("-1,99,100", sites));
        Assert.Null(SiteResolver.SiteFor("-1", sites));
        Assert.Null(SiteResolver.SiteFor("", sites));
    }

    [Fact]
    public void Root_itself_resolves_to_its_site()
    {
        var sites = new[] { Site(10), Site(20, "nl-NL") };

        Assert.Equal(10, SiteResolver.SiteFor("-1,10", sites)!.RootId);
        Assert.Equal(20, SiteResolver.SiteFor("-1,10,20", sites)!.RootId);
    }

    [Fact]
    public void Deepest_root_with_domains_wins()
    {
        var sites = new[] { Site(10, "en-US"), Site(20, "nl-NL"), Site(30, "de-DE") };

        Assert.Equal(30, SiteResolver.SiteFor("-1,10,20,30,40", sites)!.RootId);
    }
}
