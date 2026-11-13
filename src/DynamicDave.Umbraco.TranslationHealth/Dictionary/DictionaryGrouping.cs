namespace DynamicDave.Umbraco.TranslationHealth.Dictionary;

public static class DictionaryGrouping
{
    /// <summary>
    /// The groups of an entry, outermost first. A dotted key groups by its segments except the last
    /// (<c>Footer.Contact.Phone</c> gives Footer, Contact), even when it also has a tree parent;
    /// other keys group by the keys of their ancestors in the core tree.
    /// </summary>
    public static IReadOnlyList<string> GroupPath(DictionaryEntry entry, IReadOnlyDictionary<Guid, DictionaryEntry> byId)
    {
        var segments = entry.Key.Split('.');
        if (segments.Length > 1)
        {
            return segments[..^1];
        }

        var path = new List<string>();
        var seen = new HashSet<Guid> { entry.Id };
        var parentId = entry.ParentId;
        while (parentId is { } id && seen.Add(id) && byId.TryGetValue(id, out var parent))
        {
            path.Insert(0, parent.Key);
            parentId = parent.ParentId;
        }

        return path;
    }

    public static string GroupName(IReadOnlyList<string> path) => string.Join('.', path);

    /// <summary>The id of the entry whose key is the longest proper dot-prefix of <paramref name="key"/>, or null.</summary>
    public static Guid? ParentForNewKey(string key, IReadOnlyList<DictionaryEntry> entries)
    {
        var lookup = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            lookup.TryAdd(entry.Key, entry.Id);
        }

        var end = key.LastIndexOf('.');
        while (end > 0)
        {
            if (lookup.TryGetValue(key[..end], out var id))
            {
                return id;
            }

            end = key.LastIndexOf('.', end - 1);
        }

        return null;
    }
}
