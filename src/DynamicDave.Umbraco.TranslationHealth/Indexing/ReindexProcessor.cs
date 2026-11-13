using System.Text.Json;
using DynamicDave.Umbraco.TranslationHealth.Status;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Services;

namespace DynamicDave.Umbraco.TranslationHealth.Indexing;

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
            if (!ProcessBatch(batch))
            {
                break; // the write failed and the batch is queued again: wait for the next tick
            }
        }

        return handled;
    }

    // One write per batch. When reading or computing fails, the documents are tried one by one, so one failing document
    // does not stop the others. When the write itself fails the batch goes back in the queue for a later tick: retrying
    // one by one would multiply the commits exactly when the database is busy.
    private bool ProcessBatch(IReadOnlyList<(Guid Key, bool Remove)> batch)
    {
        string? error = null;
        var requeued = false;
        try
        {
            reindexer.IndexBatch(batch, IndexMode.Save);
        }
        catch (IndexWriteException ex)
        {
            logger.LogError(ex, "Translation health could not write the status of {Count} documents; they are queued again", batch.Count);
            queue.Requeue(batch);
            requeued = true;
            error = ex.InnerException?.Message ?? ex.Message;
        }
        catch (Exception batchException)
        {
            logger.LogWarning(batchException, "Translation health could not read a batch of {Count} documents; trying them one by one", batch.Count);
            foreach (var item in batch)
            {
                try
                {
                    reindexer.IndexBatch([item], IndexMode.Save);
                }
                catch (IndexWriteException ex)
                {
                    queue.Requeue([item]);
                    requeued = true;
                    error = ex.InnerException?.Message ?? ex.Message;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Translation health could not update the status of document {DocumentKey}", item.Key);
                    error = ex.Message;
                }
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

        return !requeued;
    }
}
