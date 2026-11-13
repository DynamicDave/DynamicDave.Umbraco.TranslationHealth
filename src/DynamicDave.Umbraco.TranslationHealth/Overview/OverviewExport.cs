using System.Globalization;
using DynamicDave.Umbraco.TranslationHealth.Files;
using DynamicDave.Umbraco.TranslationHealth.Status;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace DynamicDave.Umbraco.TranslationHealth.Overview;

/// <summary>The overview as CSV: one row per page, per language its status and the date it went outdated.</summary>
public static class OverviewExport
{
    /// <summary>
    /// <paramref name="cultures"/> lists the default language first (the source: it is never outdated, so it prints no date).
    /// A language a page has no row for prints as "Not applicable".
    /// </summary>
    public static byte[] Csv(
        IEnumerable<OverviewRow> rows,
        IReadOnlyList<string> cultures,
        Func<DocumentRow, string> breadcrumb,
        Func<Guid, string> backofficeUrl)
    {
        var header = new List<string> { "Name", "Path", "Document type" };
        header.AddRange(cultures);
        header.AddRange(cultures.Select(c => $"{c} outdated since"));
        header.Add("Backoffice URL");

        return CsvParser.Write(header, rows.Select(row =>
        {
            var line = new List<string> { row.Document.Name, breadcrumb(row.Document), row.Document.ContentTypeAlias };
            var byCulture = row.Variants.ToDictionary(v => v.Culture, StringComparer.OrdinalIgnoreCase);
            var since = new List<string>();
            for (var i = 0; i < cultures.Count; i++)
            {
                if (!byCulture.TryGetValue(cultures[i], out var variant))
                {
                    line.Add(Word(CellStatus.NotApplicable));
                    since.Add(string.Empty);
                    continue;
                }

                var outdated = i > 0 && variant.Outdated && variant.Status != CellStatus.NotApplicable;
                line.Add(outdated ? $"Outdated ({Word(variant.Status)})" : Word(variant.Status));
                since.Add(outdated && variant.OutdatedSinceUtc is { } date
                    ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : string.Empty);
            }

            line.AddRange(since);
            line.Add(backofficeUrl(row.Document.Key));
            return (IReadOnlyList<string>)line;
        }));
    }

    internal static string Word(CellStatus status) => status switch
    {
        CellStatus.NotApplicable => "Not applicable",
        CellStatus.Missing => "Missing",
        CellStatus.Draft => "Draft",
        CellStatus.Published => "Published",
        CellStatus.PendingChanges => "Pending changes",
        _ => status.ToString(),
    };
}

/// <summary>
/// Names the ancestors of a document ("Home / About") through <see cref="IEntityService"/>. <see cref="Prime"/> fetches the names
/// of a whole batch of documents in one call; names are cached for the lifetime of the instance, so use one per export.
/// </summary>
public sealed class BreadcrumbResolver(IEntityService entityService)
{
    private readonly Dictionary<int, string> _names = [];

    /// <summary>Loads the ancestor names of these documents that are not known yet, in one call.</summary>
    public void Prime(IEnumerable<DocumentRow> documents)
    {
        var missing = documents
            .SelectMany(d => AncestorIds(d.Path))
            .Where(id => !_names.ContainsKey(id))
            .Distinct()
            .ToArray();
        if (missing.Length == 0)
        {
            return;
        }

        foreach (var entity in entityService.GetAll(UmbracoObjectTypes.Document, missing))
        {
            _names[entity.Id] = entity.Name ?? string.Empty;
        }

        // Ancestors that no longer exist are remembered as unknown, so they are not asked for again.
        foreach (var id in missing)
        {
            _names.TryAdd(id, string.Empty);
        }
    }

    /// <summary>The ancestors of the document, root first, not including the document itself.</summary>
    public string Build(DocumentRow document) =>
        string.Join(" / ", AncestorIds(document.Path).Select(id => _names.GetValueOrDefault(id, string.Empty)).Where(n => n.Length > 0));

    /// <summary>Yields the rows unchanged, priming the names one chunk at a time so a streamed export makes one call per chunk.</summary>
    public IEnumerable<OverviewRow> Primed(IEnumerable<OverviewRow> rows, int chunkSize = OverviewQuery.ExportPageSize)
    {
        foreach (var chunk in rows.Chunk(chunkSize))
        {
            Prime(chunk.Select(r => r.Document));
            foreach (var row in chunk)
            {
                yield return row;
            }
        }
    }

    private static IEnumerable<int> AncestorIds(string path)
    {
        var ids = path.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : int.MinValue)
            .Where(id => id > 0)
            .ToList();
        return ids.Count == 0 ? ids : ids.Take(ids.Count - 1);
    }
}
