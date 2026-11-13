using DynamicDave.Umbraco.TranslationHealth.Jobs;
using DynamicDave.Umbraco.TranslationHealth.Persistence;
using DynamicDave.Umbraco.TranslationHealth.Sites;
using DynamicDave.Umbraco.TranslationHealth.Status;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Sync;

namespace DynamicDave.Umbraco.TranslationHealth.Indexing;

/// <summary>
/// After startup, starts a rebuild when the stored status cannot be trusted: after installation (nothing stored), when the
/// default language or the list of excluded document types changed while the site was down. Only one server does it.
/// A server only knows its role (single, scheduling publisher or subscriber) a few seconds after it started, so the check
/// is tried again by the indexing worker until it ran.
/// </summary>
public sealed class StartupCheck(
    IRuntimeState runtimeState,
    IServerRoleAccessor serverRoleAccessor,
    ITranslationStatusStore store,
    ISiteSource sites,
    IKeyValueService keyValues,
    IOptionsMonitor<TranslationHealthOptions> options,
    RebuildScheduler scheduler,
    ILogger<StartupCheck> logger)
{
    private volatile bool _done;

    /// <summary>The rebuild the stored state calls for, or null when it is current.</summary>
    public static IndexMode? Decide(
        int storedDocuments, string? storedDefaultCulture, string defaultCulture, string? storedExcludedHash, string excludedHash)
    {
        if (RebuildScheduler.DefaultCultureChanged(storedDefaultCulture, defaultCulture))
        {
            return IndexMode.RebuildClearBaselines;
        }

        // No stored default culture: no rebuild ever completed.
        return storedDocuments == 0 || string.IsNullOrEmpty(storedDefaultCulture) || storedExcludedHash != excludedHash
            ? IndexMode.Rebuild
            : null;
    }

    /// <summary>Does the check once, as soon as the runtime runs and the role of this server is known; later calls do nothing.</summary>
    public void Run()
    {
        if (_done || runtimeState.Level != RuntimeLevel.Run)
        {
            return;
        }

        // The same server as the scheduled jobs of Umbraco: one rebuild per installation, not one per server.
        var role = serverRoleAccessor.CurrentServerRole;
        if (role == ServerRole.Unknown)
        {
            return;
        }

        _done = true;
        if (role is not (ServerRole.Single or ServerRole.SchedulingPublisher))
        {
            return;
        }

        try
        {
            var mode = Decide(
                store.Count(),
                keyValues.GetValue(Constants.DefaultCultureKey),
                sites.DefaultCulture(),
                keyValues.GetValue(Constants.ExcludedTypesKey),
                RebuildJob.ExcludedTypesHash(options.CurrentValue.ExcludedDocumentTypes));
            if (mode is { } rebuild)
            {
                logger.LogInformation("Translation health starts a rebuild of the translation status at startup ({Mode})", rebuild);
                scheduler.Request(rebuild);
            }
        }
        catch (Exception ex)
        {
            // Never keep the site from starting.
            logger.LogError(ex, "Translation health could not check its translation status at startup");
        }
    }
}

/// <summary>Tries the <see cref="StartupCheck"/> when the application has started; the worker repeats it until it ran.</summary>
public sealed class StartupCheckHandler(StartupCheck check) : INotificationHandler<UmbracoApplicationStartedNotification>
{
    public void Handle(UmbracoApplicationStartedNotification notification) => check.Run();
}
