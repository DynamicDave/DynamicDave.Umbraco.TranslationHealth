using DynamicDave.Umbraco.TranslationHealth.Models;
using DynamicDave.Umbraco.TranslationHealth.Persistence;

namespace DynamicDave.Umbraco.TranslationHealth.Importing;

/// <summary>
/// Test-run plans. They are kept in the <see cref="ITempStore"/>, so every server behind a load balancer finds them, and
/// the deserialized plan is cached in memory per process: the UI loads a large plan in many requests, and deserializing
/// it for each would cost more than the request itself. A cached plan expires with the stored one; claiming it (apply)
/// evicts it. A plan that is not cached here (made on another server, or dropped to keep the cache bounded) is read from
/// the store.
/// </summary>
public sealed class PlanStore(ITempStore tempStore, TimeProvider timeProvider)
{
    /// <summary>Plans kept in memory; a plan holds up to 20,000 items, so only a few.</summary>
    public const int CacheCapacity = 8;

    private readonly object _gate = new();
    private readonly Dictionary<Guid, Cached> _cache = [];
    private long _clock;
    private long _evictions;

    public void Put(Guid userKey, ImportPlan plan)
    {
        tempStore.Put(plan.Id, TempKind.Plan, ApiMapping.SerializePlan(userKey, plan), plan.FileName);
        lock (_gate) Remember(plan.Id, new StoredPlan(userKey, plan), Now());
    }

    /// <summary>The plan, when it exists, has not expired and was made by <paramref name="userKey"/> (someone else's plan is as good as not there).</summary>
    public ImportPlan? Get(Guid planId, Guid userKey)
    {
        var stored = Load(planId);
        return stored is not null && stored.UserKey == userKey ? stored.Plan : null;
    }

    /// <summary>Removes the plan; true only for the one caller that removed it from the store, so a plan applies once.</summary>
    public bool Claim(Guid planId)
    {
        var claimed = tempStore.Delete(planId, TempKind.Plan);
        lock (_gate)
        {
            _cache.Remove(planId);
            _evictions++;
        }

        return claimed;
    }

    private StoredPlan? Load(Guid planId)
    {
        long evictions;
        lock (_gate)
        {
            if (_cache.TryGetValue(planId, out var cached))
            {
                if (!TempStore.IsExpired(cached.CreatedUtc, Now()))
                {
                    cached.LastUsed = ++_clock;
                    return cached.Plan;
                }

                _cache.Remove(planId);
            }

            evictions = _evictions;
        }

        if (tempStore.Get(planId, TempKind.Plan) is not { } entry || ApiMapping.ReadPlan(entry.Content) is not { } stored)
        {
            return null;
        }

        lock (_gate)
        {
            // A claim while the store was read: the plan may be gone already, so it is not cached again.
            if (evictions == _evictions)
            {
                Remember(planId, stored, entry.CreatedUtc);
            }
        }

        return stored;
    }

    // Under the lock. Drops expired plans first, then the least recently used one when the cache is full.
    private void Remember(Guid planId, StoredPlan plan, DateTime createdUtc)
    {
        var now = Now();
        foreach (var expired in _cache.Where(pair => TempStore.IsExpired(pair.Value.CreatedUtc, now)).Select(pair => pair.Key).ToList())
        {
            _cache.Remove(expired);
        }

        if (!_cache.ContainsKey(planId) && _cache.Count >= CacheCapacity)
        {
            _cache.Remove(_cache.MinBy(pair => pair.Value.LastUsed).Key);
        }

        _cache[planId] = new Cached(plan, createdUtc) { LastUsed = ++_clock };
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    private sealed class Cached(StoredPlan plan, DateTime createdUtc)
    {
        public StoredPlan Plan { get; } = plan;

        public DateTime CreatedUtc { get; } = createdUtc;

        public long LastUsed { get; set; }
    }
}
