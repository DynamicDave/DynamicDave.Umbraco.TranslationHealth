using DynamicDave.Umbraco.TranslationHealth.Status;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Extensions;

namespace DynamicDave.Umbraco.TranslationHealth.Persistence;

public sealed class TranslationStatusStore(IScopeProvider scopeProvider) : ITranslationStatusStore
{
    // SQL Server allows 2,100 parameters per statement; keys go into IN lists in chunks of at most this many.
    internal const int ChunkSize = 1000;

    public IReadOnlyList<VariantRow> GetVariants(Guid documentKey)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        return scope.Database.Fetch<VariantDto>(scope.SqlContext.Sql()
                .Select<VariantDto>().From<VariantDto>()
                .Where<VariantDto>(x => x.DocumentKey == documentKey)
                .OrderBy<VariantDto>(x => x.Culture))
            .Select(StatusRowMapping.ToRow)
            .ToList();
    }

    public DocumentRow? GetDocument(Guid key)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = scope.Database.FirstOrDefault<DocumentDto>(scope.SqlContext.Sql()
            .Select<DocumentDto>().From<DocumentDto>().Where<DocumentDto>(x => x.Key == key));
        return dto is null ? null : StatusRowMapping.ToRow(dto);
    }

    public void Save(IndexResult result)
    {
        var key = result.Document.Key;
        using var scope = scopeProvider.CreateScope();
        DeleteVariants(scope, key);
        scope.Database.Execute(scope.SqlContext.Sql().Delete<DocumentDto>().Where<DocumentDto>(x => x.Key == key));
        scope.Database.Insert(StatusRowMapping.ToDto(result.Document));
        foreach (var variant in result.Variants)
        {
            scope.Database.Insert(StatusRowMapping.ToDto(variant with { DocumentKey = key }));
        }

        scope.Complete();
    }

    public void SaveVariant(VariantRow row)
    {
        var key = row.DocumentKey;
        var culture = row.Culture;
        using var scope = scopeProvider.CreateScope();
        scope.Database.Execute(scope.SqlContext.Sql().Delete<VariantDto>()
            .Where<VariantDto>(x => x.DocumentKey == key && x.Culture == culture));
        scope.Database.Insert(StatusRowMapping.ToDto(row));
        scope.Complete();
    }

    public void Remove(IReadOnlyCollection<Guid> documentKeys)
    {
        if (documentKeys.Count == 0)
        {
            return;
        }

        using var scope = scopeProvider.CreateScope();
        RemoveKeys(scope, documentKeys);
        scope.Complete();
    }

    // Up to ~50,000 keys to keep: read the stored keys and delete the difference in chunks instead of a huge NOT IN.
    public void RemoveExcept(IReadOnlySet<Guid> keepKeys)
    {
        using var scope = scopeProvider.CreateScope();
        var stored = scope.Database.Fetch<Guid>(scope.SqlContext.Sql().Select<DocumentDto>(x => x.Key).From<DocumentDto>());
        var obsolete = stored.Where(key => !keepKeys.Contains(key)).ToList();
        RemoveKeys(scope, obsolete);
        scope.Complete();
    }

    public int Count()
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        return scope.Database.ExecuteScalar<int>(scope.SqlContext.Sql().SelectCount().From<DocumentDto>());
    }

    private static void RemoveKeys(IScope scope, IReadOnlyCollection<Guid> keys)
    {
        foreach (var chunk in keys.Chunk(ChunkSize))
        {
            scope.Database.Execute(scope.SqlContext.Sql().Delete<VariantDto>().WhereIn<VariantDto>(x => x.DocumentKey, chunk));
            scope.Database.Execute(scope.SqlContext.Sql().Delete<DocumentDto>().WhereIn<DocumentDto>(x => x.Key, chunk));
        }
    }

    private static void DeleteVariants(IScope scope, Guid documentKey)
        => scope.Database.Execute(scope.SqlContext.Sql().Delete<VariantDto>().Where<VariantDto>(x => x.DocumentKey == documentKey));
}
