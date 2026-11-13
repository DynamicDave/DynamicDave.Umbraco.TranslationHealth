using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace DynamicDave.Umbraco.TranslationHealth.Persistence;

/// <summary>One applied dictionary import.</summary>
[TableName(TableName)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
public class ImportDto
{
    public const string TableName = "dynamicDaveTranslationHealthImport";

    [Column("id")]
    [PrimaryKeyColumn(AutoIncrement = true)]
    public int Id { get; set; }

    [Column("key")]
    [Index(IndexTypes.UniqueNonClustered, Name = "IX_dynamicDaveTranslationHealthImport_key")]
    public Guid Key { get; set; }

    [Column("createdUtc")]
    public DateTime CreatedUtc { get; set; }

    [Column("userKey")]
    public Guid UserKey { get; set; }

    [Column("userName")]
    [Length(255)]
    public string UserName { get; set; } = string.Empty;

    [Column("fileName")]
    [Length(255)]
    public string FileName { get; set; } = string.Empty;

    [Column("format")]
    [Length(20)]
    public string Format { get; set; } = string.Empty;

    [Column("overwrite")]
    public bool Overwrite { get; set; }

    [Column("createKeys")]
    public bool CreateKeys { get; set; }

    [Column("applied")]
    public int Applied { get; set; }

    [Column("skipped")]
    public int Skipped { get; set; }

    /// <summary><see cref="ImportState"/>.</summary>
    [Column("state")]
    public int State { get; set; }

    [Column("undoneUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTime? UndoneUtc { get; set; }

    [Column("undoneBy")]
    [Length(255)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? UndoneBy { get; set; }
}
