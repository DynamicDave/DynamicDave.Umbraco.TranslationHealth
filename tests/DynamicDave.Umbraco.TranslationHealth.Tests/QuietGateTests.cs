using DynamicDave.Umbraco.TranslationHealth.Indexing;
using DynamicDave.Umbraco.TranslationHealth.Tests.Fakes;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class QuietGateTests
{
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly QuietGate _gate;

    public QuietGateTests() => _gate = new QuietGate(_time);

    [Fact]
    public void Nothing_to_do_never_runs()
    {
        Assert.False(_gate.ShouldRun(0, hasWork: false));
        Assert.False(_gate.ShouldRun(0, hasWork: false));
    }

    [Fact]
    public void Work_runs_once_a_tick_passed_without_new_keys()
    {
        Assert.False(_gate.ShouldRun(5, hasWork: true)); // keys just arrived
        Assert.True(_gate.ShouldRun(5, hasWork: true)); // quiet since the previous tick
        Assert.False(_gate.ShouldRun(5, hasWork: false));
    }

    [Fact]
    public void Keys_that_keep_arriving_defer_the_work()
    {
        for (var i = 1; i <= 10; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(2));
            Assert.False(_gate.ShouldRun(i, hasWork: true));
        }
    }

    [Fact]
    public void The_deferral_ends_after_thirty_seconds()
    {
        var ran = false;
        long enqueued = 0;
        for (var seconds = 0; seconds <= 40 && !ran; seconds += 2)
        {
            enqueued++;
            ran = _gate.ShouldRun(enqueued, hasWork: true);
            if (!ran)
            {
                _time.Advance(TimeSpan.FromSeconds(2));
            }
        }

        Assert.True(ran);
        Assert.InRange(_time.GetUtcNow() - new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(32));
    }

    [Fact]
    public void A_pending_rebuild_without_queued_keys_runs_at_the_second_tick()
    {
        Assert.False(_gate.ShouldRun(0, hasWork: true));
        Assert.True(_gate.ShouldRun(0, hasWork: true));
    }
}
