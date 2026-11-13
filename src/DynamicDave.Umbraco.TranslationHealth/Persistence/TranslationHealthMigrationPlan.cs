using Umbraco.Cms.Core.Packaging;
using Umbraco.Cms.Infrastructure.Migrations;

namespace DynamicDave.Umbraco.TranslationHealth.Persistence;

// Picked up by Umbraco's type loader like every PackageMigrationPlan; runs unattended at startup.
public class TranslationHealthMigrationPlan : PackageMigrationPlan
{
    public TranslationHealthMigrationPlan()
        : base(Constants.PackageName)
    {
    }

    protected override void DefinePlan() => To<CreateTables>("dynamicdave-translationhealth-1-tables");
}

// AsyncMigrationBase exists in Umbraco 17 and 18; MigrationBase was removed in 18.
public class CreateTables(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!TableExists(DocumentDto.TableName))
        {
            Create.Table<DocumentDto>().Do();
        }

        if (!TableExists(VariantDto.TableName))
        {
            Create.Table<VariantDto>().Do();
        }

        if (!TableExists(ImportDto.TableName))
        {
            Create.Table<ImportDto>().Do();
        }

        if (!TableExists(ImportRowDto.TableName))
        {
            Create.Table<ImportRowDto>().Do();
        }

        if (!TableExists(TempDto.TableName))
        {
            Create.Table<TempDto>().Do();
        }

        return Task.CompletedTask;
    }
}
