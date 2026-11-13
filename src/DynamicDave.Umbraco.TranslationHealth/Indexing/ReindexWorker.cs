using DynamicDave.Umbraco.TranslationHealth.Jobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;

namespace DynamicDave.Umbraco.TranslationHealth.Indexing;

/// <summary>
/// Every 2 seconds: does the startup check and, once editing has paused (see <see cref="QuietGate"/>), handles the queued
/// documents and starts a rebuild that was asked for.
/// </summary>
public sealed class ReindexWorker(
    ReindexQueue queue,
    ReindexProcessor processor,
    QuietGate gate,
    StartupCheck startupCheck,
    RebuildScheduler scheduler,
    IRuntimeState runtimeState,
    TimeProvider timeProvider,
    ILogger<ReindexWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (runtimeState.Level != RuntimeLevel.Run)
                {
                    continue;
                }

                try
                {
                    startupCheck.Run();
                    if (gate.ShouldRun(queue.Enqueued, queue.Count > 0 || scheduler.HasPending))
                    {
                        processor.ProcessPending(stoppingToken);
                        scheduler.StartPending();
                    }
                }
                catch (Exception ex)
                {
                    // An exception here would stop the host.
                    logger.LogError(ex, "Translation health indexing worker failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }

        if (queue.Count > 0)
        {
            logger.LogWarning("Translation health stopped with {Count} documents still queued; run a rebuild to catch up", queue.Count);
        }
    }
}
