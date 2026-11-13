using DynamicDave.Umbraco.TranslationHealth.Dictionary;
using DynamicDave.Umbraco.TranslationHealth.Persistence;

namespace DynamicDave.Umbraco.TranslationHealth.Importing;

public sealed record UndoResult(int Restored, int KeysRemoved, IReadOnlyList<ImportRowRecord> LeftAlone);

/// <summary>Reverts an applied import, leaving alone everything that was edited after it.</summary>
public sealed class ImportUndoer(IDictionarySource dictionary, IImportHistory history, TimeProvider time)
{
    public async Task<UndoResult> UndoAsync(Guid importKey, Guid userKey, string userName)
    {
        // Claim the import first: it can be undone once, also when two requests race. If restoring fails
        // midway, the import stays marked undone and the remaining rows are not retried.
        if (!history.MarkUndone(importKey, ImportApplier.Truncate(userName), time.GetUtcNow().UtcDateTime))
        {
            throw new InvalidOperationException("The import is unknown or has already been undone.");
        }

        var rows = history.Rows(importKey);
        var entries = dictionary.GetAll().ToList();
        var byKey = new Dictionary<string, DictionaryEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            byKey.TryAdd(entry.Key.Trim(), entry);
        }

        var leftAlone = new List<ImportRowRecord>();
        var restored = 0;
        var removed = 0;

        foreach (var group in rows.Where(r => !r.KeyCreated).GroupBy(r => r.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!byKey.TryGetValue(group.Key.Trim(), out var entry))
            {
                leftAlone.AddRange(group);
                continue;
            }

            var restore = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in group)
            {
                if (ImportPlanner.SameValue(ImportApplier.Lookup(entry.Values, row.Culture), row.NewValue))
                {
                    restore[row.Culture] = row.OldValue ?? string.Empty;
                }
                else
                {
                    leftAlone.Add(row);
                }
            }

            if (restore.Count > 0)
            {
                await dictionary.SetValuesAsync(entry.Id, restore, userKey);
                restored += restore.Count;
            }
        }

        // Longest keys first, so created children go before the parents they sit under.
        foreach (var group in rows.Where(r => r.KeyCreated).GroupBy(r => r.Key, StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(g => g.Key.Length))
        {
            if (!byKey.TryGetValue(group.Key.Trim(), out var entry) || !Untouched(entry, group.ToList(), entries))
            {
                leftAlone.AddRange(group);
                continue;
            }

            await dictionary.DeleteAsync(entry.Id, userKey);
            entries.RemoveAll(e => e.Id == entry.Id);
            removed++;
        }

        return new UndoResult(restored, removed, leftAlone);
    }

    private static bool Untouched(DictionaryEntry entry, List<ImportRowRecord> imported, List<DictionaryEntry> entries)
    {
        if (entries.Any(e => e.ParentId == entry.Id))
        {
            return false;
        }

        var currentCount = entry.Values.Count(v => !string.IsNullOrWhiteSpace(v.Value));
        return currentCount == imported.Count
            && imported.All(r => ImportPlanner.SameValue(ImportApplier.Lookup(entry.Values, r.Culture), r.NewValue));
    }
}
