using DynamicDave.Umbraco.TranslationHealth.Sites;
using DynamicDave.Umbraco.TranslationHealth.Status;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Services;

namespace DynamicDave.Umbraco.TranslationHealth.Jobs;

/// <summary>
/// Starts rebuilds. The job runner allows one job at a time, so a rebuild that is asked for while one runs is remembered
/// (once, in the strongest mode) and started by <see cref="StartPending"/>, which the indexing worker calls every few seconds.
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
    /// Asks for a rebuild after a change in languages, domains or document types: a normal rebuild, or one that clears
    /// the baselines when the default language is no longer the one the stored status was built for.
    /// </summary>
    public JobState? RequestAfterConfigurationChange()
    {
        var mode = DefaultCultureChanged(keyValues.GetValue(Constants.DefaultCultureKey), sites.DefaultCulture())
            ? IndexMode.RebuildClearBaselines
            : IndexMode.Rebuild;
        return Request(mode);
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
            _pending = _pending is { } waiting && waiting.Mode >= mode ? waiting : new Waiting(mode, userKey, userName);
            var started = StartLocked();
            if (started is null)
            {
                logger.LogInformation("A translation health rebuild ({Mode}) waits for the running job to finish", _pending!.Mode);
            }

            return started;
        }
    }

    /// <summary>Starts the remembered rebuild when no job is running any more (here or on another server).</summary>
    public JobState? StartPending()
    {
        lock (_gate)
        {
            return _pending is null ? null : StartLocked();
        }
    }

    private JobState? StartLocked()
    {
        var request = _pending!;
        var started = runner.TryStart(
            JobKind.Rebuild,
            request.UserKey,
            request.UserName,
            (services, progress, cancellationToken) =>
                services.GetRequiredService<RebuildJob>().RunAsync(request.Mode, progress, cancellationToken));

        if (started is not null)
        {
            _pending = null;
        }

        return started;
    }

    private sealed record Waiting(IndexMode Mode, Guid UserKey, string UserName);
}
