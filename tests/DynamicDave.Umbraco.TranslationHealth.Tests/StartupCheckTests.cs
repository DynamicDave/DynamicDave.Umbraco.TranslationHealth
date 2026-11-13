using DynamicDave.Umbraco.TranslationHealth.Indexing;
using DynamicDave.Umbraco.TranslationHealth.Jobs;
using DynamicDave.Umbraco.TranslationHealth.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Sync;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class StartupCheckTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeSnapshotSource _source = new();
    private readonly FakeSiteSource _sites = new();
    private readonly FakeStatusStore _store = new();
    private readonly FakeKeyValueService _keyValues = new();
    private readonly FakeRuntimeState _runtime;
    private readonly RoleAccessor _role = new();
    private readonly JobRunner _runner;
    private readonly StartupCheck _check;

    public StartupCheckTests()
    {
        var time = new ManualTimeProvider(Start);
        _keyValues.Values[Constants.JobStateKey] = string.Empty;
        var options = new TestOptionsMonitor<TranslationHealthOptions>(new TranslationHealthOptions());
        var reindexer = new Reindexer(_source, _sites, _store, time);
        var services = new ServiceCollection();
        services.AddTransient(_ => new RebuildJob(_source, reindexer, _keyValues, _sites, options, time));
        _runner = new JobRunner(
            _keyValues, services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), time, new ListLogger<JobRunner>());
        var scheduler = new RebuildScheduler(_runner, _sites, _keyValues, new ListLogger<RebuildScheduler>());
        var (state, control) = FakeRuntimeState.Create();
        _runtime = control;
        _check = new StartupCheck(state, _role, _sites, _keyValues, options, scheduler, new ListLogger<StartupCheck>());
    }

    [Theory]
    [InlineData(ServerRole.Single)]
    [InlineData(ServerRole.SchedulingPublisher)]
    public async Task An_empty_store_starts_a_rebuild_on_the_server_that_schedules(ServerRole role)
    {
        _role.Role = role;

        _check.Run();
        await _runner.LastRun!;

        Assert.Equal(JobStatus.Completed, _runner.Current()!.Status);
        Assert.Equal(_sites.Default, _keyValues.GetValue(Constants.DefaultCultureKey));
    }

    [Fact]
    public void A_subscriber_server_leaves_it_to_the_others()
    {
        _role.Role = ServerRole.Subscriber;

        _check.Run();

        Assert.Null(_runner.Current());
    }

    [Fact]
    public async Task An_unknown_role_is_tried_again_and_the_check_runs_only_once()
    {
        _role.Role = ServerRole.Unknown;
        _check.Run();
        Assert.Null(_runner.Current());

        _role.Role = ServerRole.Single;
        _check.Run();
        await _runner.LastRun!;
        var first = _runner.Current()!.Id;

        // Done: the store is still empty (no documents), which would ask for another rebuild if the check ran again.
        _check.Run();
        Assert.Equal(first, _runner.Current()!.Id);
    }

    [Fact]
    public void Nothing_happens_before_the_runtime_runs()
    {
        _role.Role = ServerRole.Single;
        _runtime.Level = RuntimeLevel.Upgrade;

        _check.Run();

        Assert.Null(_runner.Current());
    }

    [Fact]
    public async Task A_rebuilt_store_without_documents_starts_no_rebuild()
    {
        // A site without variant documents has an empty store after every rebuild.
        _role.Role = ServerRole.Single;
        _keyValues.Values[Constants.LastRebuildKey] = "2026-11-13T12:00:00.0000000Z";
        _keyValues.Values[Constants.DefaultCultureKey] = _sites.Default;
        _keyValues.Values[Constants.ExcludedTypesKey] = RebuildJob.ExcludedTypesHash([]);

        _check.Run();
        await Task.Delay(50);

        Assert.Null(_runner.Current());
        Assert.Equal(0, _store.Count());
    }

    [Fact]
    public async Task A_failing_check_is_tried_again()
    {
        _role.Role = ServerRole.Single;
        _keyValues.Values[Constants.JobStateKey] = string.Empty;
        _sites.Failing = true;
        _check.Run();
        Assert.Null(_runner.Current());

        _sites.Failing = false;
        _check.Run();
        await _runner.LastRun!;

        Assert.Equal(JobStatus.Completed, _runner.Current()!.Status);
    }

    [Fact]
    public async Task A_current_store_starts_no_rebuild()
    {
        _role.Role = ServerRole.Single;
        _keyValues.Values[Constants.LastRebuildKey] = "2026-11-13T12:00:00.0000000Z";
        _keyValues.Values[Constants.DefaultCultureKey] = _sites.Default;
        _keyValues.Values[Constants.ExcludedTypesKey] = RebuildJob.ExcludedTypesHash([]);

        _check.Run();
        await Task.Delay(50);

        Assert.Null(_runner.Current());
    }

    private sealed class RoleAccessor : IServerRoleAccessor
    {
        public ServerRole Role { get; set; }

        public ServerRole CurrentServerRole => Role;
    }
}
