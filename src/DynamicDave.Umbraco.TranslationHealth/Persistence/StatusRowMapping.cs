using DynamicDave.Umbraco.TranslationHealth.Status;

namespace DynamicDave.Umbraco.TranslationHealth.Persistence;

/// <summary>Maps status rows to their table rows and back. Dates are UTC; NPoco may return them as Unspecified.</summary>
public static class StatusRowMapping
{
    public static VariantDto ToDto(VariantRow row) => new()
    {
        DocumentKey = row.DocumentKey,
        Culture = row.Culture,
        Status = row.Status.ToString(),
        Outdated = row.Outdated,
        Mode = row.Mode.ToString(),
        UpdateDateUtc = ToUtc(row.UpdateDateUtc),
        PublishDateUtc = ToUtc(row.PublishDateUtc),
        Name = row.Name,
        TextFingerprint = row.TextFingerprint,
        BaselineFingerprint = row.BaselineFingerprint,
        BaselineSource = row.BaselineSource?.ToString(),
        MarkedByUserKey = row.MarkedByUserKey,
        MarkedUtc = ToUtc(row.MarkedUtc),
        OutdatedSinceUtc = ToUtc(row.OutdatedSinceUtc),
    };

    public static VariantRow ToRow(VariantDto dto) => new(
        dto.DocumentKey,
        dto.Culture,
        Enum.Parse<CellStatus>(dto.Status),
        dto.Outdated,
        Enum.Parse<TrackingMode>(dto.Mode),
        ToUtc(dto.UpdateDateUtc),
        ToUtc(dto.PublishDateUtc),
        dto.Name,
        dto.TextFingerprint,
        dto.BaselineFingerprint,
        dto.BaselineSource is null ? null : Enum.Parse<BaselineSource>(dto.BaselineSource),
        dto.MarkedByUserKey,
        ToUtc(dto.MarkedUtc),
        ToUtc(dto.OutdatedSinceUtc));

    public static DocumentDto ToDto(DocumentRow row) => new()
    {
        Key = row.Key,
        Id = row.Id,
        ParentKey = row.ParentKey,
        Path = row.Path,
        SortPath = row.SortPath,
        Level = row.Level,
        ContentTypeAlias = row.ContentTypeAlias,
        SiteRootKey = row.SiteRootKey,
        Name = row.Name,
    };

    public static DocumentRow ToRow(DocumentDto dto) => new(
        dto.Key, dto.Id, dto.ParentKey, dto.Path, dto.SortPath, dto.Level, dto.ContentTypeAlias, dto.SiteRootKey, dto.Name);

    // Unspecified is taken as UTC (that is what is stored); Local is converted.
    private static DateTime? ToUtc(DateTime? value) => value is { } v ? ToUtc(v) : null;

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    internal static DateTime AsUtc(DateTime value) => ToUtc(value);
}
