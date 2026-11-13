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
            ProcessBatch(batch);
        }

        return handled;
    }

    // One write per batch. When it fails, the documents are tried one by one, so one failing document does not stop the
    // others; the last failure is kept for the overview.
    private void ProcessBatch(IReadOnlyList<(Guid Key, bool Remove)> batch)
    {
        string? error = null;
        try
        {
            reindexer.IndexBatch(batch, IndexMode.Save);
        }
        catch (Exception batchException)
        {
            logger.LogWarning(batchException, "Translation health could not update a batch of {Count} documents; trying them one by one", batch.Count);
            foreach (var item in batch)
            {
                try
                {
                    reindexer.IndexBatch([item], IndexMode.Save);
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
    }
}
