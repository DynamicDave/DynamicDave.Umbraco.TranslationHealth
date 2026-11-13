using System.Text;
using DynamicDave.Umbraco.TranslationHealth.Files;
using DynamicDave.Umbraco.TranslationHealth.Overview;
using DynamicDave.Umbraco.TranslationHealth.Status;
using Xunit;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class OverviewAggregateTests
{
    private static TotalsRow T(string culture, CellStatus status, bool outdated, int total) =>
        new() { Culture = culture, Status = status.ToString(), Outdated = outdated, Total = total };

    [Fact]
    public void Totals_exclude_not_applicable_and_count_complete_and_outdated()
    {
        var result = OverviewSql.Aggregate(
        [
            T("nl-NL", CellStatus.Published, false, 5),
            T("nl-NL", CellStatus.Published, true, 2),
            T("nl-NL", CellStatus.PendingChanges, true, 1),
            T("nl-NL", CellStatus.PendingChanges, false, 3),
            T("nl-NL", CellStatus.Draft, false, 4),
            T("nl-NL", CellStatus.Missing, false, 6),
            T("nl-NL", CellStatus.NotApplicable, false, 9),
            T("de-DE", CellStatus.NotApplicable, false, 7),
            T("en-US", CellStatus.Published, false, 1),
        ]);

        Assert.Equal(["en-US", "nl-NL"], result.Select(r => r.Culture).ToArray());
        Assert.Equal(new LanguageTotal("nl-NL", 21, 5, 6, 4, 4, 3), result[1]);
        Assert.Equal(new LanguageTotal("en-US", 1, 1, 0, 0, 0, 0), result[0]);
    }
}

public class BreadcrumbResolverTests
{
    private static (BreadcrumbResolver Resolver, List<int[]> Calls) Create(Dictionary<int, string> names)
    {
        var calls = new List<int[]>();
        var resolver = new BreadcrumbResolver(ids =>
        {
            calls.Add(ids);
            return ids.Where(names.ContainsKey).Select(id => KeyValuePair.Create(id, (string?)names[id])).ToList();
        });
        return (resolver, calls);
    }

    private static DocumentRow Doc(string path) => new(Guid.NewGuid(), 0, null, path, "", 1, "page", Guid.Empty, "n");

    [Fact]
    public void Builds_ancestors_root_first_without_root_marker_or_self()
    {
        var (resolver, _) = Create(new() { [1] = "Home", [2] = "About", [3] = "Team" });
        var doc = Doc("-1,1,2,3");

        resolver.Prime([doc]);

        Assert.Equal("Home / About", resolver.Build(doc));
    }

    [Fact]
    public void Top_level_document_has_an_empty_breadcrumb()
    {
        var (resolver, calls) = Create([]);
        var doc = Doc("-1,1");

        resolver.Prime([doc]);

        Assert.Equal(string.Empty, resolver.Build(doc));
        Assert.Empty(calls);
    }

    [Fact]
    public void Prime_makes_one_call_for_the_batch_and_caches_names()
    {
        var (resolver, calls) = Create(new() { [1] = "Home", [2] = "About" });

        resolver.Prime([Doc("-1,1,10"), Doc("-1,1,2,11")]);
        resolver.Prime([Doc("-1,1,2,12")]);

        var call = Assert.Single(calls);
        Assert.Equal([1, 2], call.OrderBy(i => i).ToArray());
    }

    [Fact]
    public void Unknown_ancestors_are_skipped_and_not_asked_again()
    {
        var (resolver, calls) = Create(new() { [1] = "Home" });
        var doc = Doc("-1,1,99,5");

        resolver.Prime([doc]);
        resolver.Prime([doc]);

        Assert.Equal("Home", resolver.Build(doc));
        Assert.Single(calls);
    }

    [Fact]
    public void Primed_yields_all_rows_in_order_with_one_call_per_chunk()
    {
        var (resolver, calls) = Create(new() { [1] = "Home" });
        var rows = Enumerable.Range(0, 5).Select(_ => new OverviewRow(Doc("-1,1,2"), [])).ToList();

        var result = resolver.Primed(rows, 2).ToList();

        Assert.Equal(rows, result);
        Assert.Single(calls);
    }
}

public class CsvParserWriteTests
{
    [Fact]
    public void Write_quotes_commas_quotes_and_newlines()
    {
        var bytes = CsvParser.Write(["a", "b"], [["x,y", "say \"hi\""], ["line1\r\nline2", "plain"]]);

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3).ToArray());
        Assert.Equal("a,b\r\n\"x,y\",\"say \"\"hi\"\"\"\r\n\"line1\r\nline2\",plain\r\n", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
    }
}
