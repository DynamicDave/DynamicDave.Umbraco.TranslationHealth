using System.Text.Json.Serialization;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Extensions;

namespace DynamicDave.Umbraco.TranslationHealth.Persistence;

[JsonConverter(typeof(JsonStringEnumConverter<TempKind>))]
public enum TempKind
{
    Plan,
}

public sealed record TempEntry(string Content, string? FileName, DateTime CreatedUtc);

/// <summary>
/// Test-run plans for one hour, in the database so every server behind a load balancer sees them. Reading only reads:
/// expired entries are filtered out there and deleted by the writes (<see cref="Put"/> and <see cref="Delete"/>).
/// </summary>
public sealed class TempStore(IScopeProvider scopeProvider, TimeProvider timeProvider) : ITempStore
{
    public static readonly TimeSpan TimeToLive = TimeSpan.FromHours(1);

    public static bool IsExpired(DateTime createdUtc, DateTime nowUtc) => createdUtc < nowUtc - TimeToLive;

    public void Put(Guid key, TempKind kind, string content, string? fileName)
    {
        var now = Now();
        var kindValue = (int)kind;
        using var scope = scopeProvider.CreateScope();
        DeleteExpired(scope, now);
        scope.Database.Execute(scope.SqlContext.Sql().Delete<TempDto>().Where<TempDto>(x => x.Key == key && x.Kind == kindValue));
        scope.Database.Insert(new TempDto
        {
            Key = key,
            Kind = kindValue,
            CreatedUtc = now,
            ExpiresUtc = now + TimeToLive,
            FileName = fileName,
            Content = content,
        });
        scope.Complete();
    }

    public TempEntry? Get(Guid key, TempKind kind)
    {
        // The same rule as IsExpired: entries created before now - TimeToLive have expired.
        var cutoff = Now() - TimeToLive;
        var kindValue = (int)kind;
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        var dto = scope.Database.FirstOrDefault<TempDto>(scope.SqlContext.Sql()
            .Select<TempDto>().From<TempDto>().Where<TempDto>(x => x.Key == key && x.Kind == kindValue && x.CreatedUtc >= cutoff));
        return dto is null ? null : new TempEntry(dto.Content, dto.FileName, dto.CreatedUtc);
    }

    public bool Delete(Guid key, TempKind kind)
    {
        var now = Now();
        var kindValue = (int)kind;
        using var scope = scopeProvider.CreateScope();
        DeleteExpired(scope, now);
        var removed = scope.Database.Execute(scope.SqlContext.Sql().Delete<TempDto>().Where<TempDto>(x => x.Key == key && x.Kind == kindValue));
        scope.Complete();
        return removed > 0;
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    // The same rule as IsExpired: entries created before now - TimeToLive.
    private static void DeleteExpired(IScope scope, DateTime nowUtc)
    {
        var cutoff = nowUtc - TimeToLive;
        scope.Database.Execute(scope.SqlContext.Sql().Delete<TempDto>().Where<TempDto>(x => x.CreatedUtc < cutoff));
    }
}
