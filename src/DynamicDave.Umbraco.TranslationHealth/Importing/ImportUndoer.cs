using DynamicDave.Umbraco.TranslationHealth.Dictionary;
using DynamicDave.Umbraco.TranslationHealth.Persistence;
using Microsoft.Extensions.Logging;

namespace DynamicDave.Umbraco.TranslationHealth.Importing;

public sealed record UndoResult(int Restored, int KeysRemoved, IReadOnlyList<ImportRowRecord> LeftAlone);

/// <summary>Reverts an applied import, leaving alone everything that was edited after it.</summary>
public sealed class ImportUndoer(IDictionarySource dictionary, IImportHistory history, TimeProvider time, ILogger<ImportUndoer> logger)
{
    public async Task<UndoResult> UndoAsync(Guid importKey, Guid userKey, string userName)
    {
        // Claim the import first: it can be undone once, also when two requests race. If restoring fails
        // midway, the import stays marked undone and the remaining rows are not retried.
        if (!history.MarkUndone(importKey, ImportApplier.Truncate(userName), time.GetUtcNow().UtcDateTime))
        {
            throw new ImportAlreadyUndoneException();
        }

        var progress = new Progress();
        try
        {
            return await RestoreAsync(importKey, userKey, progress);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Undoing import {ImportKey} failed after restoring {Restored} values and removing {Removed} keys; it stays marked undone",
                importKey, progress.Restored, progress.Removed);
            throw;
        }
    }

    private async Task<UndoResult> RestoreAsync(Guid importKey, Guid userKey, Progress progress)
    {
        var rows = history.Rows(importKey);
        var byKey = new Dictionary<string, DictionaryEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in dictionary.GetAll())
        {
            byKey.TryAdd(entry.Key.Trim(), entry);
        }

        var leftAlone = new List<ImportRowRecord>();

        foreach (var group in rows.Where(r => !r.KeyCreated).GroupBy(r => r.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!byKey.TryGetValue(group.Key.Trim(), out var entry))
            {
                leftAlone.AddRange(group);
                continue;
            }

            var result = await dictionary.SetValuesAsync(entry.Id,
                group.Select(r => new ValueChange(r.Culture, r.NewValue, r.OldValue ?? string.Empty)).ToList(), userKey);
            progress.Restored += result.Written.Count;
            leftAlone.AddRange(group.Where(r => result.Conflicts.Contains(r.Culture, StringComparer.OrdinalIgnoreCase)));
        }

        // Longest keys first, so created children go before the parents they sit under.
        foreach (var group in rows.Where(r => r.KeyCreated).GroupBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(g => g.Key.Length))
        {
            // The entry is read again right before deleting: it may have been edited or given children since.
            if (!byKey.TryGetValue(group.Key.Trim(), out var known)
                || await dictionary.GetAsync(known.Id) is not { } fresh
                || !Untouched(fresh, group.ToList())
                || await dictionary.HasChildrenAsync(fresh.Id))
            {
                leftAlone.AddRange(group);
                continue;
            }

            await dictionary.DeleteAsync(fresh.Id, userKey);
            progress.Removed++;
        }

        return new UndoResult(progress.Restored, progress.Removed, leftAlone);
    }

    private static bool Untouched(DictionaryEntry entry, List<ImportRowRecord> imported)
        => entry.Values.Count(v => !string.IsNullOrWhiteSpace(v.Value)) == imported.Count
            && imported.All(r => DictionaryValues.SameValue(DictionaryValues.NonEmpty(entry.Values, r.Culture), r.NewValue));

    private sealed class Progress
    {
        public int Restored { get; set; }

        public int Removed { get; set; }
    }
}
