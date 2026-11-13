using System.Text.Json;
using DynamicDave.Umbraco.TranslationHealth.Jobs;
using DynamicDave.Umbraco.TranslationHealth.Status;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;

namespace DynamicDave.Umbraco.TranslationHealth.Indexing;

/// <summary>The last failure of the indexing worker, stored as JSON under <see cref="Constants.LastIndexErrorKey"/>.</summary>
public sealed record IndexError(DateTime Utc, string Message);

/// <summary>Empties the <see cref="ReindexQueue"/> in batches. Separate from the hosted service so tests can drive it.</summary>
public sealed class ReindexProcessor(
    ReindexQueue queue,
    Reindexer reindexer,
    IKeyValueService keyValues,
    TimeProvider timeProvider,
    ILogger<ReindexProcessor> logger)
{
    public const int BatchSize = 200;

    /// <summary>Handles every queued document, a batch at a time; returns how many were handled.</summary>
    public int ProcessPending(CancellationToken cancellationToken = default)
    {
        var handled = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = queue.TakeBatch(BatchSize);
            if (batch.Count == 0)
            {
                break;
            }

            handled += batch.Count;
            ProcessBatch(batch);
        }

        return handled;
    }

    // One failing document must not stop the others; the last failure is kept for the overview.
    private void ProcessBatch(IReadOnlyList<(Guid Key, bool Remove)> batch)
    {
        string? error = null;
        foreach (var (key, remove) in batch)
        {
            try
            {
                if (remove)
                {
                    reindexer.Remove(key);
                }
                else
                {
                    reindexer.Reindex(key, IndexMode.Save);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Translation health could not update the status of document {DocumentKey}", key);
                error = ex.Message;
            }
        }

        try
        {
            if (error is not null)
            {
                keyValues.SetValue(
                    Constants.LastIndexErrorKey,
                    JsonSerializer.Serialize(new IndexError(timeProvider.GetUtcNow().UtcDateTime, error)));
            }
            else if (!string.IsNullOrEmpty(keyValues.GetValue(Constants.LastIndexErrorKey)))
            {
                keyValues.SetValue(Constants.LastIndexErrorKey, string.Empty);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Translation health could not store the state of its last update");
        }
    }
}

/// <summary>Every 2 seconds: handles the queued documents, starts a rebuild that was asked for while another ran, and does the startup check.</summary>
public sealed class ReindexWorker(
    ReindexProcessor processor,
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
                    processor.ProcessPending(stoppingToken);
                    scheduler.StartPending();
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
    }
}
