using DynamicDave.Umbraco.TranslationHealth.Dictionary;
using DynamicDave.Umbraco.TranslationHealth.Persistence;

namespace DynamicDave.Umbraco.TranslationHealth.Tests.Fakes;

/// <summary>Delegates to a fake source but throws on the n-th call (1-based) of the chosen write; 0 = never.</summary>
internal sealed class FailingDictionarySource(FakeDictionarySource inner, int failSet = 0, int failCreate = 0, int failDelete = 0) : IDictionarySource
{
    private int _sets;
    private int _creates;
    private int _deletes;

    public IReadOnlyList<DictionaryEntry> GetAll() => inner.GetAll();

    public Task<DictionaryEntry?> GetAsync(Guid id) => inner.GetAsync(id);

    public Task<bool> HasChildrenAsync(Guid id) => inner.HasChildrenAsync(id);

    public Task<SetValuesResult> SetValuesAsync(Guid id, IReadOnlyList<ValueChange> changes, Guid userKey)
        => ++_sets == failSet ? throw new InvalidOperationException("Umbraco refused") : inner.SetValuesAsync(id, changes, userKey);

    public Task<Guid> CreateAsync(string key, Guid? parentId, IReadOnlyDictionary<string, string> values, Guid userKey)
        => ++_creates == failCreate ? throw new InvalidOperationException("Umbraco refused") : inner.CreateAsync(key, parentId, values, userKey);

    public Task DeleteAsync(Guid id, Guid userKey)
        => ++_deletes == failDelete ? throw new InvalidOperationException("Umbraco refused") : inner.DeleteAsync(id, userKey);
}

internal sealed class ThrowingSaveHistory(IImportHistory inner) : IImportHistory
{
    public void Save(ImportRecord import, IReadOnlyList<ImportRowRecord> rows) => throw new IOException("database down");

    public (IReadOnlyList<ImportRecord> Items, int Total) List(int skip, int take) => inner.List(skip, take);

    public ImportRecord? Get(Guid key) => inner.Get(key);

    public IReadOnlyList<ImportRowRecord> Rows(Guid key) => inner.Rows(key);

    public bool MarkUndone(Guid key, string userName, DateTime utc) => inner.MarkUndone(key, userName, utc);
}
