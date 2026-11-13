using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Extensions;

namespace DynamicDave.Umbraco.TranslationHealth.Persistence;

public sealed class ImportHistory(IScopeProvider scopeProvider) : IImportHistory
{
    public void Save(ImportRecord import, IReadOnlyList<ImportRowRecord> rows)
    {
        var dto = new ImportDto
        {
            Key = import.Key,
            CreatedUtc = StatusRowMapping.AsUtc(import.CreatedUtc),
            UserKey = import.UserKey,
            UserName = import.UserName,
            FileName = import.FileName,
            Format = import.Format,
            Overwrite = import.Overwrite,
            CreateKeys = import.CreateKeys,
            Applied = import.Applied,
            Skipped = import.Skipped,
            State = (int)import.State,
            UndoneUtc = import.UndoneUtc is { } undone ? StatusRowMapping.AsUtc(undone) : null,
            UndoneBy = import.UndoneBy,
        };

        using var scope = scopeProvider.CreateScope();
        scope.Database.Insert(dto);
        foreach (var row in rows)
        {
            scope.Database.Insert(new ImportRowDto
            {
                ImportId = dto.Id,
                Key = row.Key,
                Culture = row.Culture,
                OldValue = row.OldValue,
                NewValue = row.NewValue,
                KeyCreated = row.KeyCreated,
            });
        }

        scope.Complete();
    }

    public (IReadOnlyList<ImportRecord> Items, int Total) List(int skip, int take)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var total = scope.Database.ExecuteScalar<int>(scope.SqlContext.Sql().SelectCount().From<ImportDto>());
        var items = scope.Database.SkipTake<ImportDto>(skip, take, scope.SqlContext.Sql()
            .Select<ImportDto>().From<ImportDto>()
            .OrderByDescending<ImportDto>(x => x.CreatedUtc, x => x.Id));
        return (items.Select(ToRecord).ToList(), total);
    }

    public ImportRecord? Get(Guid key)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = FindImport(scope, key);
        return dto is null ? null : ToRecord(dto);
    }

    public IReadOnlyList<ImportRowRecord> Rows(Guid key)
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var import = FindImport(scope, key);
        if (import is null)
        {
            return [];
        }

        var importId = import.Id;
        return scope.Database.Fetch<ImportRowDto>(scope.SqlContext.Sql()
                .Select<ImportRowDto>().From<ImportRowDto>()
                .Where<ImportRowDto>(x => x.ImportId == importId)
                .OrderBy<ImportRowDto>(x => x.Id))
            .Select(dto => new ImportRowRecord(dto.Key, dto.Culture, dto.OldValue, dto.NewValue, dto.KeyCreated))
            .ToList();
    }

    public void MarkUndone(Guid key, string userName, DateTime utc)
    {
        var undone = (int)ImportState.Undone;
        DateTime? undoneUtc = StatusRowMapping.AsUtc(utc);
        using var scope = scopeProvider.CreateScope();
        scope.Database.Execute(scope.SqlContext.Sql()
            .Update<ImportDto>(u => u
                .Set(x => x.State, undone)
                .Set(x => x.UndoneUtc, undoneUtc)
                .Set(x => x.UndoneBy, userName))
            .Where<ImportDto>(x => x.Key == key));
        scope.Complete();
    }

    private static ImportDto? FindImport(IScope scope, Guid key)
        => scope.Database.FirstOrDefault<ImportDto>(scope.SqlContext.Sql()
            .Select<ImportDto>().From<ImportDto>().Where<ImportDto>(x => x.Key == key));

    private static ImportRecord ToRecord(ImportDto dto) => new(
        dto.Key,
        StatusRowMapping.AsUtc(dto.CreatedUtc),
        dto.UserKey,
        dto.UserName,
        dto.FileName,
        dto.Format,
        dto.Overwrite,
        dto.CreateKeys,
        dto.Applied,
        dto.Skipped,
        (ImportState)dto.State,
        dto.UndoneUtc is { } undoneUtc ? StatusRowMapping.AsUtc(undoneUtc) : null,
        dto.UndoneBy);
}
