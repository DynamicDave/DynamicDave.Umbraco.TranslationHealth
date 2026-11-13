using System.Text.Json;
using DynamicDave.Umbraco.TranslationHealth.Files;
using DynamicDave.Umbraco.TranslationHealth.Importing;
using DynamicDave.Umbraco.TranslationHealth.Jobs;
using DynamicDave.Umbraco.TranslationHealth.Models;
using DynamicDave.Umbraco.TranslationHealth.Overview;
using DynamicDave.Umbraco.TranslationHealth.Persistence;
using DynamicDave.Umbraco.TranslationHealth.Status;
using Microsoft.AspNetCore.Mvc;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

// The package's controllers do not use Umbraco's backoffice JSON options, so the API models serialize with MVC's
// default options. The generated TypeScript client expects enums as strings, so each enum carries its own converter.
public class ApiJsonTests
{
    private static readonly JsonSerializerOptions Mvc = new JsonOptions().JsonSerializerOptions;

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, Mvc);

    [Fact]
    public void Enums_serialize_as_strings()
    {
        Assert.Equal("\"PendingChanges\"", Json(CellStatus.PendingChanges));
        Assert.Equal("\"Fingerprint\"", Json(TrackingMode.Fingerprint));
        Assert.Equal("\"MarkedUpToDate\"", Json(BaselineSource.MarkedUpToDate));
        Assert.Equal("\"AnyIssue\"", Json(CellFilter.AnyIssue));
        Assert.Equal("\"SkippedHasValue\"", Json(ImportOutcome.SkippedHasValue));
        Assert.Equal("\"LanguageNotAllowed\"", Json(ImportErrorReason.LanguageNotAllowed));
        Assert.Equal("\"Changed\"", Json(ApplyOutcome.Changed));
        Assert.Equal("\"TooManyRows\"", Json(ImportFileError.TooManyRows));
        Assert.Equal("\"Xliff\"", Json(ImportFileFormat.Xliff));
        Assert.Equal("\"Completed\"", Json(JobStatus.Completed));
        Assert.Equal("\"Undone\"", Json(ImportState.Undone));
    }

    [Fact]
    public void Models_carry_their_enums_as_strings()
    {
        var cell = new VariantCellModel { Culture = "nl-NL", Status = CellStatus.Draft, Outdated = true, Mode = TrackingMode.Fingerprint };
        Assert.Contains("\"status\":\"Draft\"", Json(cell));
        Assert.Contains("\"mode\":\"Fingerprint\"", Json(cell));

        var item = new PlanItemModel
        {
            Id = 1, RowNumber = 2, Key = "A", Culture = "nl-NL", Outcome = ImportOutcome.NewKey, Reason = ImportErrorReason.None,
            NewValue = "x", SourceChanged = false, SelectedByDefault = true,
        };
        Assert.Contains("\"outcome\":\"NewKey\"", Json(item));
        Assert.Contains("\"reason\":\"None\"", Json(item));

        var job = new JobStateModel
        {
            Id = Guid.NewGuid(), Kind = JobKind.Rebuild, Status = JobStatus.Running, UserKey = Guid.NewGuid(), UserName = "Dave",
            StartedUtc = DateTime.UtcNow, HeartbeatUtc = DateTime.UtcNow, Done = 1, Total = 2, Phase = JobPhases.Indexing, CancelRequested = false,
        };
        Assert.Contains("\"kind\":\"Rebuild\"", Json(job));
        Assert.Contains("\"status\":\"Running\"", Json(job));
    }
}
