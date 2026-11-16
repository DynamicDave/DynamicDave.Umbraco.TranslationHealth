using DynamicDave.Umbraco.TranslationHealth.Files;
using DynamicDave.Umbraco.TranslationHealth.Importing;
using DynamicDave.Umbraco.TranslationHealth.Jobs;
using DynamicDave.Umbraco.TranslationHealth.Persistence;
using DynamicDave.Umbraco.TranslationHealth.Status;

namespace DynamicDave.Umbraco.TranslationHealth.Models;

/// <summary>Stable error codes of the API's 400, 403, 404, 409 and 500 responses (besides the <see cref="ImportFileError"/> names).</summary>
public static class ApiErrors
{
    public const string InvalidPaging = "invalidPaging";
    public const string InvalidFilter = "invalidFilter";
    public const string InvalidCulture = "invalidCulture";
    public const string InvalidFormat = "invalidFormat";
    public const string InvalidSelection = "invalidSelection";
    public const string PlanExpired = "planExpired";
    public const string ImportNotFound = "importNotFound";
    public const string AlreadyUndone = "alreadyUndone";
    public const string DocumentNotFound = "documentNotFound";
    public const string LanguageNotFound = "languageNotFound";
    public const string NotOutdated = "notOutdated";
    public const string LanguageNotAllowed = "languageNotAllowed";
    public const string ApplyFailed = "applyFailed";
    public const string UndoFailed = "undoFailed";
}

public sealed class ErrorResponse
{
    public required string Error { get; init; }
}

public sealed class LanguageModel
{
    public required string Culture { get; init; }
    public required string Name { get; init; }
    public required bool IsDefault { get; init; }
}

public sealed class SiteModel
{
    public required Guid RootKey { get; init; }
    public required string Name { get; init; }

    /// <summary>The languages of the site's domains; empty when the site has no domains (it then uses all languages).</summary>
    public required string[] Cultures { get; init; }

    /// <summary>The site has domains, but none for the default language.</summary>
    public required bool MissingDefaultCulture { get; init; }
}

public sealed class DocumentTypeModel
{
    public required string Alias { get; init; }
    public required string Name { get; init; }
}

/// <summary>One language of a page in the overview. Languages the page has no row for come as <see cref="CellStatus.NotApplicable"/>.</summary>
public sealed class VariantCellModel
{
    public required string Culture { get; init; }
    public required CellStatus Status { get; init; }
    public required bool Outdated { get; init; }
    public required TrackingMode Mode { get; init; }

    /// <summary>The name of the page in this language, when it has one.</summary>
    public string? Name { get; init; }

    public DateTime? OutdatedSinceUtc { get; init; }
}

public sealed class OverviewRowModel
{
    public required Guid Key { get; init; }
    public required string Name { get; init; }
    public Guid? ParentKey { get; init; }

    /// <summary>Depth in the tree (the root is 1), for indenting.</summary>
    public required int Level { get; init; }

    public required string ContentTypeAlias { get; init; }
    public required Guid SiteRootKey { get; init; }

    /// <summary>One cell per installed language, the default language first.</summary>
    public required VariantCellModel[] Cells { get; init; }
}

public sealed class OverviewResponse
{
    public required int Total { get; init; }
    public required OverviewRowModel[] Items { get; init; }
}

public sealed class LanguageTotalModel
{
    public required string Culture { get; init; }
    public required int Pages { get; init; }
    public required int Complete { get; init; }
    public required int Missing { get; init; }
    public required int Draft { get; init; }
    public required int PendingChanges { get; init; }
    public required int Outdated { get; init; }
}

public sealed class JobStateModel
{
    public required Guid Id { get; init; }
    public required JobKind Kind { get; init; }
    public required JobStatus Status { get; init; }
    public required Guid UserKey { get; init; }
    public required string UserName { get; init; }
    public required DateTime StartedUtc { get; init; }
    public required DateTime HeartbeatUtc { get; init; }
    public DateTime? CompletedUtc { get; init; }
    public required int Done { get; init; }
    public required int Total { get; init; }
    public required string Phase { get; init; }
    public string? Error { get; init; }
    public string? Summary { get; init; }
}

public sealed class StatusModel
{
    /// <summary>A stable code of the last failure of the indexing worker ("indexFailed"); the details are in the log. Null when the last run went well.</summary>
    public string? LastIndexError { get; init; }
    public DateTime? LastIndexErrorUtc { get; init; }
    public DateTime? LastRebuildUtc { get; init; }
}

