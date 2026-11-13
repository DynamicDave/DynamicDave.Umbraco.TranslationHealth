using DynamicDave.Umbraco.TranslationHealth.Content;
using DynamicDave.Umbraco.TranslationHealth.Dictionary;
using DynamicDave.Umbraco.TranslationHealth.Documents;
using DynamicDave.Umbraco.TranslationHealth.Importing;
using DynamicDave.Umbraco.TranslationHealth.Indexing;
using DynamicDave.Umbraco.TranslationHealth.Jobs;
using DynamicDave.Umbraco.TranslationHealth.Overview;
using DynamicDave.Umbraco.TranslationHealth.Persistence;
using DynamicDave.Umbraco.TranslationHealth.Sites;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Extensions;

namespace DynamicDave.Umbraco.TranslationHealth.Composers;

public class TranslationHealthComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddOptions<TranslationHealthOptions>().Bind(builder.Config.GetSection(Constants.OptionsSection));
        builder.Services.TryAddSingleton(TimeProvider.System);

        // The tables are created by TranslationHealthMigrationPlan, which Umbraco discovers by itself.
        builder.Services.AddSingleton<ITranslationStatusStore, TranslationStatusStore>();
        builder.Services.AddSingleton<IImportHistory, ImportHistory>();
        builder.Services.AddSingleton<TempStore>();
        builder.Services.AddSingleton<ImportApplier>();
        builder.Services.AddSingleton<ImportUndoer>();

        builder.Services.AddSingleton<OverviewQuery>();
        builder.Services.AddTransient<BreadcrumbResolver>();
        builder.Services.AddSingleton<DocumentTranslations>();

        builder.Services.AddSingleton<ISiteSource, UmbracoSiteSource>();
        builder.Services.AddSingleton<IDictionarySource, UmbracoDictionarySource>();
        builder.Services.AddSingleton<IContentSnapshotSource, UmbracoContentSnapshotSource>();

        // One writer of the status rows per process (see Reindexer); the worker empties the queue the handlers fill.
        builder.Services.AddSingleton<ReindexQueue>();
        builder.Services.AddSingleton<Reindexer>();
        builder.Services.AddSingleton<ReindexProcessor>();
        builder.Services.AddSingleton<QuietGate>();
        builder.Services.AddSingleton<EditorActivity>();
        builder.Services.AddSingleton<StartupCheck>();
        builder.Services.AddHostedService<ReindexWorker>();

        // One job at a time per installation; each job runs in its own DI scope (see JobRunner).
        builder.Services.AddSingleton<JobRunner>();
        builder.Services.AddSingleton<RebuildScheduler>();
        builder.Services.AddTransient<RebuildJob>();

        builder
            .AddNotificationHandler<ContentSavingNotification, ContentChangeHandlers>()
            .AddNotificationHandler<ContentPublishingNotification, ContentChangeHandlers>()
            .AddNotificationHandler<ContentUnpublishingNotification, ContentChangeHandlers>()
            .AddNotificationHandler<ContentMovingNotification, ContentChangeHandlers>()
            .AddNotificationHandler<ContentMovingToRecycleBinNotification, ContentChangeHandlers>()
            .AddNotificationHandler<ContentDeletingNotification, ContentChangeHandlers>()
            .AddNotificationHandler<ContentSortingNotification, ContentChangeHandlers>()
            .AddNotificationHandler<ContentCopyingNotification, ContentChangeHandlers>()
            .AddNotificationHandler<ContentSavedNotification, ContentChangeHandlers>()
            .AddNotificationHandler<ContentPublishedNotification, ContentChangeHandlers>()
            .AddNotificationHandler<ContentUnpublishedNotification, ContentChangeHandlers>()
            .AddNotificationHandler<ContentMovedNotification, ContentChangeHandlers>()
            .AddNotificationHandler<ContentMovedToRecycleBinNotification, ContentChangeHandlers>()
            .AddNotificationHandler<ContentSortedNotification, ContentChangeHandlers>()
            .AddNotificationHandler<ContentCopiedNotification, ContentChangeHandlers>()
            .AddNotificationHandler<ContentDeletedNotification, ContentChangeHandlers>()
            .AddNotificationHandler<DomainSavedNotification, ContentChangeHandlers>()
            .AddNotificationHandler<DomainDeletedNotification, ContentChangeHandlers>()
            .AddNotificationHandler<LanguageSavedNotification, ContentChangeHandlers>()
            .AddNotificationHandler<LanguageDeletedNotification, ContentChangeHandlers>()
            .AddNotificationHandler<ContentTypeChangedNotification, ContentChangeHandlers>()
            .AddNotificationHandler<UmbracoApplicationStartedNotification, StartupCheckHandler>();
    }
}
