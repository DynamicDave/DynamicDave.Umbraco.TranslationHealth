using System.Text;
using DynamicDave.Umbraco.TranslationHealth.Persistence;
using DynamicDave.Umbraco.TranslationHealth.Status;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Extensions;

namespace DynamicDave.Umbraco.TranslationHealth.Overview;

/// <summary>The paged, filtered overview, the per-language totals and the export rows, all read from the status tables.</summary>
public class OverviewQuery(IScopeProvider scopeProvider)
{
    internal const int MaxTake = 100;
    internal const int ExportPageSize = 500;

    private const string Select = "SELECT d.[key], d.id, d.parentKey, d.path, d.sortPath, d.level, d.contentTypeAlias, d.siteRootKey, d.name";
    private const string OrderBy = "ORDER BY d.sortPath, d.[key]";

    public OverviewPage Query(OverviewFilter filter, StartNodeScope scope, int skip, int take)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, MaxTake);

        using var dbScope = scopeProvider.CreateScope(autoComplete: true);
        var (where, args) = OverviewSql.BuildWhere(filter, scope);
        var from = $"FROM {DocumentDto.TableName} d WHERE {where}";

        var total = dbScope.Database.ExecuteScalar<int>(dbScope.SqlContext.Sql($"SELECT COUNT(*) {from}", args));
        if (total == 0 || skip >= total)
        {
            return new OverviewPage(total, []);
        }

        return new OverviewPage(total, FetchRows(dbScope, from, args, skip, take));
    }

    public IReadOnlyList<LanguageTotal> Totals(Guid? siteRootKey, string? contentTypeAlias, StartNodeScope scope)
    {
        using var dbScope = scopeProvider.CreateScope(autoComplete: true);
        var (where, args) = OverviewSql.BuildWhere(new OverviewFilter(siteRootKey, contentTypeAlias, null, null, null, false), scope);
        var sql = dbScope.SqlContext.Sql(
            $"SELECT v.culture AS Culture, v.status AS Status, v.outdated AS Outdated, COUNT(*) AS Total " +
            $"FROM {VariantDto.TableName} v INNER JOIN {DocumentDto.TableName} d ON d.[key] = v.documentKey " +
            $"WHERE {where} GROUP BY v.culture, v.status, v.outdated",
            args);

        return OverviewSql.Aggregate(dbScope.Database.Fetch<TotalsRow>(sql));
    }

    /// <summary>Streams the filtered documents in tree order, one page of <see cref="ExportPageSize"/> at a time.</summary>
    public IEnumerable<OverviewRow> ExportRows(OverviewFilter filter, StartNodeScope scope, int max = 50_000)
    {
        var (where, args) = OverviewSql.BuildWhere(filter, scope);
        var from = $"FROM {DocumentDto.TableName} d WHERE {where}";

        var skip = 0;
        while (skip < max)
        {
            var take = Math.Min(ExportPageSize, max - skip);
            List<OverviewRow> page;
            using (var dbScope = scopeProvider.CreateScope(autoComplete: true))
            {
                page = FetchRows(dbScope, from, args, skip, take);
            }

            foreach (var row in page)
            {
                yield return row;
            }

            if (page.Count < take)
            {
                yield break;
            }

            skip += page.Count;
        }
    }

    private static List<OverviewRow> FetchRows(IScope dbScope, string from, object[] args, int skip, int take)
    {
        var documents = dbScope.Database.SkipTake<DocumentDto>(skip, take, dbScope.SqlContext.Sql($"{Select} {from} {OrderBy}", args));
        if (documents.Count == 0)
        {
            return [];
        }

        // One query for the languages of the whole page (at most ExportPageSize keys, within SQL Server's parameter limit).
        var variants = dbScope.Database.Fetch<VariantDto>(dbScope.SqlContext.Sql()
                .Select<VariantDto>().From<VariantDto>()
                .WhereIn<VariantDto>(x => x.DocumentKey, documents.Select(d => d.Key).ToList())
                .OrderBy<VariantDto>(x => x.Culture))
            .GroupBy(v => v.DocumentKey)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<VariantRow>)g.Select(StatusRowMapping.ToRow).ToList());

        return documents
            .Select(d => new OverviewRow(StatusRowMapping.ToRow(d), variants.TryGetValue(d.Key, out var rows) ? rows : []))
            .ToList();
    }
}

internal class TotalsRow
{
    public string Culture { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public bool Outdated { get; set; }

    public int Total { get; set; }
}

/// <summary>The SQL of the overview, built without a database so it can be tested. Table alias <c>d</c> is the document, <c>v</c> a variant.</summary>
internal static class OverviewSql
{
    private const string Variants = "dynamicDaveTranslationHealthVariant";

