using DynamicDave.Umbraco.TranslationHealth.Dictionary;
using DynamicDave.Umbraco.TranslationHealth.Persistence;
using Microsoft.Extensions.Logging;

namespace DynamicDave.Umbraco.TranslationHealth.Importing;

public enum ApplyOutcome { Applied, Changed, Skipped }

public sealed record AppliedItem(int ItemId, ApplyOutcome Outcome);

public sealed record ApplyResult(Guid ImportKey, int Applied, int Skipped, IReadOnlyList<AppliedItem> Items);

/// <summary>Writes the selected items of a test-run plan to the dictionary and records them in the import history.</summary>
public sealed class ImportApplier(IDictionarySource dictionary, IImportHistory history, TimeProvider time, ILogger<ImportApplier> logger)
{
    internal const int MaxText = 255;

    public async Task<ApplyResult> ApplyAsync(ImportPlan plan, IReadOnlySet<int> selectedItemIds, Guid userKey, string userName)
    {
        var writable = plan.Items
            .Where(i => selectedItemIds.Contains(i.Id)
                && i.Outcome is ImportOutcome.NewValue or ImportOutcome.Overwrite or ImportOutcome.NewKey)
            .ToList();
        var outcomes = new Dictionary<int, ApplyOutcome>();
        var rows = new List<ImportRowRecord>();
        var importKey = Guid.NewGuid();
        var createdUtc = time.GetUtcNow().UtcDateTime;

        var entries = dictionary.GetAll().ToList();
        var existingKeys = new HashSet<string>(entries.Select(e => e.Key.Trim()), StringComparer.OrdinalIgnoreCase);

        Exception? writeError = null;
        try
        {
            // Existing entries: the source writes a culture only while it still holds the value the plan saw.
            foreach (var group in writable.Where(i => i.Outcome != ImportOutcome.NewKey)
                         .GroupBy(i => i.EntryId ?? Guid.Empty))
            {
                var items = group.ToList();
                if (group.Key == Guid.Empty)
                {
                    foreach (var item in items)
                    {
                        outcomes[item.Id] = ApplyOutcome.Changed;
                    }

                    continue;
                }

                var result = await dictionary.SetValuesAsync(group.Key,
                    items.Select(i => new ValueChange(i.Culture, i.CurrentValue, i.NewValue)).ToList(), userKey);
                foreach (var item in items)
                {
                    var written = result.Written.FirstOrDefault(w => string.Equals(w.Culture, item.Culture, StringComparison.OrdinalIgnoreCase));
                    if (written is null)
                    {
                        outcomes[item.Id] = ApplyOutcome.Changed;
                        continue;
                    }

                    outcomes[item.Id] = ApplyOutcome.Applied;
                    rows.Add(new ImportRowRecord(item.Key, item.Culture, written.OldValue, item.NewValue, false));
                }
            }

            // Shorter keys first, so a created parent is available to the keys below it.
            foreach (var group in writable.Where(i => i.Outcome == ImportOutcome.NewKey)
                         .GroupBy(i => i.Key, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(g => g.Key.Length).ThenBy(g => g.Min(i => i.Id)))
            {
                if (existingKeys.Contains(group.Key.Trim()))
                {
                    foreach (var item in group)
                    {
                        outcomes[item.Id] = ApplyOutcome.Changed;
                    }

                    continue;
                }

                var parent = DictionaryGrouping.ParentForNewKey(group.Key, entries);
                var values = group.ToDictionary(i => i.Culture, i => i.NewValue, StringComparer.OrdinalIgnoreCase);
                var id = await dictionary.CreateAsync(group.Key, parent, values, userKey);
                entries.Add(new DictionaryEntry(id, group.Key, parent, values));
                existingKeys.Add(group.Key.Trim());
                foreach (var item in group)
                {
                    outcomes[item.Id] = ApplyOutcome.Applied;
                    rows.Add(new ImportRowRecord(group.Key, item.Culture, null, item.NewValue, true));
                }
            }
        }
        catch (Exception ex)
        {
            writeError = ex;
            throw;
        }
        finally
        {
            // Whatever was written stays undoable, also when a later write failed.
            if (rows.Count > 0)
            {
                try
                {
                    history.Save(
                        new ImportRecord(importKey, createdUtc, userKey, Truncate(userName), Truncate(plan.FileName), plan.Format.ToString(),
                            plan.Options.Overwrite, plan.Options.CreateKeys, rows.Count, plan.Items.Count - rows.Count,
                            ImportState.Applied, null, null),
                        rows);
                }
                catch (Exception saveError)
                {
                    logger.LogError(saveError,
                        "Saving the history of import {ImportKey} failed after {Written} dictionary values were written: {Values}",
                        importKey, rows.Count, string.Join(", ", rows.Select(r => r.Key + " [" + r.Culture + "]")));
                    if (writeError is null)
                    {
                        throw;
                    }
                }
            }
        }

        var applied = plan.Items.Select(i => new AppliedItem(i.Id, outcomes.GetValueOrDefault(i.Id, ApplyOutcome.Skipped))).ToList();
        return new ApplyResult(importKey, rows.Count, plan.Items.Count - rows.Count, applied);
    }

    internal static string Truncate(string value) => value.Length <= MaxText ? value : value[..MaxText];
}
