using DynamicDave.Umbraco.TranslationHealth.Content;
using DynamicDave.Umbraco.TranslationHealth.Jobs;
using DynamicDave.Umbraco.TranslationHealth.Sites;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace DynamicDave.Umbraco.TranslationHealth.Indexing;

/// <summary>
/// Turns Umbraco notifications into work for the <see cref="ReindexWorker"/>: they only queue keys (or start a rebuild),
/// so the editor's save never waits for the status and never fails because of it. The "ed" notifications used here are
/// published after the database transaction committed.
/// </summary>
public sealed class ContentChangeHandlers(
    ReindexQueue queue,
    IContentSnapshotSource snapshots,
    ISiteSource sites,
    RebuildScheduler scheduler,
    IRuntimeState runtimeState,
    ILogger<ContentChangeHandlers> logger,
    EditorActivity activity) :
    INotificationHandler<ContentSavingNotification>,
    INotificationHandler<ContentPublishingNotification>,
    INotificationHandler<ContentUnpublishingNotification>,
    INotificationHandler<ContentMovingNotification>,
    INotificationHandler<ContentMovingToRecycleBinNotification>,
    INotificationHandler<ContentDeletingNotification>,
    INotificationHandler<ContentSortingNotification>,
    INotificationHandler<ContentCopyingNotification>,
    INotificationHandler<ContentRollingBackNotification>,
    INotificationHandler<ContentEmptyingRecycleBinNotification>,
    INotificationHandler<DictionaryItemSavingNotification>,
    INotificationHandler<DictionaryItemDeletingNotification>,
    INotificationHandler<MediaSavingNotification>,
    INotificationHandler<MediaMovingNotification>,
    INotificationHandler<MediaDeletingNotification>,
    INotificationHandler<ContentSavedNotification>,
    INotificationHandler<ContentPublishedNotification>,
    INotificationHandler<ContentUnpublishedNotification>,
    INotificationHandler<ContentMovedNotification>,
    INotificationHandler<ContentMovedToRecycleBinNotification>,
    INotificationHandler<ContentSortedNotification>,
    INotificationHandler<ContentCopiedNotification>,
    INotificationHandler<ContentDeletedNotification>,
    INotificationHandler<DomainSavedNotification>,
    INotificationHandler<DomainDeletedNotification>,
    INotificationHandler<LanguageSavedNotification>,
    INotificationHandler<LanguageDeletedNotification>,
    INotificationHandler<ContentTypeChangedNotification>
{
    // The "ing" notifications (and the rollback / empty-bin ones) start before the save's transaction and only mark the
    // time of the change (memory, no database), so the rebuild's write window stays clear of the whole save, including Umbraco's own handlers after it.
    public void Handle(ContentSavingNotification notification) => activity.Mark();

    public void Handle(ContentPublishingNotification notification) => activity.Mark();

    public void Handle(ContentUnpublishingNotification notification) => activity.Mark();

    public void Handle(ContentMovingNotification notification) => activity.Mark();

    public void Handle(ContentMovingToRecycleBinNotification notification) => activity.Mark();

    public void Handle(ContentDeletingNotification notification) => activity.Mark();

    public void Handle(ContentSortingNotification notification) => activity.Mark();

    public void Handle(ContentCopyingNotification notification) => activity.Mark();

    public void Handle(ContentRollingBackNotification notification) => activity.Mark();

    public void Handle(ContentEmptyingRecycleBinNotification notification) => activity.Mark();

    // Dictionary and media saves write to the same database; the status does not follow them, only the rebuild yields.
    public void Handle(DictionaryItemSavingNotification notification) => activity.Mark();

    public void Handle(DictionaryItemDeletingNotification notification) => activity.Mark();

    public void Handle(MediaSavingNotification notification) => activity.Mark();

    public void Handle(MediaMovingNotification notification) => activity.Mark();

    public void Handle(MediaDeletingNotification notification) => activity.Mark();

    public void Handle(ContentSavedNotification notification)
        => Safe(() => queue.Enqueue(notification.SavedEntities.Select(content => content.Key)), notification);

    public void Handle(ContentPublishedNotification notification)
        => Safe(() => queue.Enqueue(notification.PublishedEntities.Select(content => content.Key)), notification);

    public void Handle(ContentUnpublishedNotification notification)
        => Safe(() => queue.Enqueue(notification.UnpublishedEntities.Select(content => content.Key)), notification);

    // The documents below a moved document change path, level, site and sort path too. A restore from the recycle bin
    // is a move as well; a document that is (still) in the bin is removed by the re-index.
    public void Handle(ContentMovedNotification notification)
        => Safe(() => queue.Enqueue(WithDescendants(notification.MoveInfoCollection.Select(info => info.Entity.Key))), notification);

    public void Handle(ContentMovedToRecycleBinNotification notification)
        => Safe(() => queue.EnqueueRemoval(WithDescendants(notification.MoveInfoCollection.Select(info => info.Entity.Key))), notification);

    public void Handle(ContentSortedNotification notification)
        => Safe(() => queue.Enqueue(WithDescendants(notification.SortedEntities.Select(content => content.Key))), notification);

    public void Handle(ContentCopiedNotification notification)
        => Safe(() => queue.Enqueue(WithDescendants([notification.Copy.Key])), notification);

    public void Handle(ContentDeletedNotification notification)
        => Safe(() => queue.EnqueueRemoval(notification.DeletedEntities.Select(content => content.Key)), notification);

    public void Handle(DomainSavedNotification notification) => SitesChanged(notification);

    public void Handle(DomainDeletedNotification notification) => SitesChanged(notification);

    public void Handle(LanguageSavedNotification notification) => SitesChanged(notification);

    public void Handle(LanguageDeletedNotification notification) => SitesChanged(notification);

    public void Handle(ContentTypeChangedNotification notification)
        => SitesChanged(notification, clearSnapshotCache: true);

    // Domains and languages change which languages a site uses, document types which documents count at all. Only memory
    // is touched here: the worker starts the rebuild, so a burst of changes gives one.
    private void SitesChanged(INotification notification, bool clearSnapshotCache = false)
        => Safe(
            () =>
            {
                sites.ClearCache();
                if (clearSnapshotCache)
                {
                    // Otherwise a type that just became culture-variant is missed for up to a minute.
                    snapshots.ClearCache();
                }

                // Installing and upgrading raise these too; the startup check covers that.
                if (runtimeState.Level == RuntimeLevel.Run)
                {
                    scheduler.NoteConfigurationChanged();
                }
            },
            notification);

    private List<Guid> WithDescendants(IEnumerable<Guid> keys)
    {
        var all = new List<Guid>();
        foreach (var key in keys)
        {
            all.Add(key);
            all.AddRange(snapshots.DescendantKeys(key));
        }

        return all;
    }

    private void Safe(Action action, INotification notification)
    {
        try
        {
            activity.Mark();
            action();
        }
        catch (Exception ex)
        {
            // Never fail the editor's action because the status could not be queued; the overview offers a rebuild.
            logger.LogError(ex, "Translation health could not handle {Notification}", notification.GetType().Name);
        }
    }
}
