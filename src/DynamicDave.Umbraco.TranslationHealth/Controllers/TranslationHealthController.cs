using Asp.Versioning;
using DynamicDave.Umbraco.TranslationHealth.Documents;
using DynamicDave.Umbraco.TranslationHealth.Indexing;
using DynamicDave.Umbraco.TranslationHealth.Jobs;
using DynamicDave.Umbraco.TranslationHealth.Models;
using DynamicDave.Umbraco.TranslationHealth.Overview;
using DynamicDave.Umbraco.TranslationHealth.Security;
using DynamicDave.Umbraco.TranslationHealth.Sites;
using DynamicDave.Umbraco.TranslationHealth.Status;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Actions;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Hosting;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Security.Authorization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Authorization;
using Umbraco.Extensions;

namespace DynamicDave.Umbraco.TranslationHealth.Controllers;

/// <summary>
/// The overview, the document tab and the rebuild job (spec section 6). Thin: it checks permissions (403), validates input
/// (400), calls the package's units and maps the result. The overview only shows what the user's content start nodes allow.
/// </summary>
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = "TranslationHealth")]
public class TranslationHealthController(
    OverviewQuery overview,
    BreadcrumbResolver breadcrumbs,
    DocumentTranslations documents,
    Reindexer reindexer,
    RebuildScheduler scheduler,
    JobRunner jobs,
    ISiteSource sites,
    ILanguageService languageService,
    IEntityService entityService,
    IContentTypeService contentTypeService,
    IKeyValueService keyValues,
    IBackOfficeSecurityAccessor security,
    IAuthorizationService authorizationService,
    AppCaches appCaches,
    IOptionsMonitor<TranslationHealthOptions> options,
    IHostingEnvironment hostingEnvironment,
    TimeProvider timeProvider) : DynamicDaveUmbracoTranslationHealthApiControllerBase
{
    private const string CsvContentType = "text/csv; charset=utf-8";
    private const int MaxSearchLength = 200;

    [HttpGet("languages")]
    [Authorize(Policy = AuthorizationPolicies.TreeAccessDictionary)]
    [ProducesResponseType<LanguageModel[]>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLanguages()
    {
        var languages = (await languageService.GetAllAsync()).ToList();
        var defaultCulture = sites.DefaultCulture();
        var order = ApiMapping.CultureOrder(languages.Select(l => l.IsoCode), defaultCulture);
        return Ok(order
            .Select(culture => languages.First(l => l.IsoCode == culture))
            .Select(l => new LanguageModel
            {
                Culture = l.IsoCode,
                Name = l.CultureName,
                IsDefault = string.Equals(l.IsoCode, defaultCulture, StringComparison.OrdinalIgnoreCase),
            })
            .ToArray());
    }

    /// <summary>The sites (root documents) the user can see anything of, with the languages they use.</summary>
    [HttpGet("sites")]
    [Authorize(Policy = AuthorizationPolicies.TreeAccessDictionary)]
    [ProducesResponseType<SiteModel[]>(StatusCodes.Status200OK)]
    public IActionResult GetSites()
    {
        if (CurrentUser() is not { } user) return Unauthorized();

        var startNodes = StartNodes(user);
        var all = sites.GetSites();
        IReadOnlyList<SiteInfo> visible = all;
        if (all.Count > 0 && startNodes is not null && !startNodes.Contains(-1))
        {
            var rootPaths = entityService.GetAll(UmbracoObjectTypes.Document, all.Select(s => s.RootId).ToArray()).ToDictionary(e => e.Id, e => e.Path);
            var startPaths = startNodes.Count == 0 ? [] : entityService.GetAll(UmbracoObjectTypes.Document, startNodes.ToArray()).Select(e => e.Path).ToList();
            visible = all.Where(s => rootPaths.TryGetValue(s.RootId, out var path) && ApiMapping.SiteVisible(s.RootId, path, startNodes, startPaths)).ToList();
        }

        return Ok(visible.Select(ApiMapping.ToModel).ToArray());
    }

    [HttpGet("document-types")]
    [Authorize(Policy = AuthorizationPolicies.TreeAccessDictionary)]
    [ProducesResponseType<DocumentTypeModel[]>(StatusCodes.Status200OK)]
    public IActionResult GetDocumentTypes()
    {
        if (CurrentUser() is not { } user) return Unauthorized();

        return Ok(overview.DocumentTypeAliases(ScopeOf(user))
            .Select(alias => new DocumentTypeModel { Alias = alias, Name = contentTypeService.Get(alias)?.Name ?? alias })
            .ToArray());
    }

    [HttpGet("overview")]
    [Authorize(Policy = AuthorizationPolicies.TreeAccessDictionary)]
    [ProducesResponseType<OverviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetOverview(
        [FromQuery] Guid? siteRootKey = null,
        [FromQuery] string? documentType = null,
        [FromQuery] string? culture = null,
        [FromQuery] CellFilter? status = null,
        [FromQuery] string? search = null,
        [FromQuery] bool onlyIssues = false,
        [FromQuery] int skip = 0,
        [FromQuery] int take = Paging.FirstPageSize)
    {
        if (CurrentUser() is not { } user) return Unauthorized();
        if (!Paging.IsValid(skip, take)) return Error(ApiErrors.InvalidPaging);

        var languages = (await languageService.GetAllAsync()).ToList();
        if (!TryFilter(languages, siteRootKey, documentType, culture, status, search, onlyIssues, out var filter)) return Error(ApiErrors.InvalidFilter);

        var cultures = ApiMapping.CultureOrder(languages.Select(l => l.IsoCode), sites.DefaultCulture());
        var page = overview.Query(filter, ScopeOf(user), skip, take);
        return Ok(new OverviewResponse
        {
            Total = page.Total,
            Items = page.Items.Select(row => ApiMapping.ToRowModel(row, cultures)).ToArray(),
        });
    }

    /// <summary>Per language how many pages use it and how they stand, for the pages the user can see (optionally of one site or type).</summary>
    [HttpGet("overview/totals")]
    [Authorize(Policy = AuthorizationPolicies.TreeAccessDictionary)]
    [ProducesResponseType<LanguageTotalModel[]>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOverviewTotals([FromQuery] Guid? siteRootKey = null, [FromQuery] string? documentType = null)
    {
        if (CurrentUser() is not { } user) return Unauthorized();

        var order = ApiMapping.CultureOrder((await languageService.GetAllAsync()).Select(l => l.IsoCode), sites.DefaultCulture()).ToList();
        int Position(string culture)
        {
            var index = order.FindIndex(c => string.Equals(c, culture, StringComparison.OrdinalIgnoreCase));
            return index < 0 ? int.MaxValue : index;
        }

        return Ok(overview.Totals(siteRootKey, string.IsNullOrWhiteSpace(documentType) ? null : documentType, ScopeOf(user))
            .OrderBy(t => Position(t.Culture))
            .Select(ApiMapping.ToModel)
            .ToArray());
    }

    /// <summary>The filtered overview as CSV, at most 50,000 pages, with the default language first.</summary>
    [HttpGet("overview/export")]
    [Authorize(Policy = AuthorizationPolicies.TreeAccessDictionary)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK, "text/csv")]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportOverview(
        [FromQuery] Guid? siteRootKey = null,
        [FromQuery] string? documentType = null,
        [FromQuery] string? culture = null,
        [FromQuery] CellFilter? status = null,
        [FromQuery] string? search = null,
        [FromQuery] bool onlyIssues = false)
    {
        if (CurrentUser() is not { } user) return Unauthorized();

        var languages = (await languageService.GetAllAsync()).ToList();
        if (!TryFilter(languages, siteRootKey, documentType, culture, status, search, onlyIssues, out var filter)) return Error(ApiErrors.InvalidFilter);

        var defaultCulture = sites.DefaultCulture();
        var cultures = ApiMapping.CultureOrder(languages.Select(l => l.IsoCode), defaultCulture);
        var rows = breadcrumbs.Primed(overview.ExportRows(filter, ScopeOf(user)));
        var bytes = OverviewExport.Csv(rows, cultures, defaultCulture, row => breadcrumbs.Build(row), BackofficeUrl);
        return File(bytes, CsvContentType, ApiMapping.OverviewFileName(timeProvider.GetUtcNow().UtcDateTime));
    }

    /// <summary>Starts a rebuild of the translation status; 409 with the running job when one runs.</summary>
    [HttpPost("overview/rebuild")]
    [Authorize(Policy = AuthorizationPolicies.TreeAccessDictionary)]
    [ProducesResponseType<JobStateModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<JobStateModel>(StatusCodes.Status409Conflict)]
    public IActionResult RebuildOverview()
    {
        if (CurrentUser() is not { } user) return Unauthorized();

        // The scheduler would remember a rebuild asked for during a job and run it later; an editor who clicks Rebuild wants an answer now.
        if (jobs.Current() is { Status: JobStatus.Running } running) return Conflict(ApiMapping.ToModel(running));

        var started = scheduler.TryStartNow(IndexMode.Rebuild, user.Key, UserName(user));
        if (started is not null) return Ok(ApiMapping.ToModel(started));

        return jobs.Current() is { } current ? Conflict(ApiMapping.ToModel(current)) : Conflict();
    }

    /// <summary>The running job, or the last one that ran; 204 when there has been none.</summary>
    [HttpGet("jobs/current")]
    [Authorize(Policy = AuthorizationPolicies.TreeAccessDictionary)]
    [ProducesResponseType<JobStateModel>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult GetCurrentJob() => jobs.Current() is { } state ? Ok(ApiMapping.ToModel(state)) : NoContent();

    [HttpGet("status")]
    [Authorize(Policy = AuthorizationPolicies.TreeAccessDictionary)]
    [ProducesResponseType<StatusModel>(StatusCodes.Status200OK)]
    public IActionResult GetStatus()
        => Ok(ApiMapping.ToStatus(keyValues.GetValue(Constants.LastIndexErrorKey), keyValues.GetValue(Constants.LastRebuildKey)));

    /// <summary>The data of the Translations tab. Documents that are not culture-variant (or whose type is excluded) answer without touching the index.</summary>
    [HttpGet("document/{key:guid}")]
    [Authorize(Policy = AuthorizationPolicies.SectionAccessContent)]
    [ProducesResponseType<DocumentTranslationsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDocumentTranslations(Guid key)
    {
        if (CurrentUser() is not { } user) return Unauthorized();
        if (!await HasPermissionAsync(ActionBrowse.ActionLetter, key)) return Forbidden();
        if (FindDocument(key) is not { } entity) return NotFoundError(ApiErrors.DocumentNotFound);

        if (!Included(entity)) return Ok(new DocumentTranslationsResponse { VariesByCulture = false, Translations = [] });

        return Ok(await BuildTranslationsAsync(key, user));
    }

    /// <summary>
    /// Records a language of the document as up to date. Needs update permission on the document and edit permission for
    /// the language; 409 when the language is not outdated.
    /// </summary>
    [HttpPost("document/{key:guid}/{culture}/mark-up-to-date")]
    [Authorize(Policy = AuthorizationPolicies.SectionAccessContent)]
    [ProducesResponseType<DocumentTranslationsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MarkUpToDate(Guid key, string culture)
    {
        if (CurrentUser() is not { } user) return Unauthorized();
        if (!await HasPermissionAsync(ActionUpdate.ActionLetter, key)) return Forbidden();
        if (FindDocument(key) is null) return NotFoundError(ApiErrors.DocumentNotFound);

        var language = (await languageService.GetAllAsync()).FirstOrDefault(l => string.Equals(l.IsoCode, culture, StringComparison.OrdinalIgnoreCase));
        if (language is null) return NotFoundError(ApiErrors.LanguageNotFound);
        if (!AccessPolicy.CanEditLanguage(user.Groups, language.Id)) return Forbidden();

        try
        {
            // Checked under the lock of the reindexer: a save that makes the language up to date in between is not overwritten.
            reindexer.MarkUpToDate(key, language.IsoCode, user.Key, requireOutdated: true);
        }
        catch (NotOutdatedException)
        {
            // Any other failure is not a conflict: it surfaces as a server error.
            return ConflictError(ApiErrors.NotOutdated);
        }

        return Ok(await BuildTranslationsAsync(key, user));
    }

    private async Task<DocumentTranslationsResponse> BuildTranslationsAsync(Guid key, IUser user)
    {
        var translations = documents.Get(key);
        if (translations is null)
        {
            return new DocumentTranslationsResponse { VariesByCulture = true, Translations = [] };
        }

        var canUpdate = await HasPermissionAsync(ActionUpdate.ActionLetter, key);
        var languages = (await languageService.GetAllAsync()).ToList();
        var editable = AccessPolicy.EditableCultures(user.Groups, languages);
        return new DocumentTranslationsResponse
        {
            VariesByCulture = true,
            Translations = translations
                .Select(t => ApiMapping.ToModel(t, canUpdate && !t.IsDefault && t.Row.Outdated && editable.Contains(t.Culture)))
                .ToArray(),
        };
    }

    // The document, when it exists and is not in the recycle bin.
    private IEntitySlim? FindDocument(Guid key)
        => entityService.Get(key, UmbracoObjectTypes.Document) is { Trashed: false } entity ? entity : null;

    /// <summary>
    /// Whether the document has a Translations tab: its type varies by culture and is not excluded. Decided from the
    /// document type, so a plain page costs one lookup and no indexing.
    /// </summary>
    private bool Included(IEntitySlim entity)
    {
        if (entity is not IContentEntitySlim content
            || options.CurrentValue.ExcludedDocumentTypes.Contains(content.ContentTypeAlias, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var type = contentTypeService.Get(content.ContentTypeKey);
        return type is not null && (type.Variations & ContentVariation.Culture) != 0;
    }

    private async Task<bool> HasPermissionAsync(string actionLetter, Guid key)
    {
        // Same check as Umbraco's own document endpoints: start nodes and the permission of the user's groups.
        var result = await authorizationService.AuthorizeResourceAsync(
            User,
            ContentPermissionResource.WithKeys(actionLetter, key),
            AuthorizationPolicies.ContentPermissionByResource);
        return result.Succeeded;
    }

    private bool TryFilter(
        IReadOnlyList<ILanguage> languages,
        Guid? siteRootKey,
        string? documentType,
        string? culture,
        CellFilter? status,
        string? search,
        bool onlyIssues,
        out OverviewFilter filter)
    {
        filter = new OverviewFilter(null, null, null, null, null, false);
        if (status is { } s && !Enum.IsDefined(s)) return false;
        if (search is { Length: > MaxSearchLength } || documentType is { Length: > 255 }) return false;

        // The status table stores the ISO code as Umbraco spells it; a lookup by another casing finds nothing on SQLite.
        string? installed = null;
        if (!string.IsNullOrWhiteSpace(culture))
        {
            installed = languages.FirstOrDefault(l => string.Equals(l.IsoCode, culture, StringComparison.OrdinalIgnoreCase))?.IsoCode;
            if (installed is null) return false;
        }

        filter = new OverviewFilter(
            siteRootKey,
            string.IsNullOrWhiteSpace(documentType) ? null : documentType,
            installed,
            status,
            string.IsNullOrWhiteSpace(search) ? null : search,
            onlyIssues);
        return true;
    }

    // The start nodes the user's groups and own setting give: null means full access (root).
    private IReadOnlyCollection<int>? StartNodes(IUser user)
    {
        var calculated = user.CalculateContentStartNodeIds(entityService, appCaches);
        return StartNodeAccess.Resolve(calculated?.Contains(-1) == true, calculated);
    }

    private StartNodeScope ScopeOf(IUser user) => new(StartNodes(user));

    private string BackofficeUrl(Guid key)
    {
        var request = HttpContext.Request;
        // The backoffice path is /umbraco (GlobalSettings no longer has a setting for it in Umbraco 17).
        var path = hostingEnvironment.ToAbsolute("~/umbraco").TrimEnd('/');
        return $"{request.Scheme}://{request.Host}{path}/section/content/workspace/document/edit/{key}";
    }

    private IUser? CurrentUser() => security.BackOfficeSecurity?.CurrentUser;

    private static string UserName(IUser user) => user.Name ?? user.Username;
}
