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
        _store.Save(new IndexResult(new DocumentRow(gone, 999, null, "-1,999", "00999", 1, "page", Guid.Empty, "n"), []));
        _queue.Enqueue(keys);
        _queue.EnqueueRemoval([gone]);

        var handled = _processor.ProcessPending();

        Assert.Equal(451, handled);
        Assert.Equal(keys.Order(), _store.Keys.Order());
        Assert.Equal(0, _queue.Count);
    }

    [Fact]
    public void A_failing_document_is_logged_and_stored_and_the_others_are_still_indexed()
    {
        var bad = AddDocument(1);
        var good = AddDocument(2);
        _source.FailFor.Add(bad);
        _queue.Enqueue([bad, good]);

        _processor.ProcessPending();

        Assert.NotNull(_store.GetDocument(good));
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

        public IReadOnlyList<DocumentSnapshot> GetPage(long pageIndex, int pageSize, out long total) =>
            _inner.GetPage(pageIndex, pageSize, out total);

        public IReadOnlyList<Guid> DescendantKeys(Guid key) => _inner.DescendantKeys(key);

        public void ClearCache() => _inner.ClearCache();
    }
}
