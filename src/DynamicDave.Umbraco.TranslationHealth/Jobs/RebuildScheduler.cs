using DynamicDave.Umbraco.TranslationHealth.Sites;
using DynamicDave.Umbraco.TranslationHealth.Status;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Services;

namespace DynamicDave.Umbraco.TranslationHealth.Jobs;

/// <summary>
/// Starts rebuilds. The job runner allows one job at a time, so a rebuild that is asked for while one runs is remembered
/// (once, in the strongest mode) and started by <see cref="StartPending"/>, which the indexing worker calls when editing paused.
/// </summary>
public sealed class RebuildScheduler(
    JobRunner runner,
    ISiteSource sites,
    IKeyValueService keyValues,
    ILogger<RebuildScheduler> logger)
{
    public const string SystemUserName = "System";

    private readonly object _gate = new();
    private Waiting? _pending;
    private bool _configurationChanged;

    /// <summary>The rebuild waiting for the running job to finish, if any.</summary>
    internal IndexMode? PendingMode
    {
        get
        {
            lock (_gate) return _pending?.Mode;
        }
    }

    /// <summary>
    /// Whether the default language differs from the one the stored status was built for. Nothing stored (a first run)
    /// is not a change: there are no baselines to clear.
    /// </summary>
    public static bool DefaultCultureChanged(string? storedDefaultCulture, string currentDefaultCulture)
        => !string.IsNullOrEmpty(storedDefaultCulture)
           && !string.Equals(storedDefaultCulture, currentDefaultCulture, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Records that languages, domains or document types changed. Only a flag in memory (called on the request thread of
    /// the editor); the worker then decides the mode and starts one rebuild for a whole burst of changes.
    /// </summary>
    public void NoteConfigurationChanged()
    {
        lock (_gate) _configurationChanged = true;
    }

    /// <summary>A rebuild is waiting to be started (asked for while a job ran, or after a configuration change).</summary>
    public bool HasPending
    {
        get
        {
            lock (_gate) return _pending is not null || _configurationChanged;
        }
    }

    /// <summary>
    /// Starts a rebuild, or remembers it when a job is running. Returns the started job, or null when it is waiting.
    /// </summary>
    public JobState? Request(IndexMode mode, Guid userKey = default, string userName = SystemUserName)
    {
        if (mode == IndexMode.Save)
        {
            throw new ArgumentException("A rebuild runs in a rebuild mode.", nameof(mode));
        }

        lock (_gate)
        {
            Merge(mode, userKey, userName);
            var started = StartLocked();
            if (started is null)
            {
                logger.LogInformation("A translation health rebuild ({Mode}) waits for the running job to finish", _pending!.Mode);
            }

            return started;
        }
    }

    /// <summary>
    /// Starts the remembered rebuild when no job is running any more (here or on another server). A configuration change
    /// is turned into a rebuild at the moment it starts, with the default language compared then: a changed default
    /// language clears the baselines, anything else keeps them. While a job runs the change stays unresolved, because that
    /// job may be the one that handles the new default language and stores it.
    /// </summary>
    public JobState? StartPending()
    {
        lock (_gate)
        {
            return _pending is null && !_configurationChanged ? null : StartLocked();
        }
    }

    // The stronger mode wins: a rebuild that clears baselines includes what a normal one does.
    private static int Rank(IndexMode mode) => mode switch
    {
        IndexMode.RebuildClearBaselines => 2,
        IndexMode.Rebuild => 1,
        _ => 0,
    };

    private void Merge(IndexMode mode, Guid userKey, string userName)
        => _pending = _pending is { } waiting && Rank(waiting.Mode) >= Rank(mode) ? waiting : new Waiting(mode, userKey, userName);

    private JobState? StartLocked()
    {
        var request = _pending;
        if (_configurationChanged)
        {
            var changed = DefaultCultureChanged(keyValues.GetValue(Constants.DefaultCultureKey), sites.DefaultCulture());
            var mode = changed ? IndexMode.RebuildClearBaselines : IndexMode.Rebuild;
            request = request is not null && Rank(request.Mode) >= Rank(mode) ? request : new Waiting(mode, default, SystemUserName);
        }

        if (request is null)
        {
            return null;
        }

        var started = runner.TryStart(
            JobKind.Rebuild,
            request.UserKey,
            request.UserName,
            (services, progress, cancellationToken) =>
                services.GetRequiredService<RebuildJob>().RunAsync(request.Mode, progress, cancellationToken));

        // Not started: everything stays as it was (the configuration flag too), for the next try.
        if (started is not null)
        {
            _pending = null;
            _configurationChanged = false;
        }

        return started;
    }

    private sealed record Waiting(IndexMode Mode, Guid UserKey, string UserName);
}
