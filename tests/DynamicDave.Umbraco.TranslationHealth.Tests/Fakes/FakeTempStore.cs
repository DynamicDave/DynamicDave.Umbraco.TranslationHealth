using DynamicDave.Umbraco.TranslationHealth.Persistence;

namespace DynamicDave.Umbraco.TranslationHealth.Tests.Fakes;

/// <summary>In-memory temp store with the expiry rule of the real one; counts reads and writes. Thread-safe.</summary>
internal sealed class FakeTempStore(TimeProvider timeProvider) : ITempStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(Guid, TempKind), TempEntry> _entries = [];

    public int Reads { get; private set; }

    public int Writes { get; private set; }

    public void Put(Guid key, TempKind kind, string content, string? fileName)
    {
        lock (_gate)
        {
            Writes++;
            _entries[(key, kind)] = new TempEntry(content, fileName, Now());
        }
    }

    public TempEntry? Get(Guid key, TempKind kind)
    {
        lock (_gate)
        {
            Reads++;
            return _entries.TryGetValue((key, kind), out var entry) && !TempStore.IsExpired(entry.CreatedUtc, Now()) ? entry : null;
        }
    }

    public bool Delete(Guid key, TempKind kind)
    {
        lock (_gate)
        {
            Writes++;
            return _entries.Remove((key, kind), out var entry) && !TempStore.IsExpired(entry.CreatedUtc, Now());
        }
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
}
