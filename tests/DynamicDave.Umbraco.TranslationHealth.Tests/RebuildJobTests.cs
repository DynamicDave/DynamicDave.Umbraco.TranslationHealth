using System.Text.Json;
using DynamicDave.Umbraco.TranslationHealth.Content;
using DynamicDave.Umbraco.TranslationHealth.Indexing;
using DynamicDave.Umbraco.TranslationHealth.Jobs;
using DynamicDave.Umbraco.TranslationHealth.Persistence;
using DynamicDave.Umbraco.TranslationHealth.Status;
using DynamicDave.Umbraco.TranslationHealth.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class RebuildJobTests
{
    private const string En = "en-US";
    private const string Nl = "nl-NL";
    private static readonly DateTimeOffset Start = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly FakeSnapshotSource _source = new();
    private readonly FakeSiteSource _sites = new();
    private readonly FakeStatusStore _store = new();
    private readonly FakeKeyValueService _keyValues = new();
    private readonly TestOptionsMonitor<TranslationHealthOptions> _options = new(new TranslationHealthOptions());
    private readonly ManualTimeProvider _time = new(Start);
    private readonly Reindexer _reindexer;
    private readonly RebuildJob _job;

    public RebuildJobTests()
    {
        _reindexer = new Reindexer(_source, _sites, _store, _time);
        _job = new RebuildJob(_source, _reindexer, _keyValues, _sites, _options, _time);
    }

    private static readonly JobProgress NoProgress = new(TimeProvider.System, (_, _, _) => { });

    private static CultureSnapshot C(string culture, string fingerprint) =>
        new(culture, VariantBaseState.Published, T0, null, $"{culture} name", fingerprint);

    private static DocumentSnapshot Doc(Guid key, int id, string en = "a", string nl = "b") =>
        FakeSnapshotSource.Doc(key, id, C(En, en), C(Nl, nl));

    private List<Guid> AddDocuments(int count)
    {
        var keys = new List<Guid>();
        for (var i = 1; i <= count; i++)
        {
            var key = Guid.NewGuid();
            keys.Add(key);
            _source.Add(Doc(key, i));
        }

        return keys;
    }

    private Task<(Guid?, TempKind?, string?)> Run(IndexMode mode = IndexMode.Rebuild, CancellationToken cancellationToken = default) =>
        _job.RunAsync(mode, NoProgress, cancellationToken);

    private static VariantRow StoredNl(Guid key, string? baseline) =>
        new(key, Nl, CellStatus.Published, true, TrackingMode.Fingerprint, T0, null, "nl", "b", baseline,
            BaselineSource.Saved, null, null, T0);

    [Fact]
    public async Task Every_batch_waits_for_editors_to_pause_before_it_writes()
    {
        AddDocuments(450);
        var order = new List<string>();
        var activity = new EditorActivity(_time, by => { order.Add("wait"); _time.Advance(by); });
        _store.OnSave = () => order.Add("write");
        var job = new RebuildJob(_source, _reindexer, _keyValues, _sites, _options, _time, activity);
        activity.Mark();

        await job.RunAsync(IndexMode.Rebuild, NoProgress, CancellationToken.None);

        // The first batch had to wait for the quiet window (editing just happened), the later ones found it quiet.
        Assert.Equal("wait", order[0]);
        Assert.Equal(3, order.Count(o => o == "write"));
        Assert.True(order.IndexOf("wait") < order.IndexOf("write"));
    }

    [Fact]
    public async Task Rebuild_indexes_all_documents_and_removes_orphans()
    {
        var keys = AddDocuments(450);
        var orphan = Guid.NewGuid();
        _store.Save(new IndexResult(
            new DocumentRow(orphan, 99, null, "-1,99", "00099", 1, "page", Guid.Empty, "orphan"), [StoredNl(orphan, "x")]));

        var (key, kind, summary) = await Run();

        Assert.Null(key);
        Assert.Null(kind);
        Assert.Equal(450, JsonDocument.Parse(summary!).RootElement.GetProperty("documents").GetInt32());
        Assert.Equal(keys.Order(), _store.Keys.Order());
        Assert.Null(_store.GetDocument(orphan));
        Assert.Empty(_store.GetVariants(orphan));
        Assert.All(keys, k => Assert.Equal(2, _store.GetVariants(k).Count));
        Assert.Equal(3, _source.GetPageCalls); // pages of 200, 200 and 50
    }

    [Fact]
    public async Task Rebuild_stores_what_it_was_based_on()
    {
        AddDocuments(1);
        _options.CurrentValue.ExcludedDocumentTypes = ["Alpha", "beta"];

        await Run();

        Assert.Equal(En, _keyValues.GetValue(Constants.DefaultCultureKey));
        Assert.Equal(RebuildJob.ExcludedTypesHash(["beta", "ALPHA"]), _keyValues.GetValue(Constants.ExcludedTypesKey));
        Assert.Equal(Start.UtcDateTime, DateTime.Parse(_keyValues.GetValue(Constants.LastRebuildKey)!, null, System.Globalization.DateTimeStyles.RoundtripKind));
    }

    [Fact]
    public async Task Rebuild_keeps_baselines_and_marks()
    {
        var key = Guid.NewGuid();
        _source.Add(Doc(key, 1));
        _store.Save(new IndexResult(
            new DocumentRow(key, 1, null, "-1,1", "00001", 1, "page", Guid.Empty, "n"), [StoredNl(key, "old-baseline")]));

        await Run(IndexMode.Rebuild);

        var nl = _store.GetVariants(key).Single(v => v.Culture == Nl);
        Assert.Equal("old-baseline", nl.BaselineFingerprint);
        Assert.True(nl.Outdated);
    }

    [Fact]
    public async Task Rebuild_after_default_culture_change_clears_baselines()
    {
        var key = Guid.NewGuid();
        _source.Add(Doc(key, 1));
        _store.Save(new IndexResult(
            new DocumentRow(key, 1, null, "-1,1", "00001", 1, "page", Guid.Empty, "n"), [StoredNl(key, "old-baseline")]));

        await Run(IndexMode.RebuildClearBaselines);

        var nl = _store.GetVariants(key).Single(v => v.Culture == Nl);
        Assert.Null(nl.BaselineFingerprint);
        Assert.Null(nl.BaselineSource);
        Assert.Equal(TrackingMode.Date, nl.Mode);
    }

    [Fact]
    public async Task Rebuild_keeps_documents_indexed_while_it_ran()
    {
        AddDocuments(250);
        var created = Guid.NewGuid();
        _source.Add(Doc(created, 1000));
        _source.HiddenFromPages.Add(created);

        // The page loop is past the new document's place when the worker indexes it.
        _source.OnGetPage = page =>
        {
            if (page == 1)
            {
                _reindexer.Reindex(created, IndexMode.Save);
            }
        };

        await Run();

        Assert.NotNull(_store.GetDocument(created));
        Assert.Equal(251, _store.Count());
    }

    [Fact]
    public async Task Rebuild_does_not_overwrite_a_document_the_worker_indexed_meanwhile()
    {
        var keys = AddDocuments(3);
        var changed = keys[1];

        // The page was read; then the document is edited and indexed by the worker.
        _source.OnGetPage = _ =>
        {
            _source.OnGetPage = null;
            _source.Replace(Doc(changed, 2, en: "newer"));
            _reindexer.Reindex(changed, IndexMode.Save);
        };

        await Run();

        Assert.Equal("newer", _store.GetVariants(changed).Single(v => v.Culture == En).TextFingerprint);
    }

    [Fact]
    public async Task Rebuild_does_not_bring_back_a_document_removed_meanwhile()
    {
        var keys = AddDocuments(3);
        _source.OnGetPage = _ =>
        {
            _source.OnGetPage = null;
            _source.Remove(keys[0]);
            _reindexer.Remove(keys[0]);
        };

        await Run();

        Assert.Null(_store.GetDocument(keys[0]));
        Assert.Equal(2, _store.Count());
    }

    [Fact]
    public async Task Rebuild_keeps_the_rows_of_live_documents_that_a_move_shifted_out_of_the_pages()
    {
        var keys = AddDocuments(600);
        await Run();
        var baseline = new Dictionary<Guid, VariantRow>();
        foreach (var key in keys)
        {
            var nl = _store.GetVariants(key).Single(v => v.Culture == Nl);
            baseline[key] = nl with { BaselineFingerprint = "mark", BaselineSource = BaselineSource.MarkedUpToDate };
            _store.SaveVariant(baseline[key]);
        }

        // While page 1 is read, a block of documents moves from the first page behind the cursor: the pages shift and
        // documents 400..449 are never returned. Their rows (with marks) must survive.
        _source.OnGetPage = page =>
        {
            if (page == 1)
            {
                _source.OnGetPage = null;
                foreach (var key in keys.Take(50))
                {
                    var document = _source.Get(key)!;
                    _source.Remove(key);
                    _source.Add(document);
                }
            }
        };

        await Run();

        Assert.Equal(600, _store.Count());
        Assert.All(keys, key => Assert.Equal("mark", _store.GetVariants(key).Single(v => v.Culture == Nl).BaselineFingerprint));
    }

    [Fact]
    public async Task Rebuild_writes_one_transaction_per_page_and_one_for_the_orphans()
    {
        AddDocuments(450);

        await Run();

        Assert.Equal(3, _store.SaveManyCalls); // pages of 200, 200 and 50; nothing to remove
    }

    [Fact]
    public async Task Orphans_are_handled_in_chunks_of_200_with_one_write_each()
    {
        for (var i = 0; i < 450; i++)
        {
            var key = Guid.NewGuid();
            _store.Save(new IndexResult(new DocumentRow(key, 1000 + i, null, "-1,9", "00009", 1, "page", Guid.Empty, "gone"), []));
        }

        await Run();

        Assert.Equal(0, _store.Count());
        Assert.Equal(3, _store.SaveManyCalls);
    }

    [Fact]
    public async Task Failing_rebuild_keeps_orphans_and_stores_nothing()
    {
        AddDocuments(450);
        var orphan = Guid.NewGuid();
        _store.Save(new IndexResult(
            new DocumentRow(orphan, 99, null, "-1,99", "00099", 1, "page", Guid.Empty, "orphan"), []));
        _source.OnGetPage = page =>
        {
            if (page == 1)
            {
                throw new InvalidOperationException("Database gone");
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run());

        Assert.NotNull(_store.GetDocument(orphan));
        Assert.Empty(_keyValues.Values);
    }

    [Fact]
    public async Task Cancelled_rebuild_keeps_orphans_and_stores_nothing()
    {
        AddDocuments(450);
        var orphan = Guid.NewGuid();
        _store.Save(new IndexResult(
            new DocumentRow(orphan, 99, null, "-1,99", "00099", 1, "page", Guid.Empty, "orphan"), []));
        using var cancel = new CancellationTokenSource();
        _source.OnGetPage = page =>
        {
            if (page == 1)
            {
                cancel.Cancel();
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(cancellationToken: cancel.Token));

        Assert.NotNull(_store.GetDocument(orphan));
        Assert.Empty(_keyValues.Values);
    }

    [Fact]
    public async Task Rebuild_rejects_the_save_mode()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Run(IndexMode.Save));
    }

    [Fact]
    public void Excluded_types_hash_ignores_order_casing_and_blanks()
    {
        Assert.Equal(RebuildJob.ExcludedTypesHash(["Alpha", "beta"]), RebuildJob.ExcludedTypesHash(["BETA", " alpha", "", "alpha"]));
        Assert.NotEqual(RebuildJob.ExcludedTypesHash(["alpha"]), RebuildJob.ExcludedTypesHash([]));
    }

    [Theory]
    [InlineData(null, null, "en-US", null, "h", IndexMode.Rebuild)]
    [InlineData("t", "en-US", "en-US", "h", "h", null)]
    [InlineData("t", "EN-us", "en-US", "h", "h", null)]
    [InlineData(null, "en-US", "en-US", "h", "h", IndexMode.Rebuild)]
    [InlineData("t", "en-US", "en-US", "old", "h", IndexMode.Rebuild)]
    [InlineData("t", "en-US", "en-US", null, "h", IndexMode.Rebuild)]
    [InlineData("t", "en-US", "nl-NL", "h", "h", IndexMode.RebuildClearBaselines)]
    [InlineData(null, "en-US", "nl-NL", null, "h", IndexMode.RebuildClearBaselines)]
    [InlineData("t", null, "en-US", "h", "h", IndexMode.Rebuild)]
    public void Startup_check_decides_the_rebuild(
        string? lastRebuild, string? storedDefault, string current, string? storedHash, string hash, IndexMode? expected)
    {
        Assert.Equal(expected, StartupCheck.Decide(lastRebuild, storedDefault, current, storedHash, hash));
    }

    // The scheduler on a real job runner: one job at a time.
    public sealed class SchedulerTests
    {
        private readonly FakeSnapshotSource _source = new();
        private readonly FakeSiteSource _sites = new();
        private readonly FakeStatusStore _store = new();
        private readonly FakeKeyValueService _keyValues = new();
        private readonly ManualTimeProvider _time = new(Start);
        private readonly JobRunner _runner;
        private readonly RebuildScheduler _scheduler;
        private readonly Guid _key = Guid.NewGuid();

        public SchedulerTests()
        {
            _keyValues.Values[Constants.JobStateKey] = string.Empty;
            var reindexer = new Reindexer(_source, _sites, _store, _time);
            var services = new ServiceCollection();
            services.AddTransient(_ => new RebuildJob(
                _source, reindexer, _keyValues, _sites, new TestOptionsMonitor<TranslationHealthOptions>(new TranslationHealthOptions()), _time));
            var provider = services.BuildServiceProvider();
            _runner = new JobRunner(_keyValues, provider.GetRequiredService<IServiceScopeFactory>(), _time, new ListLogger<JobRunner>());
            _scheduler = new RebuildScheduler(_runner, _sites, _keyValues, new ListLogger<RebuildScheduler>());
            _source.Add(Doc(_key, 1));
            _store.Save(new IndexResult(
                new DocumentRow(_key, 1, null, "-1,1", "00001", 1, "page", Guid.Empty, "n"), [StoredNl(_key, "old-baseline")]));
        }

        // The first job stops inside its first page read until released.
        private (ManualResetEventSlim Entered, ManualResetEventSlim Release) BlockFirstRun()
        {
            var entered = new ManualResetEventSlim();
            var release = new ManualResetEventSlim();
            _source.OnGetPage = _ =>
            {
                _source.OnGetPage = null;
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            };
            return (entered, release);
        }

        [Fact]
        public async Task Rebuild_requested_while_running_runs_once_afterwards()
        {
            var (entered, release) = BlockFirstRun();
            Assert.NotNull(_scheduler.Request(IndexMode.Rebuild));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));

            // Asked for several times meanwhile: remembered once, in the strongest mode.
            Assert.Null(_scheduler.Request(IndexMode.RebuildClearBaselines));
            Assert.Null(_scheduler.Request(IndexMode.Rebuild));
            Assert.Null(_scheduler.StartPending());
            Assert.Equal(IndexMode.RebuildClearBaselines, _scheduler.PendingMode);

            release.Set();
            await _runner.LastRun!;
            Assert.Equal(1, _source.GetPageCalls);

            Assert.NotNull(_scheduler.StartPending());
            await _runner.LastRun!;
            Assert.Equal(2, _source.GetPageCalls);
            Assert.Null(_scheduler.PendingMode);
            Assert.Null(_store.GetVariants(_key).Single(v => v.Culture == Nl).BaselineFingerprint);

            Assert.Null(_scheduler.StartPending());
            Assert.Equal(2, _source.GetPageCalls);
        }

        [Fact]
        public async Task Try_start_now_starts_when_idle_and_remembers_nothing_when_busy()
        {
            var (entered, release) = BlockFirstRun();
            Assert.NotNull(_scheduler.TryStartNow(IndexMode.Rebuild));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));

            Assert.Null(_scheduler.TryStartNow(IndexMode.RebuildClearBaselines));
            Assert.Null(_scheduler.PendingMode);
            Assert.False(_scheduler.HasPending);

            release.Set();
            await _runner.LastRun!;
            Assert.Null(_scheduler.StartPending());
            Assert.Equal(1, _source.GetPageCalls);
        }

        [Fact]
        public async Task The_first_rebuild_keeps_baselines_when_nothing_stronger_was_asked()
        {
            Assert.NotNull(_scheduler.Request(IndexMode.Rebuild));
            await _runner.LastRun!;

            Assert.Equal("old-baseline", _store.GetVariants(_key).Single(v => v.Culture == Nl).BaselineFingerprint);
            Assert.Equal(JobStatus.Completed, _runner.Current()!.Status);
        }

        [Fact]
        public async Task A_burst_of_configuration_changes_gives_one_rebuild()
        {
            _scheduler.NoteConfigurationChanged();
            _scheduler.NoteConfigurationChanged();
            _scheduler.NoteConfigurationChanged();
            Assert.Null(_runner.Current());

            Assert.NotNull(_scheduler.StartPending());
            await _runner.LastRun!;
            Assert.Null(_scheduler.StartPending());

            Assert.Equal(1, _source.GetPageCalls);
            Assert.False(_scheduler.HasPending);
        }

        [Fact]
        public async Task A_configuration_change_during_a_clearing_rebuild_does_not_clear_again()
        {
            // The default language changed: the running rebuild clears the baselines and then stores the new default.
            _keyValues.Values[Constants.DefaultCultureKey] = "nl-NL";
            var (entered, release) = BlockFirstRun();
            Assert.NotNull(_scheduler.Request(IndexMode.RebuildClearBaselines));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));

            _scheduler.NoteConfigurationChanged(); // a domain was saved meanwhile
            Assert.Null(_scheduler.StartPending());
            Assert.True(_scheduler.HasPending);

            release.Set();
            await _runner.LastRun!;
            _store.SaveVariant(_store.GetVariants(_key).Single(v => v.Culture == Nl) with { BaselineFingerprint = "marked-later" });

            Assert.NotNull(_scheduler.StartPending());
            await _runner.LastRun!;
            Assert.Equal("marked-later", _store.GetVariants(_key).Single(v => v.Culture == Nl).BaselineFingerprint);
            Assert.False(_scheduler.HasPending);
        }

        [Theory]
        [InlineData("nl-NL", true)]
        [InlineData("en-US", false)]
        [InlineData(null, false)]
        public async Task A_configuration_change_starts_the_rebuild_the_default_language_calls_for(string? storedDefault, bool clears)
        {
            if (storedDefault is not null)
            {
                _keyValues.Values[Constants.DefaultCultureKey] = storedDefault;
            }

            _scheduler.NoteConfigurationChanged();
            Assert.NotNull(_scheduler.StartPending());
            await _runner.LastRun!;

            var baseline = _store.GetVariants(_key).Single(v => v.Culture == Nl).BaselineFingerprint;
            Assert.Equal(clears ? null : "old-baseline", baseline);
        }
    }
}
