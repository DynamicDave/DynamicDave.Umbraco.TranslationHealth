using DynamicDave.Umbraco.TranslationHealth.Content;

namespace DynamicDave.Umbraco.TranslationHealth.Tests.Fakes;

/// <summary>Documents in tree order, served like the Umbraco source: <c>Get</c> and pages of snapshots.</summary>
internal sealed class FakeSnapshotSource : IContentSnapshotSource
{
    private readonly object _gate = new();
    private readonly List<DocumentSnapshot> _documents = [];

    public Dictionary<Guid, List<Guid>> Descendants { get; } = [];

    /// <summary>Runs in every <see cref="GetPage"/> after the page was read (outside the lock), with the number of the call (0 for the first): what the caller holds is then already stale.</summary>
    public Action<long>? OnGetPage { get; set; }

    /// <summary>Documents that <see cref="Get"/> returns but pages leave out, like documents created after the pager passed.</summary>
    public HashSet<Guid> HiddenFromPages { get; } = [];

    public int GetPageCalls { get; private set; }

    public int ClearCacheCalls { get; private set; }

    public IReadOnlyList<DocumentSnapshot> Documents
    {
        get
        {
            lock (_gate) return [.. _documents];
        }
    }

    public static DocumentSnapshot Doc(Guid key, int id, params CultureSnapshot[] cultures) =>
        new(key, id, null, $"-1,{id}", id.ToString("D5"), 1, "page", cultures);

    public void Add(DocumentSnapshot document)
    {
        lock (_gate) _documents.Add(document);
    }

    public void Remove(Guid key)
    {
        lock (_gate) _documents.RemoveAll(d => d.Key == key);
    }

    public void Replace(DocumentSnapshot document)
    {
        lock (_gate)
        {
            var index = _documents.FindIndex(d => d.Key == document.Key);
            _documents[index] = document;
        }
    }

    public DocumentSnapshot? Get(Guid key)
    {
        lock (_gate) return _documents.FirstOrDefault(d => d.Key == key);
    }

    public IReadOnlyList<DocumentSnapshot> GetPage(int afterId, int pageSize, out long remaining)
    {
        List<DocumentSnapshot> page;
        long call;
        lock (_gate)
        {
            call = GetPageCalls++;
            var above = _documents.Where(d => !HiddenFromPages.Contains(d.Key) && d.Id > afterId).OrderBy(d => d.Id).ToList();
            remaining = above.Count;
            page = above.Take(pageSize).ToList();
        }

        OnGetPage?.Invoke(call);
        return page;
    }

    public IReadOnlyList<Guid> DescendantKeys(Guid key) => Descendants.TryGetValue(key, out var keys) ? keys : [];

    public void ClearCache() => ClearCacheCalls++;
}
