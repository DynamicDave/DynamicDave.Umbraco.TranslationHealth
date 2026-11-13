using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace DynamicDave.Umbraco.TranslationHealth.Persistence;

/// <summary>The translation status of one document in one language. Enums are stored as their names.</summary>
[TableName(TableName)]
[PrimaryKey("documentKey,culture", AutoIncrement = false)]
[ExplicitColumns]
public class VariantDto
{
    public const string TableName = "dynamicDaveTranslationHealthVariant";

    [Column("documentKey")]
    [PrimaryKeyColumn(AutoIncrement = false, Clustered = true, Name = "PK_dynamicDaveTranslationHealthVariant", OnColumns = "documentKey, culture")]
    public Guid DocumentKey { get; set; }

    [Column("culture")]
    [Length(20)]
    public string Culture { get; set; } = string.Empty;

    [Column("status")]
    [Length(32)]
    [Index(IndexTypes.NonClustered, Name = "IX_dynamicDaveTranslationHealthVariant_culture_status", ForColumns = "culture,status")]
    public string Status { get; set; } = string.Empty;

    [Column("outdated")]
    [Index(IndexTypes.NonClustered, Name = "IX_dynamicDaveTranslationHealthVariant_culture_outdated", ForColumns = "culture,outdated")]
    public bool Outdated { get; set; }

    [Column("mode")]
    [Length(32)]
    public string Mode { get; set; } = string.Empty;

    [Column("updateDateUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? UpdateDateUtc { get; set; }

    [Column("publishDateUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? PublishDateUtc { get; set; }

    [Column("name")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Name { get; set; }

    [Column("textFingerprint")]
    [Length(128)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? TextFingerprint { get; set; }

    [Column("baselineFingerprint")]
    [Length(128)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? BaselineFingerprint { get; set; }

    [Column("baselineSource")]
    [Length(32)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? BaselineSource { get; set; }

    [Column("markedByUserKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? MarkedByUserKey { get; set; }

    [Column("markedUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? MarkedUtc { get; set; }

    [Column("outdatedSinceUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? OutdatedSinceUtc { get; set; }
}
