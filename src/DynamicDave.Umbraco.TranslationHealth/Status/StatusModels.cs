using System.Text.Json.Serialization;
namespace DynamicDave.Umbraco.TranslationHealth.Status;

[JsonConverter(typeof(JsonStringEnumConverter<CellStatus>))]
public enum CellStatus
{
    NotApplicable,
    Missing,
    Draft,
    Published,
    PendingChanges,
}

[JsonConverter(typeof(JsonStringEnumConverter<TrackingMode>))]
public enum TrackingMode
{
    Date,
    Fingerprint,
}

[JsonConverter(typeof(JsonStringEnumConverter<BaselineSource>))]
public enum BaselineSource
{
    Saved,
    MarkedUpToDate,
}

public enum IndexMode
{
    Save,
    Rebuild,
    RebuildClearBaselines,
}

public record DocumentRow(
    Guid Key,
    int Id,
    Guid? ParentKey,
    string Path,
    string SortPath,
    int Level,
    string ContentTypeAlias,
    Guid SiteRootKey,
    string Name);

public record VariantRow(
    Guid DocumentKey,
    string Culture,
    CellStatus Status,
    bool Outdated,
    TrackingMode Mode,
    DateTime? UpdateDateUtc,
    DateTime? PublishDateUtc,
    string? Name,
    string? TextFingerprint,
    string? BaselineFingerprint,
    BaselineSource? BaselineSource,
    Guid? MarkedByUserKey,
    DateTime? MarkedUtc,
    DateTime? OutdatedSinceUtc);

public record IndexResult(DocumentRow Document, IReadOnlyList<VariantRow> Variants);
