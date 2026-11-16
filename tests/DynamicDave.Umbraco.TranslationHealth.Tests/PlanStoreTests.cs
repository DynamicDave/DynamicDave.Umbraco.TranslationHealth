using DynamicDave.Umbraco.TranslationHealth.Files;
using DynamicDave.Umbraco.TranslationHealth.Importing;
using DynamicDave.Umbraco.TranslationHealth.Tests.Fakes;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class PlanStoreTests
{
    private static readonly Guid User = Guid.NewGuid();

    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeTempStore _temp;
    private readonly PlanStore _plans;

    public PlanStoreTests()
    {
        _temp = new FakeTempStore(_time);
        _plans = new PlanStore(_temp, _time);
    }

    private static ImportPlan Plan() => new(
        Guid.NewGuid(),
        "a.csv",
        ImportFileFormat.Csv,
        new ImportOptions(false, false),
        [],
        [new PlanItem(1, 2, "Key", "nl-NL", ImportOutcome.NewValue, ImportErrorReason.None, null, "Waarde", null, null, false, true)]);

    [Fact]
    public void Reading_a_plan_writes_nothing_and_deserializes_it_once()
    {
        var plan = Plan();
        _plans.Put(User, plan);
        var writes = _temp.Writes;

        var first = _plans.Get(plan.Id, User);
        var second = _plans.Get(plan.Id, User);

        Assert.Equal(plan.Id, first!.Id);
        Assert.Same(first, second);
        Assert.Equal(writes, _temp.Writes);
        Assert.Equal(0, _temp.Reads); // the server that made the plan has it in memory
    }

    [Fact]
    public void Another_server_reads_the_plan_from_the_store_once()
    {
        var plan = Plan();
        _plans.Put(User, plan);
        var other = new PlanStore(_temp, _time);

        Assert.Equal(plan.Id, other.Get(plan.Id, User)!.Id);
        Assert.Equal(plan.Id, other.Get(plan.Id, User)!.Id);
        Assert.Equal(1, _temp.Reads);
    }

    [Fact]
    public void An_expired_plan_reads_as_missing_from_memory_and_from_the_store()
    {
        var plan = Plan();
        _plans.Put(User, plan);
        var other = new PlanStore(_temp, _time);
        Assert.NotNull(other.Get(plan.Id, User));

        _time.Advance(TimeSpan.FromMinutes(60));
        Assert.NotNull(_plans.Get(plan.Id, User));

        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(_plans.Get(plan.Id, User));
        Assert.Null(other.Get(plan.Id, User));
        Assert.Null(new PlanStore(_temp, _time).Get(plan.Id, User));
    }

    [Fact]
    public void Someone_elses_plan_is_not_there()
    {
        var plan = Plan();
        _plans.Put(User, plan);

        Assert.Null(_plans.Get(plan.Id, Guid.NewGuid()));
        Assert.Null(new PlanStore(_temp, _time).Get(plan.Id, Guid.NewGuid()));
    }

    [Fact]
    public void Applying_claims_the_plan_once_and_evicts_it_from_memory()
    {
        var plan = Plan();
        _plans.Put(User, plan);
        Assert.NotNull(_plans.Get(plan.Id, User));

        Assert.True(_plans.Claim(plan.Id));

        Assert.Null(_plans.Get(plan.Id, User));
        Assert.False(_plans.Claim(plan.Id));
    }

    [Fact]
    public void The_cache_holds_a_bounded_number_of_plans()
    {
        var plans = Enumerable.Range(0, PlanStore.CacheCapacity + 1).Select(_ => Plan()).ToList();
        foreach (var plan in plans) _plans.Put(User, plan);

        // The first one was dropped from memory, but is still in the store.
        Assert.Equal(plans[0].Id, _plans.Get(plans[0].Id, User)!.Id);
        Assert.Equal(1, _temp.Reads);
        Assert.NotNull(_plans.Get(plans[^1].Id, User));
        Assert.Equal(1, _temp.Reads);
    }
}
