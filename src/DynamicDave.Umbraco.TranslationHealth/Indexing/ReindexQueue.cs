namespace DynamicDave.Umbraco.TranslationHealth.Indexing;

/// <summary>
/// Documents waiting to be (re-)indexed or removed, filled by the notification handlers and emptied by the worker.
/// A key is queued once: a later request for the same key replaces the earlier one, so a removal that follows a
/// re-index wins and the other way round. Batches come out in the order the keys were first queued.
/// </summary>
public sealed class ReindexQueue
{
    private readonly object _gate = new();
    private readonly Queue<Guid> _order = new();
    private readonly Dictionary<Guid, bool> _pending = [];

    private long _enqueued;

    /// <summary>How many keys were ever queued: a change tells the worker that editing is still going on.</summary>
    public long Enqueued
    {
        get
        {
            lock (_gate) return _enqueued;
        }
    }

    public int Count
    {
        get
        {
            lock (_gate) return _pending.Count;
        }
    }

    public void Enqueue(IEnumerable<Guid> keys) => Add(keys, remove: false);

    public void EnqueueRemoval(IEnumerable<Guid> keys) => Add(keys, remove: true);

    /// <summary>Takes and forgets at most <paramref name="max"/> queued keys.</summary>
    public IReadOnlyList<(Guid Key, bool Remove)> TakeBatch(int max)
    {
        var batch = new List<(Guid, bool)>(Math.Min(max, 256));
        lock (_gate)
        {
            while (batch.Count < max && _order.TryDequeue(out var key))
            {
                if (_pending.Remove(key, out var remove))
                {
                    batch.Add((key, remove));
                }
            }
        }

        return batch;
    }

    private void Add(IEnumerable<Guid> keys, bool remove)
    {
        lock (_gate)
        {
            foreach (var key in keys)
            {
                _enqueued++;
                if (!_pending.ContainsKey(key))
                {
                    _order.Enqueue(key);
                }

                _pending[key] = remove;
            }
        }
    }
}
