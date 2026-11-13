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
    public void Reindex(Guid key, IndexMode mode)
    {
        lock (_gate)
        {
            Touched(key);

            // Read inside the lock, so a snapshot is never older than the rows it replaces.
            var snapshot = snapshots.Get(key);
            if (snapshot is null)
            {
                store.Remove([key]);
                return;
            }

            IndexLocked(snapshot, mode);
        }
    }

    /// <summary>
    /// Re-indexes a document from a snapshot the caller read earlier (a rebuild paging through the documents). A document
    /// that was re-indexed or removed since <see cref="BeginTracking"/> is skipped: that happened after the snapshot was
    /// read or will be repeated, and the snapshot may be older.
    /// </summary>
    public void Reindex(DocumentSnapshot snapshot, IndexMode mode)
    {
        lock (_gate)
        {
            if (_trackers.Any(tracker => tracker.Contains(snapshot.Key)))
            {
                return;
            }

            IndexLocked(snapshot, mode);
        }
    }

    public void Remove(Guid key)
    {
        lock (_gate)
        {
            Touched(key);
            store.Remove([key]);
        }
    }

    /// <summary>
    /// Records <paramref name="culture"/> of the document as up to date with the stored default-language fingerprint.
    /// The stored fingerprint is used rather than a fresh snapshot: it is what the status the user looked at was based on,
    /// and a save that is still queued marks the row outdated again once it is indexed.
    /// </summary>
    /// <exception cref="InvalidOperationException">The row does not exist, is not created or belongs to the default language.</exception>
    public VariantRow MarkUpToDate(Guid key, string culture, Guid userKey)
    {
        lock (_gate)
        {
            var rows = store.GetVariants(key);
            var defaultCulture = sites.DefaultCulture();
            var row = rows.FirstOrDefault(r => Same(r.Culture, culture));
            if (row is null || Same(row.Culture, defaultCulture) || row.Status is CellStatus.Missing or CellStatus.NotApplicable)
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
    /// Removes every stored document that is neither in <paramref name="seen"/> nor touched while <paramref name="tracker"/>
    /// was active. Runs under the lock, so a document indexed meanwhile is not removed.
    /// </summary>
    public void RemoveOrphans(IReadOnlySet<Guid> seen, KeyTracker tracker)
    {
        lock (_gate)
        {
            var keep = new HashSet<Guid>(seen);
            keep.UnionWith(tracker.Keys());
            store.RemoveExcept(keep);
        }
    }

    private void IndexLocked(DocumentSnapshot doc, IndexMode mode)
    {
        var site = SiteResolver.SiteFor(doc.Path, sites.GetSites());

        // No site found: all languages apply (an empty list means that) and the document belongs to no site.
        var result = StatusIndexer.Index(
            doc,
            site?.RootKey ?? Guid.Empty,
            sites.DefaultCulture(),
            site?.Cultures ?? [],
            store.GetVariants(doc.Key),
            mode,
            Now());
        store.Save(result);
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

        internal IReadOnlyCollection<Guid> Keys() => _keys;

        public void Dispose()
        {
            lock (_owner._gate) _owner._trackers.Remove(this);
        }
    }
}
