namespace DynamicDave.Umbraco.TranslationHealth.Sites;

public static class SiteResolver
{
    /// <summary>
    /// The site a document belongs to, from its Umbraco path ("-1,10,20,30"): the site with domains whose root is the
    /// deepest id in the path, else the site of the top-level ancestor (the second id). Null when none matches.
    /// </summary>
    public static SiteInfo? SiteFor(string path, IReadOnlyList<SiteInfo> sites)
    {
        var ids = new List<int>();
        foreach (var part in path.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out var id)) return null;
            ids.Add(id);
        }

        for (var i = ids.Count - 1; i >= 1; i--)
        {
            var site = sites.FirstOrDefault(s => s.HasDomains && s.RootId == ids[i]);
            if (site is not null) return site;
        }

        return ids.Count > 1 ? sites.FirstOrDefault(s => s.RootId == ids[1]) : null;
    }
}
