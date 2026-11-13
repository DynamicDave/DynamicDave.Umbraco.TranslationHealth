using DynamicDave.Umbraco.TranslationHealth.Jobs;
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
    ISiteSource sites,
    IKeyValueService keyValues,
    IOptionsMonitor<TranslationHealthOptions> options,
    RebuildScheduler scheduler,
    ILogger<StartupCheck> logger)
{
    private volatile bool _done;

    private const int MaxAttempts = 5;
    private int _failures;

    /// <summary>The rebuild the stored state calls for, or null when it is current.</summary>
    /// <param name="lastRebuild">The stored completion time of the last rebuild; empty when none ever completed.</param>
    public static IndexMode? Decide(
        string? lastRebuild, string? storedDefaultCulture, string defaultCulture, string? storedExcludedHash, string excludedHash)
    {
        if (RebuildScheduler.DefaultCultureChanged(storedDefaultCulture, defaultCulture))
        {
            return IndexMode.RebuildClearBaselines;
        }

        // Nothing stored: no rebuild ever completed. An empty store after a rebuild just means a site without variant documents.
        return string.IsNullOrEmpty(lastRebuild) || string.IsNullOrEmpty(storedDefaultCulture) || storedExcludedHash != excludedHash
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

        if (role is not (ServerRole.Single or ServerRole.SchedulingPublisher))
        {
            _done = true;
            return;
        }

        try
        {
            var mode = Decide(
                keyValues.GetValue(Constants.LastRebuildKey),
                keyValues.GetValue(Constants.DefaultCultureKey),
                sites.DefaultCulture(),
                keyValues.GetValue(Constants.ExcludedTypesKey),
                RebuildJob.ExcludedTypesHash(options.CurrentValue.ExcludedDocumentTypes));
            if (mode is { } rebuild)
            {
                logger.LogInformation("Translation health starts a rebuild of the translation status at startup ({Mode})", rebuild);
                scheduler.Request(rebuild);
            }

            _done = true;
        }
        catch (Exception ex)
        {
            // Never keep the site from starting. A transient error is retried by the worker, a few times.
            _done = ++_failures >= MaxAttempts;
            logger.LogError(ex, "Translation health could not check its translation status at startup (attempt {Attempt})", _failures);
        }
    }
}
