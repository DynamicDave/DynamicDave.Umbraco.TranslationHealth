using DynamicDave.Umbraco.TranslationHealth.Indexing;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class ReindexQueueTests
{
    [Fact]
    public void Queue_coalesces_duplicate_keys_and_hands_out_batches()
    {
        var queue = new ReindexQueue();
        var keys = Enumerable.Range(0, 2000).Select(_ => Guid.NewGuid()).ToList();
        queue.Enqueue(keys);
        queue.Enqueue(keys);

        var taken = new List<Guid>();
        for (var i = 0; i < 10; i++)
        {
            var batch = queue.TakeBatch(200);
            Assert.Equal(200, batch.Count);
            taken.AddRange(batch.Select(b => b.Key));
        }

        Assert.Equal(2000, taken.Distinct().Count());
        Assert.Equal(keys, taken);
        Assert.Empty(queue.TakeBatch(200));
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Removal_after_enqueue_wins()
    {
        var queue = new ReindexQueue();
        var key = Guid.NewGuid();
        queue.Enqueue([key]);
        queue.EnqueueRemoval([key]);

        Assert.Equal([(key, true)], queue.TakeBatch(10));
        Assert.Empty(queue.TakeBatch(10));
    }

    [Fact]
    public void Enqueue_after_removal_wins()
    {
        var queue = new ReindexQueue();
        var key = Guid.NewGuid();
        queue.EnqueueRemoval([key]);
        queue.Enqueue([key]);

        Assert.Equal([(key, false)], queue.TakeBatch(10));
    }

    [Fact]
    public void A_key_taken_can_be_queued_again()
    {
        var queue = new ReindexQueue();
        var key = Guid.NewGuid();
        queue.Enqueue([key]);
        queue.TakeBatch(10);
        queue.Enqueue([key]);

        Assert.Equal([(key, false)], queue.TakeBatch(10));
    }
}
