using DynamicDave.Umbraco.TranslationHealth.Dictionary;
using DynamicDave.Umbraco.TranslationHealth.Persistence;

namespace DynamicDave.Umbraco.TranslationHealth.Importing;

public enum ApplyOutcome { Applied, Changed, Skipped }

public sealed record AppliedItem(int ItemId, ApplyOutcome Outcome);

public sealed record ApplyResult(Guid ImportKey, int Applied, int Skipped, IReadOnlyList<AppliedItem> Items);

/// <summary>Writes the selected items of a test-run plan to the dictionary and records them in the import history.</summary>
public sealed class ImportApplier(IDictionarySource dictionary, IImportHistory history, TimeProvider time)
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
        var byKey = new Dictionary<string, DictionaryEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            byKey.TryAdd(entry.Key.Trim(), entry);
        }

        void MarkAll(IEnumerable<PlanItem> group, ApplyOutcome outcome)
        {
            foreach (var item in group)
            {
                outcomes[item.Id] = outcome;
            }
        }

        try
        {
            foreach (var group in writable.Where(i => i.Outcome != ImportOutcome.NewKey).GroupBy(i => i.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (!byKey.TryGetValue(group.Key.Trim(), out var entry))
                {
                    MarkAll(group, ApplyOutcome.Changed);
                    continue;
                }

                var toWrite = new List<(PlanItem Item, string? Old)>();
                foreach (var item in group)
                {
                    var current = Lookup(entry.Values, item.Culture);
                    if (ImportPlanner.SameValue(current, item.CurrentValue))
                    {
                        toWrite.Add((item, current));
                    }
                    else
                    {
                        outcomes[item.Id] = ApplyOutcome.Changed;
                    }
                }

                if (toWrite.Count == 0)
                {
                    continue;
                }

                await dictionary.SetValuesAsync(entry.Id, ToValues(toWrite.Select(w => w.Item)), userKey);
                foreach (var (item, old) in toWrite)
                {
                    outcomes[item.Id] = ApplyOutcome.Applied;
                    rows.Add(new ImportRowRecord(entry.Key, item.Culture, old, item.NewValue, false));
                }
            }

            // Shorter keys first, so a created parent is available to the keys below it.
            foreach (var group in writable.Where(i => i.Outcome == ImportOutcome.NewKey)
                         .GroupBy(i => i.Key, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(g => g.Key.Length).ThenBy(g => g.Min(i => i.Id)))
            {
                if (byKey.ContainsKey(group.Key.Trim()))
                {
                    MarkAll(group, ApplyOutcome.Changed);
                    continue;
                }

                var parent = DictionaryGrouping.ParentForNewKey(group.Key, entries);
                var values = ToValues(group);
                var id = await dictionary.CreateAsync(group.Key, parent, values, userKey);
                var created = new DictionaryEntry(id, group.Key, parent, values);
                entries.Add(created);
                byKey[group.Key.Trim()] = created;
                foreach (var item in group)
                {
                    outcomes[item.Id] = ApplyOutcome.Applied;
                    rows.Add(new ImportRowRecord(group.Key, item.Culture, null, item.NewValue, true));
                }
            }
        }
        finally
        {
            // Whatever was written stays undoable, also when a later write failed.
            if (rows.Count > 0)
            {
                history.Save(
                    new ImportRecord(importKey, createdUtc, userKey, Truncate(userName), Truncate(plan.FileName), plan.Format.ToString(),
                        plan.Options.Overwrite, plan.Options.CreateKeys, rows.Count, plan.Items.Count - rows.Count,
                        ImportState.Applied, null, null),
                    rows);
            }
        }

        var items = plan.Items.Select(i => new AppliedItem(i.Id, outcomes.GetValueOrDefault(i.Id, ApplyOutcome.Skipped))).ToList();
        return new ApplyResult(importKey, rows.Count, plan.Items.Count - rows.Count, items);
    }

    internal static string Truncate(string value) => value.Length <= MaxText ? value : value[..MaxText];

    internal static string? Lookup(IReadOnlyDictionary<string, string> values, string culture)
    {
        foreach (var (name, value) in values)
        {
            if (string.Equals(name, culture, StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }

    private static Dictionary<string, string> ToValues(IEnumerable<PlanItem> items)
        => items.ToDictionary(i => i.Culture, i => i.NewValue, StringComparer.OrdinalIgnoreCase);
}
