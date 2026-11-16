namespace DynamicDave.Umbraco.TranslationHealth.Indexing;

/// <summary>
/// Remembers when an editor last changed content, so a rebuild can hold its write transactions until editing paused.
/// On SQLite a save that read before another connection committed can never get its write lock again, and a rebuild
/// commits in chunks; yielding to editors keeps the two apart. Marked from the "ing" notifications (before the save
/// starts), only in memory.
/// </summary>
public sealed class EditorActivity
{
    /// <summary>No content change for this long counts as quiet.</summary>
    public static readonly TimeSpan QuietWindow = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(100);

    private readonly TimeProvider _timeProvider;
    private readonly Action<TimeSpan, CancellationToken> _sleep;
    private long _lastChangeTicks;

    public EditorActivity(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;

        // A cancel wakes the wait at once.
        _sleep = (by, cancellationToken) => cancellationToken.WaitHandle.WaitOne(by);
    }

    public EditorActivity(TimeProvider timeProvider, Action<TimeSpan> sleep)
    {
        _timeProvider = timeProvider;
        _sleep = (by, _) => sleep(by);
    }

    public void Mark() => Interlocked.Exchange(ref _lastChangeTicks, _timeProvider.GetUtcNow().UtcTicks);

    /// <summary>True when no content change happened within <see cref="QuietWindow"/>. Never blocks.</summary>
    public bool IsQuiet()
    {
        var last = Interlocked.Read(ref _lastChangeTicks);
        return last == 0 || _timeProvider.GetUtcNow().UtcTicks - last >= QuietWindow.Ticks;
    }

    /// <summary>
    /// Blocks until there was no change for <see cref="QuietWindow"/>, at most <see cref="QuietGate.MaxDeferral"/>; a busy site
    /// must not starve the caller. True when it is quiet, false when the wait ran out.
    /// </summary>
    public bool WaitUntilQuiet(CancellationToken cancellationToken)
    {
        var started = _timeProvider.GetUtcNow();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsQuiet())
            {
                return true;
            }

            var now = _timeProvider.GetUtcNow();
            if (now - started >= QuietGate.MaxDeferral)
            {
                return false;
            }

            _sleep(Poll, cancellationToken);
        }
    }
}