    public static (string Where, object[] Args) BuildWhere(OverviewFilter filter, StartNodeScope scope)
    {
        var parts = new List<string>();
        var args = new List<object>();

        string Arg(object value)
        {
            args.Add(value);
            return "@" + (args.Count - 1);
        }

        if (filter.SiteRootKey is { } site)
        {
            parts.Add($"d.siteRootKey = {Arg(site)}");
        }

        if (!string.IsNullOrWhiteSpace(filter.ContentTypeAlias))
        {
            parts.Add($"d.contentTypeAlias = {Arg(filter.ContentTypeAlias)}");
        }

        // Root access (-1) and no scope at all see everything; an empty list sees nothing.
        if (scope.StartNodeIds is { } ids && !ids.Contains(-1))
        {
            if (ids.Count == 0)
            {
                parts.Add("1 = 0");
            }
            else
            {
                var any = ids.Distinct().Select(id => $"(d.path LIKE {Arg($"%,{id},%")} OR d.path LIKE {Arg($"%,{id}")})");
                parts.Add("(" + string.Join(" OR ", any) + ")");
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.NameSearch))
        {
            var pattern = Arg("%" + EscapeLike(filter.NameSearch.Trim()) + "%");
            parts.Add(
                $"(d.name LIKE {pattern} ESCAPE '\\' OR EXISTS (SELECT 1 FROM {Variants} v WHERE v.documentKey = d.[key] AND v.name LIKE {pattern} ESCAPE '\\'))");
        }

        var culture = string.IsNullOrWhiteSpace(filter.Culture) ? null : filter.Culture;
        if (filter.Status is { } status)
        {
            parts.Add(Exists(status, culture, args));
        }

        if (filter.OnlyIssues)
        {
            parts.Add(Exists(CellFilter.AnyIssue, culture, args));
        }

        return (parts.Count == 0 ? "1 = 1" : string.Join(" AND ", parts), args.ToArray());
    }

    // Not applicable is never an issue: the status checks name the three issue statuses, and "outdated" excludes it explicitly.
    private static string Exists(CellFilter filter, string? culture, List<object> args)
    {
        string Arg(object value)
        {
            args.Add(value);
            return "@" + (args.Count - 1);
        }

        var sql = new StringBuilder($"EXISTS (SELECT 1 FROM {Variants} v WHERE v.documentKey = d.[key]");
        if (culture is not null)
        {
            sql.Append($" AND v.culture = {Arg(culture)}");
        }

        string Single(CellStatus s) => $" AND v.status = {Arg(s.ToString())}";
        string Outdated() => $"v.outdated = {Arg(true)} AND v.status <> {Arg(nameof(CellStatus.NotApplicable))}";

        sql.Append(filter switch
        {
            CellFilter.Missing => Single(CellStatus.Missing),
            CellFilter.Draft => Single(CellStatus.Draft),
            CellFilter.PendingChanges => Single(CellStatus.PendingChanges),
            CellFilter.Outdated => " AND " + Outdated(),
            _ => $" AND (v.status IN ({Arg(nameof(CellStatus.Missing))}, {Arg(nameof(CellStatus.Draft))}, {Arg(nameof(CellStatus.PendingChanges))}) OR ({Outdated()}))",
        });
        sql.Append(')');
        return sql.ToString();
    }

    // SQL Server treats [ as the start of a character class, so it is escaped with the others.
    internal static string EscapeLike(string value) => value
        .Replace(@"\", @"\\")
        .Replace("%", @"\%")
        .Replace("_", @"\_")
        .Replace("[", @"\[");

    internal static IReadOnlyList<LanguageTotal> Aggregate(IEnumerable<TotalsRow> rows) => rows
        .Where(r => r.Status != nameof(CellStatus.NotApplicable))
        .GroupBy(r => r.Culture)
        .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
        .Select(g => new LanguageTotal(
            g.Key,
            g.Sum(r => r.Total),
            g.Where(r => r.Status == nameof(CellStatus.Published) && !r.Outdated).Sum(r => r.Total),
            g.Where(r => r.Status == nameof(CellStatus.Missing)).Sum(r => r.Total),
            g.Where(r => r.Status == nameof(CellStatus.Draft)).Sum(r => r.Total),
            g.Where(r => r.Status == nameof(CellStatus.PendingChanges)).Sum(r => r.Total),
            g.Where(r => r.Outdated).Sum(r => r.Total)))
        .ToList();
}
