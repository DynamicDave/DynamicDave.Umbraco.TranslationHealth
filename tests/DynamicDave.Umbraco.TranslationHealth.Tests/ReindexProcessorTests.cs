using System.Text.Json;
using DynamicDave.Umbraco.TranslationHealth.Content;
using DynamicDave.Umbraco.TranslationHealth.Indexing;
using DynamicDave.Umbraco.TranslationHealth.Status;
using DynamicDave.Umbraco.TranslationHealth.Tests.Fakes;
using Microsoft.Extensions.Logging;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class ReindexProcessorTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly FailingSource _source = new();
    private readonly FakeSiteSource _sites = new();
    private readonly FakeStatusStore _store = new();
    private readonly FakeKeyValueService _keyValues = new();
    private readonly ManualTimeProvider _time = new(Start);
    private readonly ListLogger<ReindexProcessor> _log = new();
    private readonly ReindexQueue _queue = new();
    private readonly ReindexProcessor _processor;

    public ReindexProcessorTests()
    {
        var reindexer = new Reindexer(_source, _sites, _store, _time);
        _processor = new ReindexProcessor(_queue, reindexer, _keyValues, _time, _log);
    }

    private Guid AddDocument(int id)
    {
        var key = Guid.NewGuid();
        _source.Add(FakeSnapshotSource.Doc(
            key,
            id,
            new CultureSnapshot("en-US", VariantBaseState.Published, T0, null, "n", "a"),
            new CultureSnapshot("nl-NL", VariantBaseState.Published, T0, null, "n", "b")));
        return key;
    }

    [Fact]
    public void Queued_documents_are_indexed_and_removed_in_batches()
    {
        var keys = Enumerable.Range(1, 450).Select(AddDocument).ToList();
        var gone = Guid.NewGuid();
        _store.Seed(new IndexResult(new DocumentRow(gone, 999, null, "-1,999", "00999", 1, "page", Guid.Empty, "n"), []));
        _queue.Enqueue(keys);
        _queue.EnqueueRemoval([gone]);

        var handled = _processor.ProcessPending();

        Assert.Equal(451, handled);
        Assert.Equal(keys.Order(), _store.Keys.Order());
        Assert.Equal(0, _queue.Count);
        Assert.Equal(3, _store.SaveManyCalls); // 200 + 200 + 51 documents: one write transaction per batch
    }

    [Fact]
    public void A_failing_document_is_logged_and_stored_and_the_others_are_still_indexed()
    {
        var bad = AddDocument(1);
        var good = AddDocument(2);
        _source.FailFor.Add(bad);
        _queue.Enqueue([bad, good]);

        _processor.ProcessPending();

        Assert.NotNull(_store.Document(good));
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains(bad.ToString()));
        var error = JsonSerializer.Deserialize<IndexError>(_keyValues.GetValue(Constants.LastIndexErrorKey)!)!;
        Assert.Equal("Broken", error.Message);
        Assert.Equal(Start.UtcDateTime, error.Utc);
    }

    [Fact]
    public void A_successful_batch_clears_the_stored_error()
    {
        var bad = AddDocument(1);
        _source.FailFor.Add(bad);
        _queue.Enqueue([bad]);
        _processor.ProcessPending();
        Assert.NotEmpty(_keyValues.GetValue(Constants.LastIndexErrorKey)!);

        _source.FailFor.Clear();
        _queue.Enqueue([bad]);
        _processor.ProcessPending();

        Assert.Equal(string.Empty, _keyValues.GetValue(Constants.LastIndexErrorKey));
    }

    [Fact]
    public void A_failing_write_puts_the_batch_back_without_retrying_one_by_one()
    {
        var keys = new[] { AddDocument(1), AddDocument(2), AddDocument(3) };
        _queue.Enqueue(keys);
        var writes = 0;
        _store.OnSave = () =>
        {
            writes++;
            throw new InvalidOperationException("database is locked");
        };

        _processor.ProcessPending();

        Assert.Equal(1, writes);
        Assert.Equal(3, _queue.Count);
        Assert.Equal("database is locked", JsonSerializer.Deserialize<IndexError>(_keyValues.GetValue(Constants.LastIndexErrorKey)!)!.Message);

        _store.OnSave = null;
        _time.Advance(ReindexProcessor.FirstRetryDelay);
        _processor.ProcessPending();

        Assert.Equal(keys.Order(), _store.Keys.Order());
        Assert.Equal(string.Empty, _keyValues.GetValue(Constants.LastIndexErrorKey));
    }

    [Fact]
    public void A_failing_write_is_retried_with_a_growing_delay_of_at_most_five_minutes()
    {
        var keys = new[] { AddDocument(1), AddDocument(2) };
        _queue.Enqueue(keys);
        var writes = 0;
        _store.OnSave = () =>
        {
            writes++;
            throw new InvalidOperationException("no such table");
        };

        _processor.ProcessPending();
        Assert.Equal(1, writes);

        // Every failure doubles the wait, starting at 4 seconds: the worker's 2-second ticks in between do nothing.
        var delay = TimeSpan.FromSeconds(4);
        for (var attempt = 2; attempt <= 12; attempt++)
        {
            _time.Advance(delay - TimeSpan.FromSeconds(1));
            Assert.Equal(0, _processor.ProcessPending());
            Assert.Equal(attempt - 1, writes);

            _time.Advance(TimeSpan.FromSeconds(1));
            _processor.ProcessPending();
            Assert.Equal(attempt, writes);
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, TimeSpan.FromMinutes(5).Ticks));
        }

        Assert.Equal(TimeSpan.FromMinutes(5), delay);
        Assert.Equal(2, _queue.Count); // nothing is lost

        // Once the database is back, the keys are written and the delay starts over.
        _store.OnSave = null;
        _time.Advance(delay);
        Assert.Equal(2, _processor.ProcessPending());
        Assert.Equal(keys.Order(), _store.Keys.Order());

        var more = AddDocument(3);
        _queue.Enqueue([more]);
        _store.OnSave = () => throw new InvalidOperationException("locked");
        _processor.ProcessPending();
        _store.OnSave = null;
        _time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(1, _processor.ProcessPending());
    }

    [Fact]
    public void Nothing_queued_touches_nothing()
    {
        Assert.Equal(0, _processor.ProcessPending());
        Assert.Null(_keyValues.GetValue(Constants.LastIndexErrorKey));
    }

    private sealed class FailingSource : IContentSnapshotSource
    {
        private readonly FakeSnapshotSource _inner = new();

        public HashSet<Guid> FailFor { get; } = [];

        public void Add(DocumentSnapshot document) => _inner.Add(document);

        public DocumentSnapshot? Get(Guid key) =>
            FailFor.Contains(key) ? throw new InvalidOperationException("Broken") : _inner.Get(key);

        public IReadOnlyList<DocumentSnapshot> GetPage(int afterId, int pageSize, out long remaining) =>
            _inner.GetPage(afterId, pageSize, out remaining);

        public IReadOnlyList<Guid> DescendantKeys(Guid key) => _inner.DescendantKeys(key);

        public void ClearCache() => _inner.ClearCache();
    }
}
