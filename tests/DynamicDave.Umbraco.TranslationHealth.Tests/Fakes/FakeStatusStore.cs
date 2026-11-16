using DynamicDave.Umbraco.TranslationHealth.Persistence;
using DynamicDave.Umbraco.TranslationHealth.Status;

namespace DynamicDave.Umbraco.TranslationHealth.Tests.Fakes;

/// <summary>In-memory status store with the semantics of the real one (Save replaces the variants of the document). Thread-safe.</summary>
internal sealed class FakeStatusStore : ITranslationStatusStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, DocumentRow> _documents = [];
    private readonly Dictionary<Guid, List<VariantRow>> _variants = [];

    /// <summary>Runs at the start of <see cref="SaveMany"/> (outside the lock), e.g. to block a writer.</summary>
    public Action? OnSave { get; set; }

    public IReadOnlyCollection<Guid> Keys
    {
        get
        {
            lock (_gate) return [.. _documents.Keys];
        }
    }

    public IReadOnlyList<VariantRow> GetVariants(Guid documentKey)
    {
        lock (_gate) return _variants.TryGetValue(documentKey, out var rows) ? [.. rows] : [];
    }

    public IReadOnlyDictionary<Guid, IReadOnlyList<VariantRow>> GetVariants(IReadOnlyCollection<Guid> documentKeys)
    {
        lock (_gate)
        {
            return documentKeys.Distinct().ToDictionary(
                key => key, key => _variants.TryGetValue(key, out var rows) ? (IReadOnlyList<VariantRow>)[.. rows] : []);
        }
    }

    public IReadOnlyList<Guid> GetKeys()
    {
        lock (_gate) return [.. _documents.Keys];
    }

    /// <summary>Number of <see cref="SaveMany"/> calls: one per write transaction of the real store.</summary>
    public int SaveManyCalls { get; private set; }

    public void SaveMany(IReadOnlyList<IndexResult> results, IReadOnlyCollection<Guid> removals)
    {
        if (results.Count == 0 && removals.Count == 0)
        {
            return; // like the real store: no transaction
        }

        OnSave?.Invoke();
        lock (_gate)
        {
            SaveManyCalls++;
            foreach (var key in removals)
            {
                _documents.Remove(key);
                _variants.Remove(key);
            }

            foreach (var result in results)
            {
                _documents[result.Document.Key] = result.Document;
                _variants[result.Document.Key] = [.. result.Variants];
            }
        }
    }

    /// <summary>Test setup: stores the rows of a document directly (no write is counted, <see cref="OnSave"/> does not run).</summary>
    public void Seed(IndexResult result)
    {
        lock (_gate)
        {
            _documents[result.Document.Key] = result.Document;
            _variants[result.Document.Key] = [.. result.Variants];
        }
    }

    /// <summary>Test inspection: the stored document row, or null.</summary>
    public DocumentRow? Document(Guid key)
    {
        lock (_gate) return _documents.GetValueOrDefault(key);
    }

    public void SaveVariant(VariantRow row)
    {
        lock (_gate)
        {
            var rows = _variants.GetValueOrDefault(row.DocumentKey) ?? [];
            rows.RemoveAll(r => r.Culture == row.Culture);
            rows.Add(row);
            _variants[row.DocumentKey] = rows;
        }
    }
}
