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
/// (<see cref="IndexMode.RebuildClearBaselines"/>); a normal rebuild is upgraded to a clearing one when the default
/// language differs from the stored one (a clearing rebuild that failed leaves the old one stored). Documents indexed by
/// the worker while it runs are skipped by the pass; a clearing rebuild re-indexes them at the end, because the worker's
/// Save mode kept their old baselines. Before each write it waits for editors to pause (<see cref="EditorActivity"/>), at
/// most 30 seconds.
/// </summary>
public sealed class RebuildJob(
    IContentSnapshotSource snapshots,
    Reindexer reindexer,
    IKeyValueService keyValues,
    ISiteSource sites,
    IOptionsMonitor<TranslationHealthOptions> options,
    TimeProvider timeProvider,
    EditorActivity activity)
{
    public const int PageSize = 200;

    /// <summary>Documents per write transaction. Pages are read at <see cref="PageSize"/>, written in larger chunks: every commit can make a save of an editor on SQLite hang.</summary>
    public const int WriteChunkSize = 1000;

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
        if (mode == IndexMode.Rebuild
            && RebuildScheduler.DefaultCultureChanged(keyValues.GetValue(Constants.DefaultCultureKey), defaultCulture))
        {
            mode = IndexMode.RebuildClearBaselines;
        }

        var excludedHash = ExcludedTypesHash(options.CurrentValue.ExcludedDocumentTypes);

        using var tracking = reindexer.BeginTracking();
        var seen = new HashSet<Guid>();
        var pending = new List<DocumentSnapshot>();
        var afterId = int.MinValue;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var documents = snapshots.GetPage(afterId, PageSize, out var remaining);
            var total = seen.Count + remaining;
            seen.UnionWith(documents.Select(document => document.Key));
            pending.AddRange(documents);
            var last = documents.Count < PageSize || documents.Count >= remaining;
            if (documents.Count > 0)
            {
                afterId = documents[^1].Id;
            }

            if (pending.Count >= WriteChunkSize || last)
            {
                // A snapshot that is older by a few pages is fine: documents handled since BeginTracking are skipped.
                activity.WaitUntilQuiet(cancellationToken);
                reindexer.IndexPage(pending, mode);
                pending = [];
            }

            progress.Report(seen.Count, (int)Math.Max(total, seen.Count), JobPhases.Indexing);
            if (last)
            {
                break;
            }
        }

        // Only a complete pass gets here (an exception or a cancel skipped this). Paging is by id (keyset), so the pass saw every
        // document that lived throughout. An unseen document with rows can still be live (restored from the recycle bin after
        // the pass got past its id, or of a type included meanwhile), so the rows of unseen documents are checked again, not just deleted.
        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(seen.Count, seen.Count, JobPhases.RemovingOrphans);
        activity.WaitUntilQuiet(cancellationToken);
        reindexer.RemoveOrphans(seen, tracking, mode, () => activity.WaitUntilQuiet(cancellationToken));

        // The worker indexed some documents meanwhile in Save mode, which keeps the baselines of the previous rows, and the
        // pass skipped them. Once their rows are cleared, a later save cannot bring an old baseline back: Save mode takes
        // the baseline from the (cleared) previous rows or from the current default-language text.
        if (mode == IndexMode.RebuildClearBaselines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reindexer.ReindexTracked(tracking, mode, () => activity.WaitUntilQuiet(cancellationToken));
        }

        // IKeyValueService has no bulk write: every key is its own commit, so each waits for editors too.
        activity.WaitUntilQuiet(cancellationToken);
        keyValues.SetValue(Constants.DefaultCultureKey, defaultCulture);
        activity.WaitUntilQuiet(cancellationToken);
        keyValues.SetValue(Constants.ExcludedTypesKey, excludedHash);
        activity.WaitUntilQuiet(cancellationToken);
        keyValues.SetValue(Constants.LastRebuildKey, timeProvider.GetUtcNow().UtcDateTime.ToString("o"));

        var summary = JsonSerializer.Serialize(new { documents = seen.Count });
        return Task.FromResult<(Guid?, TempKind?, string?)>((null, null, summary));
    }
}
