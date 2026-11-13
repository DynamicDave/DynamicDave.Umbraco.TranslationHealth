using Asp.Versioning;
using DynamicDave.Umbraco.TranslationHealth.Dictionary;
using DynamicDave.Umbraco.TranslationHealth.Files;
using DynamicDave.Umbraco.TranslationHealth.Importing;
using DynamicDave.Umbraco.TranslationHealth.Models;
using DynamicDave.Umbraco.TranslationHealth.Persistence;
using DynamicDave.Umbraco.TranslationHealth.Security;
using DynamicDave.Umbraco.TranslationHealth.Sites;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Authorization;

namespace DynamicDave.Umbraco.TranslationHealth.Controllers;

/// <summary>
/// The dictionary tools (spec sections 5 and 6): the missing report, export, import with a test run, and the history with undo.
/// Same API and the same rules as <see cref="TranslationHealthController"/>; split off to keep each controller small.
/// </summary>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "TranslationDictionary")]
public class DictionaryTranslationsController(
    IDictionarySource dictionary,
    ISiteSource sites,
    ILanguageService languageService,
    TempStore tempStore,
    IImportHistory history,
    ImportApplier applier,
    ImportUndoer undoer,
    IBackOfficeSecurityAccessor security,
    TimeProvider timeProvider,
    ILogger<DictionaryTranslationsController> logger) : DynamicDaveUmbracoTranslationHealthApiControllerBase
{
    internal const long MaxUploadBytes = 10_000_000;
    private const long MaxRequestBytes = 10_500_000;
    private const int MaxTextLength = 255;

    [HttpGet("dictionary/missing")]
    [Authorize(Policy = AuthorizationPolicies.TreeAccessDictionary)]
    [ProducesResponseType<MissingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetMissingTranslations(
        [FromQuery] string? culture = null,
        [FromQuery] string? group = null,
        [FromQuery] string? search = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = Paging.FirstPageSize)
    {
        if (!Paging.IsValid(skip, take)) return Error(ApiErrors.InvalidPaging);
        if (group is { Length: > MaxTextLength } || search is { Length: > MaxTextLength }) return Error(ApiErrors.InvalidFilter);

        var languages = (await languageService.GetAllAsync()).ToList();
        var installed = Installed(languages, culture);
        if (!string.IsNullOrWhiteSpace(culture) && installed is null) return Error(ApiErrors.InvalidCulture);

        var cultures = ApiMapping.CultureOrder(languages.Select(l => l.IsoCode), sites.DefaultCulture());
        var (counts, items) = MissingReport.Build(dictionary.GetAll(), cultures, installed, group, search);
        return Ok(ApiMapping.ToMissingResponse(counts, items, skip, take));
    }

    [HttpGet("dictionary/groups")]
    [Authorize(Policy = AuthorizationPolicies.TreeAccessDictionary)]
    [ProducesResponseType<string[]>(StatusCodes.Status200OK)]
    public IActionResult GetDictionaryGroups() => Ok(ApiMapping.Groups(dictionary.GetAll()).ToArray());

    /// <summary>
    /// The dictionary as CSV (one column per language, default first) or as XLIFF 1.2 for one target language (<paramref name="culture"/>,
    /// required; the default language is the source). <paramref name="missingIn"/> and <paramref name="group"/> limit the items.
    /// </summary>
    [HttpGet("dictionary/export")]
    [Authorize(Policy = AuthorizationPolicies.TreeAccessDictionary)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK, "text/csv", "application/xliff+xml")]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportDictionary(
        [FromQuery] ImportFileFormat format = ImportFileFormat.Csv,
        [FromQuery] string? culture = null,
        [FromQuery] string? missingIn = null,
        [FromQuery] string? group = null)
    {
        if (!Enum.IsDefined(format)) return Error(ApiErrors.InvalidFormat);
        if (group is { Length: > MaxTextLength }) return Error(ApiErrors.InvalidFilter);

        var languages = (await languageService.GetAllAsync()).ToList();
        var defaultCulture = sites.DefaultCulture();
        var target = Installed(languages, culture);
        var missing = Installed(languages, missingIn);
        if (!string.IsNullOrWhiteSpace(missingIn) && missing is null) return Error(ApiErrors.InvalidCulture);

        // An XLIFF file translates the default language into one other language.
        if (format == ImportFileFormat.Xliff && (target is null || string.Equals(target, defaultCulture, StringComparison.OrdinalIgnoreCase)))
        {
            return Error(ApiErrors.InvalidCulture);
        }

        var entries = ApiMapping.FilterForExport(dictionary.GetAll(), missing, group);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (format == ImportFileFormat.Xliff)
        {
            return File(DictionaryXliff.Write(entries, defaultCulture, target!), "application/xliff+xml", ApiMapping.DictionaryFileName(format, target, now));
        }

        var cultures = ApiMapping.CultureOrder(languages.Select(l => l.IsoCode), defaultCulture);
        return File(DictionaryCsv.Write(entries, cultures), "text/csv; charset=utf-8", ApiMapping.DictionaryFileName(format, null, now));
    }

    /// <summary>
    /// Reads an uploaded CSV or XLIFF file and plans the import without writing anything. The plan is kept for an hour and
    /// can be applied once, by the user who made it.
    /// </summary>
    [HttpPost("dictionary/import/plan")]
    [Authorize(Policy = AuthorizationPolicies.TreeAccessDictionary)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [ProducesResponseType<ImportPlanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateImportPlan(IFormFile? file, [FromQuery] bool overwrite = false, [FromQuery] bool createKeys = false)
    {
        if (CurrentUser() is not { } user) return Unauthorized();
        if (file is null || file.Length == 0) return Error(nameof(ImportFileError.Unreadable));
        if (file.Length > MaxUploadBytes) return Error(nameof(ImportFileError.TooLarge));

        byte[]? bytes;
        await using (var upload = file.OpenReadStream())
        {
            bytes = ImportStreams.ReadBounded(upload, MaxUploadBytes);
        }

        if (bytes is null) return Error(nameof(ImportFileError.TooLarge));
        if (ApiMapping.DetectFormat(file.FileName, bytes) is not { } format) return Error(nameof(ImportFileError.Unreadable));

        var languages = (await languageService.GetAllAsync()).ToList();
        var installed = languages.Select(l => l.IsoCode).ToList();
        using var stream = new MemoryStream(bytes);
        var read = format == ImportFileFormat.Xliff
            ? DictionaryXliff.Read(stream, installed, MaxUploadBytes)
            : DictionaryCsv.Read(stream, installed, MaxUploadBytes);
        if (read.Error is { } fileError) return Error(fileError.ToString());

        var plan = ImportPlanner.Plan(
            Guid.NewGuid(),
            ApiMapping.UploadFileName(file.FileName),
            read,
            dictionary.GetAll(),
            sites.DefaultCulture(),
            AccessPolicy.EditableCultures(user.Groups, languages),
            installed.ToHashSet(StringComparer.OrdinalIgnoreCase),
            new ImportOptions(overwrite, createKeys));
        tempStore.Put(plan.Id, TempKind.Plan, ApiMapping.SerializePlan(user.Key, plan), plan.FileName);
        return Ok(ApiMapping.ToPlanResponse(plan));
    }

    [HttpGet("dictionary/import/{planId:guid}/items")]
    [Authorize(Policy = AuthorizationPolicies.TreeAccessDictionary)]
    [ProducesResponseType<PlanItemsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public IActionResult GetImportPlanItems(Guid planId, [FromQuery] ImportOutcome[]? outcome = null, [FromQuery] int skip = 0, [FromQuery] int take = Paging.FirstPageSize)
    {
        if (CurrentUser() is not { } user) return Unauthorized();
        if (!Paging.IsValid(skip, take)) return Error(ApiErrors.InvalidPaging);

        // Model binding accepts any number for an enum.
        if (outcome is not null && !outcome.All(o => Enum.IsDefined(o))) return Error(ApiErrors.InvalidFilter);
        if (LoadPlan(planId, user) is not { } plan) return NotFoundError(ApiErrors.PlanExpired);

        return Ok(ApiMapping.ToPlanItems(plan, outcome, skip, take));
    }

    /// <summary>
    /// Writes the selected items of a plan. The plan is claimed first, so it applies once, also for two requests at the same
    /// time; selected items in a language the user may no longer edit are dropped (they count as skipped).
    /// </summary>
    [HttpPost("dictionary/import/{planId:guid}/apply")]
    [Authorize(Policy = AuthorizationPolicies.TreeAccessDictionary)]
    [ProducesResponseType<ApplyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ApplyImportPlan(Guid planId, [FromBody] ApplyRequest request)
    {
        if (CurrentUser() is not { } user) return Unauthorized();
        if (LoadPlan(planId, user) is not { } plan) return NotFoundError(ApiErrors.PlanExpired);

        var editable = AccessPolicy.EditableCultures(user.Groups, await languageService.GetAllAsync());
        // Nothing selected: refuse before the plan is claimed, so it can still be used.
        if (request.SelectedItemIds is not { Length: > 0 }) return Error(ApiErrors.InvalidSelection);

        var (valid, selected) = ApiMapping.Select(plan, request.SelectedItemIds, editable);
        if (!valid) return Error(ApiErrors.InvalidSelection);

        // Whoever deletes the plan first applies it.
        if (!tempStore.Delete(planId, TempKind.Plan)) return NotFoundError(ApiErrors.PlanExpired);

        try
        {
            return Ok(ApiMapping.ToApplyResponse(await applier.ApplyAsync(plan, selected, user.Key, UserName(user))));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Applying import plan {PlanId} failed", planId);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse { Error = ApiErrors.ApplyFailed });
        }
    }

    [HttpGet("dictionary/imports")]
    [Authorize(Policy = AuthorizationPolicies.TreeAccessDictionary)]
    [ProducesResponseType<ImportsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public IActionResult GetImports([FromQuery] int skip = 0, [FromQuery] int take = Paging.FirstPageSize)
    {
        if (!Paging.IsValid(skip, take)) return Error(ApiErrors.InvalidPaging);

        var (items, total) = history.List(skip, take);
        return Ok(new ImportsResponse { Total = total, Items = items.Select(ApiMapping.ToModel).ToArray() });
    }

    /// <summary>One import with a page of the values it wrote.</summary>
    [HttpGet("dictionary/imports/{key:guid}")]
    [Authorize(Policy = AuthorizationPolicies.TreeAccessDictionary)]
    [ProducesResponseType<ImportDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public IActionResult GetImport(Guid key, [FromQuery] int skip = 0, [FromQuery] int take = Paging.FirstPageSize)
    {
        if (!Paging.IsValid(skip, take)) return Error(ApiErrors.InvalidPaging);
        if (history.Get(key) is not { } import) return NotFoundError(ApiErrors.ImportNotFound);

        var rows = history.Rows(key);
        return Ok(new ImportDetailResponse
        {
            Import = ApiMapping.ToModel(import),
            TotalRows = rows.Count,
            Rows = rows.Skip(skip).Take(take).Select(ApiMapping.ToModel).ToArray(),
        });
    }

    /// <summary>
    /// Undoes an import once. Needs edit permission for every language the import wrote (403 otherwise); 409 when it was
    /// undone already. Values and keys that were changed after the import are left alone and listed.
    /// </summary>
    [HttpPost("dictionary/imports/{key:guid}/undo")]
    [Authorize(Policy = AuthorizationPolicies.TreeAccessDictionary)]
    [ProducesResponseType<UndoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UndoImport(Guid key)
    {
        if (CurrentUser() is not { } user) return Unauthorized();
        if (history.Get(key) is not { } import) return NotFoundError(ApiErrors.ImportNotFound);
        if (import.State == ImportState.Undone) return ConflictError(ApiErrors.AlreadyUndone);

        // A language that is no longer installed has no values to restore, so it does not block the undo.
        var languages = (await languageService.GetAllAsync()).ToList();
        var cultures = history.Rows(key).Select(r => r.Culture).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var culture in cultures)
        {
            var language = languages.FirstOrDefault(l => string.Equals(l.IsoCode, culture, StringComparison.OrdinalIgnoreCase));
            if (language is not null && !AccessPolicy.CanEditLanguage(user.Groups, language.Id)) return Forbidden();
        }

        try
        {
            return Ok(ApiMapping.ToModel(await undoer.UndoAsync(key, user.Key, UserName(user))));
        }
        catch (ImportAlreadyUndoneException)
        {
            // Another request undid it between the check above and the claim.
            return ConflictError(ApiErrors.AlreadyUndone);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Undoing import {ImportKey} failed", key);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse { Error = ApiErrors.UndoFailed });
        }
    }

    // The plan, when it exists, has not expired and was made by this user (someone else's plan is as good as not there).
    private ImportPlan? LoadPlan(Guid planId, IUser user)
        => tempStore.Get(planId, TempKind.Plan) is { } entry && ApiMapping.ReadPlan(entry.Content) is { } stored && stored.UserKey == user.Key
            ? stored.Plan
            : null;

    // The installed ISO code spelled as Umbraco has it, or null.
    private static string? Installed(IEnumerable<ILanguage> languages, string? culture)
        => string.IsNullOrWhiteSpace(culture)
            ? null
            : languages.FirstOrDefault(l => string.Equals(l.IsoCode, culture, StringComparison.OrdinalIgnoreCase))?.IsoCode;

    private IUser? CurrentUser() => security.BackOfficeSecurity?.CurrentUser;

    private static string UserName(IUser user) => user.Name ?? user.Username;
}
