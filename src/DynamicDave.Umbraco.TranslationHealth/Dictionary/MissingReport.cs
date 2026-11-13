namespace DynamicDave.Umbraco.TranslationHealth.Dictionary;

public sealed record MissingItem(Guid Id, string Key, string Group, IReadOnlyList<string> MissingCultures);

public sealed record MissingCount(string Culture, int Missing);

public static class MissingReport
{
    /// <summary>
    /// Counts and lists the items that lack a value. <paramref name="group"/> matches that group or any deeper one,
    /// <paramref name="keySearch"/> is a substring of the key; both ignore case. The counts ignore <paramref name="culture"/>
    /// (so every language keeps its number) but respect group and search. An item lists every culture it lacks.
    /// </summary>
    public static (IReadOnlyList<MissingCount> Counts, IReadOnlyList<MissingItem> Items) Build(
        IReadOnlyList<DictionaryEntry> entries,
        IReadOnlyList<string> cultures,
        string? culture,
        string? group,
        string? keySearch)
    {
        var byId = entries.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
        var counts = cultures.ToDictionary(c => c, _ => 0, StringComparer.OrdinalIgnoreCase);
        var items = new List<MissingItem>();

        foreach (var entry in entries)
        {
            if (!string.IsNullOrEmpty(keySearch) && !entry.Key.Contains(keySearch, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var groupName = DictionaryGrouping.GroupName(DictionaryGrouping.GroupPath(entry, byId));
            if (!string.IsNullOrEmpty(group) && !InGroup(groupName, group))
            {
                continue;
            }

            var missing = cultures.Where(c => !entry.Values.TryGetValue(c, out var v) || string.IsNullOrWhiteSpace(v)).ToList();
            foreach (var c in missing)
            {
                counts[c]++;
            }

            if (missing.Count == 0 || (!string.IsNullOrEmpty(culture) && !missing.Contains(culture, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            items.Add(new MissingItem(entry.Id, entry.Key, groupName, missing));
        }

        var sorted = items
            .OrderBy(i => i.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return (cultures.Select(c => new MissingCount(c, counts[c])).ToList(), sorted);
    }

    internal static bool InGroup(string groupName, string group) =>
        groupName.Equals(group, StringComparison.OrdinalIgnoreCase)
        || groupName.StartsWith(group + ".", StringComparison.OrdinalIgnoreCase);
}