public sealed class DocumentTranslationModel
{
    public required string Culture { get; init; }
    public required string LanguageName { get; init; }
    public required bool IsDefault { get; init; }
    public required CellStatus Status { get; init; }
    public required bool Outdated { get; init; }
    public required TrackingMode Mode { get; init; }
    public DateTime? OutdatedSinceUtc { get; init; }
    public DateTime? UpdateDateUtc { get; init; }
    public DateTime? PublishDateUtc { get; init; }
    public string? Name { get; init; }
    public BaselineSource? BaselineSource { get; init; }
    public DateTime? MarkedUtc { get; init; }
    public string? MarkedByName { get; init; }

    /// <summary>The language is outdated and the current user may mark it up to date (update permission on the page and edit permission for the language).</summary>
    public required bool CanMarkUpToDate { get; init; }
}

public sealed class DocumentTranslationsResponse
{
    /// <summary>False for documents that are not culture-variant or whose type is excluded: they have no Translations tab.</summary>
    public required bool VariesByCulture { get; init; }
    public required DocumentTranslationModel[] Translations { get; init; }
}

public sealed class MissingCountModel
{
    public required string Culture { get; init; }
    public required int Missing { get; init; }
}

public sealed class MissingItemModel
{
    public required Guid Id { get; init; }
    public required string Key { get; init; }
    public required string Group { get; init; }
    public required string[] MissingCultures { get; init; }
}

public sealed class MissingResponse
{
    /// <summary>Per language, the number of items without a value (ignores the culture filter, respects group and search).</summary>
    public required MissingCountModel[] Counts { get; init; }
    public required int Total { get; init; }
    public required MissingItemModel[] Items { get; init; }
}

public sealed class OutcomeCountModel
{
    public required ImportOutcome Outcome { get; init; }
    public required int Count { get; init; }
}

public sealed class PlanItemModel
{
    public required int Id { get; init; }
    public required int RowNumber { get; init; }
    public required string Key { get; init; }
    public required string Culture { get; init; }
    public required ImportOutcome Outcome { get; init; }
    public required ImportErrorReason Reason { get; init; }
    public string? CurrentValue { get; init; }
    public required string NewValue { get; init; }
    public string? FileSource { get; init; }
    public string? CurrentSource { get; init; }
    public required bool SourceChanged { get; init; }
    public required bool SelectedByDefault { get; init; }
}

public sealed class ImportPlanResponse
{
    public required Guid PlanId { get; init; }
    public required string FileName { get; init; }
    public required ImportFileFormat Format { get; init; }
    public required string[] UnknownColumns { get; init; }
    public required OutcomeCountModel[] Counts { get; init; }
    public required int Total { get; init; }

    /// <summary>The first page of items; the rest comes from the items endpoint.</summary>
    public required PlanItemModel[] Items { get; init; }
}

public sealed class PlanItemsResponse
{
    public required int Total { get; init; }
    public required PlanItemModel[] Items { get; init; }
}

public sealed class ApplyRequest
{
    public required int[] SelectedItemIds { get; init; }
}

public sealed class AppliedItemModel
{
    public required int ItemId { get; init; }
    public required ApplyOutcome Outcome { get; init; }
}

public sealed class ApplyResponse
{
    /// <summary>The history entry of the import; null when nothing was written (there is no entry then).</summary>
    public Guid? ImportKey { get; init; }
    public required int Applied { get; init; }
    public required int Skipped { get; init; }
    public required AppliedItemModel[] Items { get; init; }
}

public sealed class ImportModel
{
    public required Guid Key { get; init; }
    public required DateTime CreatedUtc { get; init; }
    public required Guid UserKey { get; init; }
    public required string UserName { get; init; }
    public required string FileName { get; init; }
    public required string Format { get; init; }
    public required bool Overwrite { get; init; }
    public required bool CreateKeys { get; init; }
    public required int Applied { get; init; }
    public required int Skipped { get; init; }
    public required ImportState State { get; init; }
    public DateTime? UndoneUtc { get; init; }
    public string? UndoneBy { get; init; }
}

public sealed class ImportsResponse
{
    public required int Total { get; init; }
    public required ImportModel[] Items { get; init; }
}

public sealed class ImportRowModel
{
    public required string Key { get; init; }
    public required string Culture { get; init; }
    public string? OldValue { get; init; }
    public required string NewValue { get; init; }
    public required bool KeyCreated { get; init; }
}

public sealed class ImportDetailResponse
{
    public required ImportModel Import { get; init; }
    public required int TotalRows { get; init; }
    public required ImportRowModel[] Rows { get; init; }
}

public sealed class UndoResponse
{
    public required int Restored { get; init; }
    public required int KeysRemoved { get; init; }

    /// <summary>Rows that were not restored because the value or key was changed after the import (or is gone).</summary>
    public required ImportRowModel[] LeftAlone { get; init; }
}
