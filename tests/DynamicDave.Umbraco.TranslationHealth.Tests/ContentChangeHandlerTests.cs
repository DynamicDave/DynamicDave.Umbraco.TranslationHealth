using DynamicDave.Umbraco.TranslationHealth.Indexing;
using DynamicDave.Umbraco.TranslationHealth.Jobs;
using DynamicDave.Umbraco.TranslationHealth.Status;
using DynamicDave.Umbraco.TranslationHealth.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services.Changes;
using Umbraco.Cms.Core.Strings;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class ContentChangeHandlerTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeSnapshotSource _source = new();
    private readonly FakeSiteSource _sites = new();
    private readonly FakeKeyValueService _keyValues = new();
    private readonly ReindexQueue _queue = new();
    private readonly ManualTimeProvider _time = new(Start);
    private readonly EditorActivity _activity;
    private readonly RebuildScheduler _scheduler;
    private readonly ContentChangeHandlers _handlers;
    private readonly ContentType _type = new(new DefaultShortStringHelper(Options.Create(new RequestHandlerSettings())), -1)
    {
        Alias = "page",
    };

    private readonly FakeRuntimeState _runtime;
    private readonly JobRunner _runner;

    public ContentChangeHandlerTests()
    {
        _keyValues.Values[Constants.JobStateKey] = string.Empty;
        var time = _time;
        _activity = new EditorActivity(time, by => time.Advance(by));
        var reindexer = new Reindexer(_source, _sites, new FakeStatusStore(), time);
        var services = new ServiceCollection();
        services.AddTransient(_ => new RebuildJob(
            _source, reindexer, _keyValues, _sites, new TestOptionsMonitor<TranslationHealthOptions>(new TranslationHealthOptions()), time, _activity));
        var provider = services.BuildServiceProvider();
        _runner = new JobRunner(_keyValues, provider.GetRequiredService<IServiceScopeFactory>(), time, _activity, new ListLogger<JobRunner>());
        _scheduler = new RebuildScheduler(_runner, _sites, _keyValues, new ListLogger<RebuildScheduler>());
        var (state, control) = FakeRuntimeState.Create();
        _runtime = control;
        _handlers = new ContentChangeHandlers(_queue, _source, _sites, _scheduler, state, new ListLogger<ContentChangeHandlers>(), _activity);
    }

    private IContent Doc(Guid key) => new global::Umbraco.Cms.Core.Models.Content("name", -1, _type) { Key = key };

    private static EventMessages Messages() => new();

    private List<(Guid Key, bool Remove)> Taken() => [.. _queue.TakeBatch(100)];

    [Fact]
    public void Saved_published_and_unpublished_queue_the_documents_for_a_reindex()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        _handlers.Handle(new ContentSavedNotification(Doc(a), Messages()));
        _handlers.Handle(new ContentPublishedNotification(Doc(b), Messages()));
        _handlers.Handle(new ContentUnpublishedNotification(Doc(c), Messages()));

        Assert.Equal([(a, false), (b, false), (c, false)], Taken());
    }

    [Fact]
    public void Moved_queues_the_document_and_its_descendants_for_a_reindex()
    {
        var moved = Guid.NewGuid();
        var child = Guid.NewGuid();
        _source.Descendants[moved] = [child];

        _handlers.Handle(new ContentMovedNotification(new MoveEventInfo<IContent>(Doc(moved), "-1,5", 7), Messages()));

        Assert.Equal([(moved, false), (child, false)], Taken());
    }

    [Fact]
    public void Moved_to_the_recycle_bin_queues_the_document_and_its_descendants_for_removal()
    {
        var moved = Guid.NewGuid();
        var child = Guid.NewGuid();
        _source.Descendants[moved] = [child];

        _handlers.Handle(new ContentMovedToRecycleBinNotification(new MoveToRecycleBinEventInfo<IContent>(Doc(moved), "-1,5"), Messages()));

        Assert.Equal([(moved, true), (child, true)], Taken());
    }

    [Fact]
    public void Sorted_queues_the_documents_and_their_descendants_for_a_reindex()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var child = Guid.NewGuid();
        _source.Descendants[b] = [child];

        _handlers.Handle(new ContentSortedNotification([Doc(a), Doc(b)], Messages()));

        Assert.Equal([(a, false), (b, false), (child, false)], Taken());
    }

    [Fact]
    public void Deleted_queues_the_documents_for_removal()
    {
        var a = Guid.NewGuid();

        _handlers.Handle(new ContentDeletedNotification(Doc(a), Messages()));

        Assert.Equal([(a, true)], Taken());
    }

    [Fact]
    public void Editor_actions_mark_activity_in_memory_only()
    {
        var helper = new DefaultShortStringHelper(Options.Create(new RequestHandlerSettings()));
        var media = new Media("m", -1, new MediaType(helper, -1) { Alias = "image" });
        var dictionaryItem = new DictionaryItem("greeting");
        Action[] handles =
        [
            () => _handlers.Handle(new ContentSavingNotification(Doc(Guid.NewGuid()), Messages())),
            () => _handlers.Handle(new ContentRollingBackNotification(Doc(Guid.NewGuid()), Messages())),
            () => _handlers.Handle(new ContentEmptyingRecycleBinNotification([Doc(Guid.NewGuid())], Messages())),
            () => _handlers.Handle(new DictionaryItemSavingNotification(dictionaryItem, Messages())),
            () => _handlers.Handle(new DictionaryItemDeletingNotification(dictionaryItem, Messages())),
            () => _handlers.Handle(new MediaSavingNotification(media, Messages())),
            () => _handlers.Handle(new MediaMovingNotification(new MoveEventInfo<IMedia>(media, "-1,5", 7), Messages())),
            () => _handlers.Handle(new MediaDeletingNotification(media, Messages())),
        ];

        foreach (var handle in handles)
        {
            _time.Advance(EditorActivity.QuietWindow);
            Assert.True(_activity.IsQuiet());

            handle();

            Assert.False(_activity.IsQuiet());
        }

        Assert.Empty(Taken());
    }

    [Fact]
    public void A_failing_lookup_never_fails_the_notification()
    {
        var moved = Guid.NewGuid();
        _source.Descendants.Clear();
        var failing = new ContentChangeHandlers(
            _queue, new ThrowingSource(), _sites, _scheduler, FakeRuntimeState.Create().State, new ListLogger<ContentChangeHandlers>(), _activity);

        failing.Handle(new ContentMovedNotification(new MoveEventInfo<IContent>(Doc(moved), "-1,5", 7), Messages()));

        Assert.Empty(Taken());
    }

    [Fact]
    public async Task Domain_and_language_changes_only_note_the_change_and_clear_the_site_cache()
    {
        _handlers.Handle(new DomainSavedNotification(new UmbracoDomain("example.com") { RootContentId = 1 }, Messages()));
        _handlers.Handle(new DomainDeletedNotification(new UmbracoDomain("example.com") { RootContentId = 1 }, Messages()));
        _handlers.Handle(new LanguageSavedNotification(new Language("nl-NL", "Dutch"), Messages()));
        _handlers.Handle(new LanguageDeletedNotification(new Language("nl-NL", "Dutch"), Messages()));

        // Nothing started on the request thread: the worker does that.
        Assert.Null(_runner.Current());
        Assert.True(_scheduler.HasPending);
        Assert.Equal(4, _sites.ClearCacheCalls);
        Assert.Equal(0, _source.ClearCacheCalls);

        Assert.NotNull(_scheduler.StartPending());
        await _runner.LastRun!;
        Assert.Equal(JobKind.Rebuild, _runner.Current()!.Kind);
        Assert.Equal(JobStatus.Completed, _runner.Current()!.Status);
    }

    [Fact]
    public void A_document_type_change_also_clears_the_snapshot_cache()
    {
        _handlers.Handle(new ContentTypeChangedNotification(
            new ContentTypeChange<IContentType>(_type, ContentTypeChangeTypes.RefreshMain), Messages()));

        Assert.Equal(1, _sites.ClearCacheCalls);
        Assert.Equal(1, _source.ClearCacheCalls);
        Assert.True(_scheduler.HasPending);
        Assert.Null(_runner.Current());
    }

    [Fact]
    public void No_rebuild_is_started_before_the_runtime_runs()
    {
        _runtime.Level = RuntimeLevel.Install;

        _handlers.Handle(new LanguageSavedNotification(new Language("nl-NL", "Dutch"), Messages()));

        Assert.Equal(1, _sites.ClearCacheCalls);
        Assert.False(_scheduler.HasPending);
    }

    private sealed class ThrowingSource : DynamicDave.Umbraco.TranslationHealth.Content.IContentSnapshotSource
    {
        public DynamicDave.Umbraco.TranslationHealth.Content.DocumentSnapshot? Get(Guid key) => throw new InvalidOperationException();

        public IReadOnlyList<DynamicDave.Umbraco.TranslationHealth.Content.DocumentSnapshot> GetPage(long pageIndex, int pageSize, out long total) => throw new InvalidOperationException();

        public IReadOnlyList<Guid> DescendantKeys(Guid key) => throw new InvalidOperationException("Navigation not ready");

        public void ClearCache()
        {
        }
    }
}
