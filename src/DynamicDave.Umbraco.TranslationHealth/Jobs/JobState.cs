using System.Text.Json.Serialization;

namespace DynamicDave.Umbraco.TranslationHealth.Jobs;

[JsonConverter(typeof(JsonStringEnumConverter<JobKind>))]
public enum JobKind
{
    Rebuild,
}

[JsonConverter(typeof(JsonStringEnumConverter<JobStatus>))]
public enum JobStatus
{
    Running,
    Completed,
    Failed,
    Aborted,
}

/// <summary>Stable phase codes of <see cref="JobState.Phase"/>; the dashboard localizes them.</summary>
public static class JobPhases
{
    public const string Indexing = "indexing";
    public const string RemovingOrphans = "removingOrphans";
}

/// <summary>
/// The one background job of the installation, stored as JSON in umbracoKeyValue under <see cref="Constants.JobStateKey"/>.
/// <see cref="Phase"/> is one of <see cref="JobPhases"/> (empty before the first progress report); <see cref="Summary"/>
/// is JSON with counts for the UI.
/// </summary>
public record JobState(
    Guid Id,
    JobKind Kind,
    JobStatus Status,
    Guid UserKey,
    string UserName,
    DateTime StartedUtc,
    DateTime HeartbeatUtc,
    DateTime? CompletedUtc,
    int Done,
    int Total,
    string Phase,
    string? Error,
    string? Summary);
