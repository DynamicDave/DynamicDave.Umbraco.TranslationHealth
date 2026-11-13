using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace DynamicDave.Umbraco.TranslationHealth.Persistence;

/// <summary>The indexed facts of one document; its languages are in <see cref="VariantDto"/>.</summary>
[TableName(TableName)]
[PrimaryKey("key", AutoIncrement = false)]
[ExplicitColumns]
public class DocumentDto
{
    public const string TableName = "dynamicDaveTranslationHealthDocument";

    [Column("key")]
    [PrimaryKeyColumn(AutoIncrement = false, Name = "PK_dynamicDaveTranslationHealthDocument")]
    public Guid Key { get; set; }

    [Column("id")]
    public int Id { get; set; }

    [Column("parentKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? ParentKey { get; set; }

    [Column("path")]
    [Length(450)]
    [Index(IndexTypes.NonClustered, Name = "IX_dynamicDaveTranslationHealthDocument_path")]
    public string Path { get; set; } = string.Empty;

    [Column("sortPath")]
    [Length(450)]
    [Index(IndexTypes.NonClustered, Name = "IX_dynamicDaveTranslationHealthDocument_siteRootKey_sortPath", ForColumns = "siteRootKey,sortPath")]
    public string SortPath { get; set; } = string.Empty;

    [Column("level")]
    public int Level { get; set; }

    [Column("contentTypeAlias")]
    [Length(255)]
    [Index(IndexTypes.NonClustered, Name = "IX_dynamicDaveTranslationHealthDocument_contentTypeAlias")]
    public string ContentTypeAlias { get; set; } = string.Empty;

    [Column("siteRootKey")]
    public Guid SiteRootKey { get; set; }

    [Column("name")]
    [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
    public string Name { get; set; } = string.Empty;
}
