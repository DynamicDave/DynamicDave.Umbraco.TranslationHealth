using DynamicDave.Umbraco.TranslationHealth.Overview;
using Xunit;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class OverviewSqlTests
{
    private static readonly OverviewFilter NoFilter = new(null, null, null, null, null, false);

    [Fact]
    public void Scope_excludes_documents_outside_start_nodes()
    {
        var (where, args) = OverviewSql.BuildWhere(NoFilter, new StartNodeScope([20]));

        Assert.Contains("LIKE", where);
        Assert.Equal(["%,20,%", "%,20"], args.Cast<string>().ToArray());
    }

    [Fact]
    public void Null_scope_adds_no_path_filter()
    {
        var (where, args) = OverviewSql.BuildWhere(NoFilter, new StartNodeScope(null));

        Assert.DoesNotContain("path", where);
        Assert.Empty(args);
    }

    [Fact]
    public void Root_start_node_adds_no_path_filter()
    {
        var (where, _) = OverviewSql.BuildWhere(NoFilter, new StartNodeScope([-1, 20]));

        Assert.DoesNotContain("path", where);
    }

    [Fact]
    public void Empty_start_nodes_match_nothing()
    {
        var (where, _) = OverviewSql.BuildWhere(NoFilter, new StartNodeScope([]));

        Assert.Contains("1 = 0", where);
    }

    [Fact]
    public void Only_issues_never_matches_not_applicable()
    {
        var (where, args) = OverviewSql.BuildWhere(NoFilter with { OnlyIssues = true }, new StartNodeScope(null));
        var strings = args.OfType<string>().ToList();

        Assert.Contains("EXISTS", where);
        Assert.Contains("v.status <>", where);
        Assert.Equal(["Missing", "Draft", "PendingChanges", "NotApplicable"], strings);
    }

    [Fact]
    public void Culture_filter_applies_to_that_cultures_variant()
    {
        var (where, args) = OverviewSql.BuildWhere(NoFilter with { Culture = "nl-NL", Status = CellFilter.Missing }, new StartNodeScope(null));

        Assert.Contains("v.culture =", where);
        Assert.Contains("nl-NL", args.OfType<string>());
        Assert.Contains("Missing", args.OfType<string>());
    }

    [Fact]
    public void Status_without_culture_checks_any_culture()
    {
        var (where, _) = OverviewSql.BuildWhere(NoFilter with { Status = CellFilter.Draft }, new StartNodeScope(null));

        Assert.DoesNotContain("v.culture", where);
    }

    [Fact]
    public void Site_and_type_filters_are_parameters()
    {
        var site = Guid.NewGuid();
        var (where, args) = OverviewSql.BuildWhere(NoFilter with { SiteRootKey = site, ContentTypeAlias = "page" }, new StartNodeScope(null));

        Assert.Contains("siteRootKey", where);
        Assert.Contains(site, args.OfType<Guid>());
        Assert.Contains("page", args.OfType<string>());
        Assert.DoesNotContain("'page'", where);
    }

    [Fact]
    public void Empty_site_key_filters_documents_without_a_site()
    {
        var (_, args) = OverviewSql.BuildWhere(NoFilter with { SiteRootKey = Guid.Empty }, new StartNodeScope(null));

        Assert.Equal(Guid.Empty, Assert.Single(args.OfType<Guid>()));
    }

    [Fact]
    public void Name_search_escapes_wildcards()
    {
        var (where, args) = OverviewSql.BuildWhere(NoFilter with { NameSearch = @"50%_off [a]\" }, new StartNodeScope(null));

        Assert.Contains(@"ESCAPE '\'", where);
        Assert.Equal(@"%50\%\_off \[a]\\%", Assert.Single(args.OfType<string>()));
    }

    [Fact]
    public void Blank_name_search_is_ignored()
    {
        var (where, args) = OverviewSql.BuildWhere(NoFilter with { NameSearch = "  " }, new StartNodeScope(null));

        Assert.DoesNotContain("LIKE", where);
        Assert.Empty(args);
    }

    [Fact]
    public void Placeholders_match_the_arguments()
    {
        var (where, args) = OverviewSql.BuildWhere(
            new OverviewFilter(Guid.NewGuid(), "page", "nl-NL", CellFilter.AnyIssue, "x", true), new StartNodeScope([5, 6]));

        for (var i = 0; i < args.Length; i++)
        {
            Assert.Contains("@" + i, where);
        }

        Assert.DoesNotContain("@" + args.Length, where);
    }
}
