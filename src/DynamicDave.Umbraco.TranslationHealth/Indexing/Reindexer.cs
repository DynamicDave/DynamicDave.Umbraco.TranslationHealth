using DynamicDave.Umbraco.TranslationHealth.Content;
using DynamicDave.Umbraco.TranslationHealth.Persistence;
using DynamicDave.Umbraco.TranslationHealth.Sites;
using DynamicDave.Umbraco.TranslationHealth.Status;

namespace DynamicDave.Umbraco.TranslationHealth.Indexing;

/// <summary>
/// Writes the status rows of documents. Storing a document replaces its rows after the previous rows were read (baselines
/// and outdated-since dates carry over), so every read-then-write runs under one lock: the worker, a running rebuild and
/// the mark endpoint never interleave for a document.
/// </summary>
public sealed class Reindexer(
    IContentSnapshotSource snapshots,
    ISiteSource sites,
    ITranslationStatusStore store,
    TimeProvider timeProvider)
{
    private readonly object _gate = new();
    private readonly List<KeyTracker> _trackers = [];

    /// <summary>Re-indexes a document from its current state; removes its rows when it is gone, trashed or not variant.</summary>
    public void Reindex(Guid key, IndexMode mode) => IndexBatch([(key, false)], mode);

    public void Remove(Guid key) => IndexBatch([(key, true)], IndexMode.Save);

    /// <summary>
    /// Handles a batch of queued documents with one write transaction: the snapshots and previous rows are read first, the
    /// rows computed, and everything is written at once (fewer commits matter on SQLite, where every commit of this
    /// package can stall a read-then-write transaction of Umbraco). A document that is gone, trashed or not variant is removed.
    /// </summary>
    public void IndexBatch(IReadOnlyList<(Guid Key, bool Remove)> items, IndexMode mode)
    {
        lock (_gate)
        {
            var removals = new List<Guid>();
            var snapshotsToIndex = new List<DocumentSnapshot>();
            foreach (var (key, remove) in items)
            {
                Touched(key);

                // Read inside the lock, so a snapshot is never older than the rows it replaces.
                var snapshot = remove ? null : snapshots.Get(key);
                if (snapshot is null)
                {
                    removals.Add(key);
                }
                else
                {
                    snapshotsToIndex.Add(snapshot);
                }
            }

            Write(Compute(snapshotsToIndex, mode), removals);
        }
    }

    /// <summary>
    /// Re-indexes the documents of a rebuild page, read earlier by the caller, with one write. A document that was
    /// re-indexed or removed since <see cref="BeginTracking"/> is skipped: that happened after the snapshot was read or
    /// will be repeated, and the snapshot may be older.
    /// </summary>
    public void IndexPage(IReadOnlyList<DocumentSnapshot> page, IndexMode mode)
    {
        lock (_gate)
        {
            var current = page.Where(snapshot => !_trackers.Any(tracker => tracker.Contains(snapshot.Key))).ToList();
            Write(Compute(current, mode), []);
        }
    }

    /// <summary>
    /// Records <paramref name="culture"/> of the document as up to date with the stored default-language fingerprint.
    /// The stored fingerprint is used rather than a fresh snapshot: it is what the status the user looked at was based on,
    /// and a save that is still queued marks the row outdated again once it is indexed.
    /// </summary>
    /// <param name="requireOutdated">Refuse a language that is not outdated; checked under the lock, so it cannot change in between.</param>
    /// <exception cref="InvalidOperationException">The row does not exist, is not created, belongs to the default language, or is not outdated while <paramref name="requireOutdated"/> is set.</exception>
    public VariantRow MarkUpToDate(Guid key, string culture, Guid userKey, bool requireOutdated = false)
    {
        lock (_gate)
        {
            var rows = store.GetVariants(key);
            var defaultCulture = sites.DefaultCulture();
            var row = rows.FirstOrDefault(r => Same(r.Culture, culture));
            if (row is null || Same(row.Culture, defaultCulture) || row.Status is CellStatus.Missing or CellStatus.NotApplicable
                || (requireOutdated && !row.Outdated))
            {
                throw new InvalidOperationException($"Language '{culture}' of document {key} cannot be marked up to date.");
            }

            var fingerprint = rows.FirstOrDefault(r => Same(r.Culture, defaultCulture))?.TextFingerprint
                ?? throw new InvalidOperationException($"Document {key} has no text in its default language to compare with.");

            var marked = StatusIndexer.MarkUpToDate(row, fingerprint, userKey, Now());
            store.SaveVariant(marked);
            return marked;
        }
    }

    /// <summary>
    /// Starts collecting the keys that <see cref="Reindex(Guid, IndexMode)"/> and <see cref="Remove"/> touch, until the
    /// result is disposed. A rebuild uses it so documents handled while it ran are neither overwritten with an older
    /// snapshot nor removed as orphans.
    /// </summary>
    public KeyTracker BeginTracking()
    {
        var tracker = new KeyTracker(this);
        lock (_gate) _trackers.Add(tracker);
        return tracker;
    }

    /// <summary>
    /// Handles the stored documents that a rebuild did not see and that were not touched while <paramref name="tracker"/>
    /// was active. Paging can skip live documents when others are deleted meanwhile, so each candidate is looked up
    /// again: gone documents lose their rows, live ones are re-indexed. Works in chunks of <see cref="OrphanChunkSize"/>
    /// with the lock taken per chunk (a content load per candidate must not block the worker for long), one write per chunk.
    /// </summary>
    /// <param name="beforeWrite">Called before each chunk that writes (e.g. to wait for editors to pause); a throw stops the removal.</param>
    public void RemoveOrphans(IReadOnlySet<Guid> seen, KeyTracker tracker, IndexMode mode, Action? beforeWrite = null)
    {
        var candidates = store.GetKeys().Where(key => !seen.Contains(key)).ToList();
        foreach (var chunk in candidates.Chunk(OrphanChunkSize))
        {
            lock (_gate)
            {
                var removals = new List<Guid>();
                var live = new List<DocumentSnapshot>();
                foreach (var key in chunk.Where(key => !tracker.Contains(key)))
                {
                    var snapshot = snapshots.Get(key);
                    if (snapshot is null)
                    {
                        removals.Add(key);
                    }
                    else
                    {
                        live.Add(snapshot);
                    }
                }

                var results = Compute(live, mode);
                if (results.Count > 0 || removals.Count > 0)
                {
                    beforeWrite?.Invoke();
                }

                Write(results, removals);
            }
        }
    }

    public const int OrphanChunkSize = 200;

    /// <summary>
    /// Re-indexes, from fresh snapshots read under the lock, every document touched while <paramref name="tracker"/> was
    /// active, in chunks of <see cref="OrphanChunkSize"/> with one write each. A clearing rebuild uses it at the end. A
    /// document touched again after its chunk was written is indexed from rows that are already cleared.
    /// </summary>
    /// <param name="beforeWrite">Called before each chunk, outside the lock (e.g. to wait for editors to pause); a throw stops it.</param>
    public void ReindexTracked(KeyTracker tracker, IndexMode mode, Action? beforeWrite = null)
    {
        List<Guid> keys;
        lock (_gate) keys = [.. tracker.Keys];
        foreach (var chunk in keys.Chunk(OrphanChunkSize))
        {
            beforeWrite?.Invoke();
            IndexBatch(chunk.Select(key => (key, false)).ToList(), mode);
        }
    }

    private void Write(IReadOnlyList<IndexResult> results, IReadOnlyCollection<Guid> removals)
    {
        try
        {
            store.SaveMany(results, removals);
        }
        catch (Exception ex)
        {
            throw new IndexWriteException(ex);
        }
    }

    // The previous rows of all documents are read in one go; the caller writes the result in one transaction.
    private List<IndexResult> Compute(IReadOnlyList<DocumentSnapshot> documents, IndexMode mode)
    {
        if (documents.Count == 0)
        {
            return [];
        }

        var previous = store.GetVariants(documents.Select(d => d.Key).ToList());
        var siteList = sites.GetSites();
        var defaultCulture = sites.DefaultCulture();
        var now = Now();
        var results = new List<IndexResult>(documents.Count);
        foreach (var doc in documents)
        {
            var site = SiteResolver.SiteFor(doc.Path, siteList);

            // No site found: all languages apply (an empty list means that) and the document belongs to no site.
            results.Add(StatusIndexer.Index(
                doc,
                site?.RootKey ?? Guid.Empty,
                defaultCulture,
                site?.Cultures ?? [],
                previous.GetValueOrDefault(doc.Key) ?? [],
                mode,
                now));
        }

        return results;
    }

    private void Touched(Guid key)
    {
        foreach (var tracker in _trackers)
        {
            tracker.Add(key);
        }
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>The keys touched since <see cref="BeginTracking"/>; guarded by the lock of the reindexer.</summary>
    public sealed class KeyTracker : IDisposable
    {
        private readonly Reindexer _owner;
        private readonly HashSet<Guid> _keys = [];

        internal KeyTracker(Reindexer owner) => _owner = owner;

        internal void Add(Guid key) => _keys.Add(key);

        internal bool Contains(Guid key) => _keys.Contains(key);

        internal IEnumerable<Guid> Keys => _keys;

        public void Dispose()
        {
            lock (_owner._gate) _owner._trackers.Remove(this);
        }
    }
}
