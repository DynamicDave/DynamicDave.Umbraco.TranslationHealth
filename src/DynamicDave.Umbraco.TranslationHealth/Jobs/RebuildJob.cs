using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DynamicDave.Umbraco.TranslationHealth.Content;
using DynamicDave.Umbraco.TranslationHealth.Indexing;
using DynamicDave.Umbraco.TranslationHealth.Persistence;
using DynamicDave.Umbraco.TranslationHealth.Sites;
using DynamicDave.Umbraco.TranslationHealth.Status;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Services;

namespace DynamicDave.Umbraco.TranslationHealth.Jobs;

/// <summary>
/// Re-indexes every culture-variant document, then removes the rows of documents that no longer qualify. A rebuild keeps
/// existing baselines and marks (<see cref="IndexMode.Rebuild"/>) unless the default language changed
/// (<see cref="IndexMode.RebuildClearBaselines"/>). Documents indexed by the worker while it runs are left alone.
/// </summary>
public sealed class RebuildJob(
    IContentSnapshotSource snapshots,
    Reindexer reindexer,
    IKeyValueService keyValues,
    ISiteSource sites,
    IOptionsMonitor<TranslationHealthOptions> options,
    TimeProvider timeProvider)
{
    public const int PageSize = 200;

    // Documents created or deleted while paging shift the pages; a second pass picks up what a shift skipped.
    private const int MaxPasses = 3;

    /// <summary>Fingerprint of the configured excluded document types: changes when the list changes, not with its order or casing.</summary>
    public static string ExcludedTypesHash(IEnumerable<string> aliases)
    {
        var normalized = aliases
            .Select(alias => alias.Trim().ToLowerInvariant())
            .Where(alias => alias.Length > 0)
            .Distinct()
            .Order(StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', normalized))))[..16];
    }

    public Task<(Guid? ResultKey, TempKind? ResultKind, string? Summary)> RunAsync(
        IndexMode mode, JobProgress progress, CancellationToken cancellationToken)
    {
        if (mode == IndexMode.Save)
        {
            throw new ArgumentException("A rebuild runs in a rebuild mode.", nameof(mode));
        }

        // What the rebuild is based on, stored once it is complete.
        var defaultCulture = sites.DefaultCulture();
        var excludedHash = ExcludedTypesHash(options.CurrentValue.ExcludedDocumentTypes);

        using var tracking = reindexer.BeginTracking();
        var seen = new HashSet<Guid>();
        for (var pass = 0; pass < MaxPasses; pass++)
        {
            if (!IndexAll(mode, seen, progress, cancellationToken))
            {
                break;
            }
        }

        // Only a complete pass gets here (an exception or a cancel skipped this), so the rows left over are orphans.
        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(seen.Count, seen.Count, JobPhases.RemovingOrphans);
        reindexer.RemoveOrphans(seen, tracking);

        keyValues.SetValue(Constants.DefaultCultureKey, defaultCulture);
        keyValues.SetValue(Constants.ExcludedTypesKey, excludedHash);
        keyValues.SetValue(Constants.LastRebuildKey, timeProvider.GetUtcNow().UtcDateTime.ToString("o"));

        var summary = JsonSerializer.Serialize(new { documents = seen.Count });
        return Task.FromResult<(Guid?, TempKind?, string?)>((null, null, summary));
    }

    /// <summary>One pass over all pages; true when the number of documents changed meanwhile, so another pass is worth it.</summary>
    private bool IndexAll(IndexMode mode, HashSet<Guid> seen, JobProgress progress, CancellationToken cancellationToken)
    {
        long? firstTotal = null;
        var changed = false;
        for (long page = 0; ; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var documents = snapshots.GetPage(page, PageSize, out var total);
            firstTotal ??= total;
            changed |= total != firstTotal;

            foreach (var document in documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (seen.Add(document.Key))
                {
                    reindexer.Reindex(document, mode);
                }
            }

            progress.Report(seen.Count, (int)Math.Max(total, seen.Count), JobPhases.Indexing);
            if (documents.Count < PageSize || (page + 1) * PageSize >= total)
            {
                return changed;
            }
        }
    }
}
