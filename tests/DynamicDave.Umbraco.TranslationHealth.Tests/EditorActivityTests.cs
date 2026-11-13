using DynamicDave.Umbraco.TranslationHealth.Indexing;
using DynamicDave.Umbraco.TranslationHealth.Tests.Fakes;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class EditorActivityTests
{
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly List<TimeSpan> _sleeps = [];
    private readonly EditorActivity _activity;

    public EditorActivityTests() => _activity = new EditorActivity(_time, Sleep);

    private void Sleep(TimeSpan by)
    {
        _sleeps.Add(by);
        _time.Advance(by);
    }

    [Fact]
    public void Without_any_editing_nothing_is_waited_for()
    {
        Assert.True(_activity.WaitUntilQuiet(CancellationToken.None));
        Assert.Empty(_sleeps);
    }

    [Fact]
    public void A_recent_change_makes_the_caller_wait_for_the_quiet_window()
    {
        _activity.Mark();

        Assert.True(_activity.WaitUntilQuiet(CancellationToken.None));

        Assert.NotEmpty(_sleeps);
        var waited = _sleeps.Aggregate(TimeSpan.Zero, (a, b) => a + b);
        Assert.InRange(waited, EditorActivity.QuietWindow, EditorActivity.QuietWindow + EditorActivity.Poll);
    }

    [Fact]
    public void A_change_that_is_old_enough_does_not_delay()
    {
        _activity.Mark();
        _time.Advance(EditorActivity.QuietWindow);

        Assert.True(_activity.WaitUntilQuiet(CancellationToken.None));
        Assert.Empty(_sleeps);
    }

    [Fact]
    public void Editing_that_never_stops_is_waited_for_at_most_the_gate_deferral_then_the_caller_goes_ahead()
    {
        EditorActivity? busy = null;
        busy = new EditorActivity(_time, by =>
        {
            Sleep(by);
            busy!.Mark(); // somebody keeps saving
        });
        busy.Mark();

        Assert.False(busy.WaitUntilQuiet(CancellationToken.None));

        var waited = _sleeps.Aggregate(TimeSpan.Zero, (a, b) => a + b);
        Assert.InRange(waited, QuietGate.MaxDeferral, QuietGate.MaxDeferral + EditorActivity.Poll);
    }

    [Fact]
    public void A_cancel_stops_the_wait()
    {
        _activity.Mark();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => _activity.WaitUntilQuiet(cts.Token));
    }
}
