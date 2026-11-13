using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace DynamicDave.Umbraco.TranslationHealth.Persistence;

/// <summary>One dictionary value written by an import, with the value it replaced (for undo).</summary>
[TableName(TableName)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
public class ImportRowDto
{
    public const string TableName = "dynamicDaveTranslationHealthImportRow";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("importId")]
    [ForeignKey(typeof(ImportDto))]
    [Index(IndexTypes.NonClustered, Name = "IX_dynamicDaveTranslationHealthImportRow_importId")]
    public int ImportId { get; set; }

    [Column("key")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    public string Key { get; set; } = string.Empty;

    [Column("culture")]
    [Length(20)]
    public string Culture { get; set; } = string.Empty;

    [Column("oldValue")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? OldValue { get; set; }

    [Column("newValue")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    public string NewValue { get; set; } = string.Empty;

    [Column("keyCreated")]
    [Constraint(Default = "0")]
    public bool KeyCreated { get; set; }
}
