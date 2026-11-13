namespace DynamicDave.Umbraco.TranslationHealth.Indexing;

/// <summary>
/// Lets the worker wait until editing has paused: while keys keep arriving, work is deferred (at most
/// <see cref="MaxDeferral"/>), so a burst of saves is written in a few transactions instead of many. Each commit of this
/// package can make a read-then-write transaction of Umbraco fail on SQLite.
/// </summary>
public sealed class QuietGate(TimeProvider timeProvider)
{
    public static readonly TimeSpan MaxDeferral = TimeSpan.FromSeconds(30);

    private long _lastEnqueued = -1;
    private DateTimeOffset? _waitingSince;

    /// <param name="enqueued">The queue's <see cref="ReindexQueue.Enqueued"/> counter.</param>
    /// <param name="hasWork">Queued keys or a rebuild are waiting.</param>
    public bool ShouldRun(long enqueued, bool hasWork)
    {
        var changed = enqueued != _lastEnqueued;
        _lastEnqueued = enqueued;
        if (!hasWork)
        {
            _waitingSince = null;
            return false;
        }

        var now = timeProvider.GetUtcNow();
        _waitingSince ??= now;
        if (!changed || now - _waitingSince >= MaxDeferral)
        {
            _waitingSince = null;
            return true;
        }

        return false;
    }
}
