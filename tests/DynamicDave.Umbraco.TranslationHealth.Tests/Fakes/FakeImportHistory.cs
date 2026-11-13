using DynamicDave.Umbraco.TranslationHealth.Persistence;

namespace DynamicDave.Umbraco.TranslationHealth.Tests.Fakes;

internal sealed class FakeImportHistory : IImportHistory
{
    private readonly object _lock = new();
    private readonly List<(ImportRecord Record, IReadOnlyList<ImportRowRecord> Rows)> _imports = [];

    public int SaveCount { get; private set; }

    public void Save(ImportRecord import, IReadOnlyList<ImportRowRecord> rows)
    {
        lock (_lock)
        {
            SaveCount++;
            _imports.Add((import, [.. rows]));
        }
    }

    public (IReadOnlyList<ImportRecord> Items, int Total) List(int skip, int take)
    {
        lock (_lock)
        {
            var ordered = _imports.Select(i => i.Record).OrderByDescending(r => r.CreatedUtc).ToList();
            return (ordered.Skip(skip).Take(take).ToList(), ordered.Count);
        }
    }

    public ImportRecord? Get(Guid key)
    {
        lock (_lock)
        {
            return _imports.Select(i => i.Record).FirstOrDefault(r => r.Key == key);
        }
    }

    public IReadOnlyList<ImportRowRecord> Rows(Guid key)
    {
        lock (_lock)
        {
            return _imports.FirstOrDefault(i => i.Record.Key == key).Rows ?? [];
        }
    }

    public bool MarkUndone(Guid key, string userName, DateTime utc)
    {
        lock (_lock)
        {
            var index = _imports.FindIndex(i => i.Record.Key == key && i.Record.State == ImportState.Applied);
            if (index < 0)
            {
                return false;
            }

            _imports[index] = (_imports[index].Record with { State = ImportState.Undone, UndoneUtc = utc, UndoneBy = userName }, _imports[index].Rows);
            return true;
        }
    }
}
