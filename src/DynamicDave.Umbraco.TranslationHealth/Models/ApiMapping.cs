using System.Globalization;
using System.Text;
using System.Text.Json;
using DynamicDave.Umbraco.TranslationHealth.Dictionary;
using DynamicDave.Umbraco.TranslationHealth.Documents;
using DynamicDave.Umbraco.TranslationHealth.Files;
using DynamicDave.Umbraco.TranslationHealth.Importing;
using DynamicDave.Umbraco.TranslationHealth.Indexing;
using DynamicDave.Umbraco.TranslationHealth.Jobs;
using DynamicDave.Umbraco.TranslationHealth.Overview;
using DynamicDave.Umbraco.TranslationHealth.Persistence;
using DynamicDave.Umbraco.TranslationHealth.Status;

namespace DynamicDave.Umbraco.TranslationHealth.Models;

/// <summary>A test-run plan as kept in the <see cref="ITempStore"/>: the plan and the user who made it.</summary>
public sealed record StoredPlan(Guid UserKey, ImportPlan Plan);

/// <summary>Request validation and the mapping between the package's records and the API models. Pure: no services.</summary>
public static class ApiMapping
{
    /// <summary>The code the status endpoint gives for a failed indexing run; the details are in the log, not in the response.</summary>
    public const string IndexFailedCode = "indexFailed";

    // How much of an uploaded file is looked at to find out whether it is XLIFF or CSV.
    private const int SniffBytes = 512;

    private static readonly char[] CsvDelimiters = [',', ';', '\t'];

    public static VariantCellModel ToCell(VariantRow row) => new()
    {
        Culture = row.Culture,
        Status = row.Status,
        Outdated = row.Outdated,
        Mode = row.Mode,
        Name = row.Name,
        OutdatedSinceUtc = row.OutdatedSinceUtc,
    };

    /// <summary>The page with one cell per culture in <paramref name="cultures"/> order; a culture the page has no row for is Not applicable.</summary>
    public static OverviewRowModel ToRowModel(OverviewRow row, IReadOnlyList<string> cultures)
    {
        var byCulture = row.Variants.ToDictionary(v => v.Culture, StringComparer.OrdinalIgnoreCase);
        return new OverviewRowModel
        {
            Key = row.Document.Key,
            Name = row.Document.Name,
            ParentKey = row.Document.ParentKey,
            Level = row.Document.Level,
            ContentTypeAlias = row.Document.ContentTypeAlias,
            SiteRootKey = row.Document.SiteRootKey,
            Cells = cultures
                .Select(culture => byCulture.TryGetValue(culture, out var variant)
                    ? ToCell(variant)
                    : new VariantCellModel { Culture = culture, Status = CellStatus.NotApplicable, Outdated = false, Mode = TrackingMode.Date })
                .ToArray(),
        };
    }

    /// <summary>The default language first, then the others in the given order.</summary>
    public static IReadOnlyList<string> CultureOrder(IEnumerable<string> installed, string defaultCulture)
    {
        var all = installed.ToList();
        var defaultName = all.FirstOrDefault(c => string.Equals(c, defaultCulture, StringComparison.OrdinalIgnoreCase));
        return defaultName is null ? all : [defaultName, .. all.Where(c => !string.Equals(c, defaultCulture, StringComparison.OrdinalIgnoreCase))];
    }

    public static LanguageTotalModel ToModel(LanguageTotal total) => new()
    {
        Culture = total.Culture,
        Pages = total.Pages,
        Complete = total.Complete,
        Missing = total.Missing,
        Draft = total.Draft,
        PendingChanges = total.PendingChanges,
        Outdated = total.Outdated,
    };

    public static SiteModel ToModel(Sites.SiteInfo site) => new()
    {
        RootKey = site.RootKey,
        Name = site.Name,
        Cultures = [.. site.Cultures],
        MissingDefaultCulture = site.MissingDefaultCulture,
    };

    /// <summary>
    /// Whether the user can see anything of a site: they see its root (or an ancestor), or one of their start nodes lies inside it.
    /// <paramref name="startNodeIds"/> null or containing -1 means full access.
    /// </summary>
    public static bool SiteVisible(int rootId, string rootPath, IReadOnlyCollection<int>? startNodeIds, IEnumerable<string> startNodePaths)
        => StartNodeAccess.CanSee(rootPath, startNodeIds) || startNodePaths.Any(path => StartNodeAccess.CanSee(path, [rootId]));

    public static JobStateModel ToModel(JobState state) => new()
    {
        Id = state.Id,
        Kind = state.Kind,
        Status = state.Status,
        UserKey = state.UserKey,
        UserName = state.UserName,
        StartedUtc = state.StartedUtc,
        HeartbeatUtc = state.HeartbeatUtc,
        CompletedUtc = state.CompletedUtc,
        Done = state.Done,
        Total = state.Total,
        Phase = state.Phase,
        Error = state.Error,
        Summary = state.Summary,
    };

    /// <summary>The status endpoint's data from the two stored values (the JSON of <see cref="IndexError"/> and an ISO date); unreadable values count as absent.</summary>
    public static StatusModel ToStatus(string? lastIndexErrorJson, string? lastRebuild)
    {
        IndexError? error = null;
        if (!string.IsNullOrWhiteSpace(lastIndexErrorJson))
        {
            try
            {
                error = JsonSerializer.Deserialize<IndexError>(lastIndexErrorJson);
            }
            catch (JsonException)
            {
            }
        }

        DateTime? rebuilt = DateTime.TryParse(lastRebuild, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
        return new StatusModel
        {
            LastIndexError = error is null ? null : IndexFailedCode,
            LastIndexErrorUtc = error?.Utc,
            LastRebuildUtc = rebuilt,
        };
    }

    public static DocumentTranslationModel ToModel(DocumentTranslation translation, bool canMarkUpToDate) => new()
    {
        Culture = translation.Culture,
        LanguageName = translation.LanguageName,
        IsDefault = translation.IsDefault,
        Status = translation.Row.Status,
        Outdated = translation.Row.Outdated,
        Mode = translation.Row.Mode,
        OutdatedSinceUtc = translation.Row.OutdatedSinceUtc,
        UpdateDateUtc = translation.Row.UpdateDateUtc,
        PublishDateUtc = translation.Row.PublishDateUtc,
        Name = translation.Row.Name,
        BaselineSource = translation.Row.BaselineSource,
        MarkedUtc = translation.Row.MarkedUtc,
        MarkedByName = translation.MarkedByName,
        CanMarkUpToDate = canMarkUpToDate,
    };

    public static MissingResponse ToMissingResponse(IReadOnlyList<MissingCount> counts, IReadOnlyList<MissingItem> items, int skip, int take) => new()
    {
        Counts = counts.Select(c => new MissingCountModel { Culture = c.Culture, Missing = c.Missing }).ToArray(),
        Total = items.Count,
        Items = items.Skip(skip).Take(take)
            .Select(i => new MissingItemModel { Id = i.Id, Key = i.Key, Group = i.Group, MissingCultures = [.. i.MissingCultures] })
            .ToArray(),
    };

    /// <summary>Every group (and each parent group of a dotted one) once, sorted. Items without a group add none.</summary>
    public static IReadOnlyList<string> Groups(IReadOnlyList<DictionaryEntry> entries)
    {
        var byId = entries.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
        var groups = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var path = DictionaryGrouping.GroupPath(entry, byId);
            for (var length = 1; length <= path.Count; length++)
            {
                groups.Add(DictionaryGrouping.GroupName([.. path.Take(length)]));
            }
        }

        return [.. groups];
    }

    /// <summary>
    /// The entries of an export: those of <paramref name="group"/> (that group or a deeper one) that lack a value in
    /// <paramref name="missingIn"/>. Both null = all. The order of <paramref name="entries"/> is kept.
    /// </summary>
    public static IReadOnlyList<DictionaryEntry> FilterForExport(IReadOnlyList<DictionaryEntry> entries, string? missingIn, string? group)
    {
        if (string.IsNullOrEmpty(missingIn) && string.IsNullOrEmpty(group))
        {
            return entries;
        }

        var byId = entries.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
        return entries
            .Where(entry =>
                (string.IsNullOrEmpty(group) || MissingReport.InGroup(DictionaryGrouping.GroupName(DictionaryGrouping.GroupPath(entry, byId)), group))
                && (string.IsNullOrEmpty(missingIn) || DictionaryValues.NonEmpty(entry.Values, missingIn) is null))
            .ToList();
    }

    public static ImportPlanResponse ToPlanResponse(ImportPlan plan)
    {
        var counts = plan.Items.GroupBy(i => i.Outcome).ToDictionary(g => g.Key, g => g.Count());
        return new ImportPlanResponse
        {
            PlanId = plan.Id,
            FileName = plan.FileName,
            Format = plan.Format,
            UnknownColumns = [.. plan.UnknownColumns],
            Counts = Enum.GetValues<ImportOutcome>()
                .Select(o => new OutcomeCountModel { Outcome = o, Count = counts.GetValueOrDefault(o) })
                .ToArray(),
            Total = plan.Items.Count,
            Items = plan.Items.Take(Paging.FirstPageSize).Select(ToModel).ToArray(),
        };
    }

    /// <summary>The items with one of <paramref name="outcomes"/> (all items when null or empty), in plan order.</summary>
    public static PlanItemsResponse ToPlanItems(ImportPlan plan, ImportOutcome[]? outcomes, int skip, int take)
    {
        var items = outcomes is { Length: > 0 } ? plan.Items.Where(i => outcomes.Contains(i.Outcome)).ToList() : plan.Items;
        return new PlanItemsResponse { Total = items.Count, Items = items.Skip(skip).Take(take).Select(ToModel).ToArray() };
    }

    public static PlanItemModel ToModel(PlanItem item) => new()
    {
        Id = item.Id,
        RowNumber = item.RowNumber,
        Key = item.Key,
        Culture = item.Culture,
        Outcome = item.Outcome,
        Reason = item.Reason,
        CurrentValue = item.CurrentValue,
        NewValue = item.NewValue,
        FileSource = item.FileSource,
        CurrentSource = item.CurrentSource,
        SourceChanged = item.SourceChanged,
        SelectedByDefault = item.SelectedByDefault,
    };

    public static string SerializePlan(Guid userKey, ImportPlan plan) => JsonSerializer.Serialize(new StoredPlan(userKey, plan));

    /// <summary>The stored plan, or null when the text is not one.</summary>
    public static StoredPlan? ReadPlan(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<StoredPlan>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Checks the ids a user selected against the plan and drops the items of cultures the user may not edit (right now,
    /// not when the test run was made). Invalid when an id is not an item of the plan.
    /// </summary>
    public static (bool Valid, IReadOnlySet<int> Selected) Select(ImportPlan plan, IEnumerable<int> selectedItemIds, IReadOnlySet<string> editableCultures)
    {
        var byId = plan.Items.ToDictionary(i => i.Id);
        var selected = new HashSet<int>();
        foreach (var id in selectedItemIds)
        {
            if (!byId.TryGetValue(id, out var item))
            {
                return (false, new HashSet<int>());
            }

            if (editableCultures.Contains(item.Culture))
            {
                selected.Add(id);
            }
        }

        return (true, selected);
    }

    /// <summary>
    /// The format of an uploaded file: by extension (.csv, .xlf, .xliff), otherwise by what the file starts with
    /// (XML is XLIFF, a first line with a comma, semicolon or tab is CSV). Null when neither says.
    /// </summary>
    public static ImportFileFormat? DetectFormat(string fileName, byte[] head)
    {
        switch (Path.GetExtension(fileName).ToLowerInvariant())
        {
            case ".csv":
                return ImportFileFormat.Csv;
            case ".xlf" or ".xliff":
                return ImportFileFormat.Xliff;
        }

        var text = Encoding.UTF8.GetString(head, 0, Math.Min(head.Length, SniffBytes)).TrimStart('﻿', ' ', '\t', '\r', '\n');
        if (text.Length == 0)
        {
            return null;
        }

        if (text[0] == '<')
        {
            return ImportFileFormat.Xliff;
        }

        var firstLine = text.Split('\n', 2)[0];
        return firstLine.IndexOfAny(CsvDelimiters) >= 0 ? ImportFileFormat.Csv : null;
    }

    public static string OverviewFileName(DateTime utcNow) => $"translation-overview-{Stamp(utcNow)}.csv";

    public static string DictionaryFileName(ImportFileFormat format, string? culture, DateTime utcNow) => format == ImportFileFormat.Xliff
        ? $"dictionary-{culture}-{Stamp(utcNow)}.xlf"
        : $"dictionary-{Stamp(utcNow)}.csv";

    /// <summary>The upload's file name as stored with the plan: no path, and short enough for the history.</summary>
    public static string UploadFileName(string? fileName)
    {
        var name = Path.GetFileName(fileName ?? string.Empty);
        return name.Length > ImportApplier.MaxText ? name[..ImportApplier.MaxText] : name;
    }

    public static ApplyResponse ToApplyResponse(ApplyResult result) => new()
    {
        ImportKey = result.Applied > 0 ? result.ImportKey : null,
        Applied = result.Applied,
        Skipped = result.Skipped,
        Items = result.Items.Select(i => new AppliedItemModel { ItemId = i.ItemId, Outcome = i.Outcome }).ToArray(),
    };

    public static ImportModel ToModel(ImportRecord record) => new()
    {
        Key = record.Key,
        CreatedUtc = record.CreatedUtc,
        UserKey = record.UserKey,
        UserName = record.UserName,
        FileName = record.FileName,
        Format = record.Format,
        Overwrite = record.Overwrite,
        CreateKeys = record.CreateKeys,
        Applied = record.Applied,
        Skipped = record.Skipped,
        State = record.State,
        UndoneUtc = record.UndoneUtc,
        UndoneBy = record.UndoneBy,
    };

    public static ImportRowModel ToModel(ImportRowRecord row) => new()
    {
        Key = row.Key,
        Culture = row.Culture,
        OldValue = row.OldValue,
        NewValue = row.NewValue,
        KeyCreated = row.KeyCreated,
    };

    public static UndoResponse ToModel(UndoResult result) => new()
    {
        Restored = result.Restored,
        KeysRemoved = result.KeysRemoved,
        LeftAlone = result.LeftAlone.Select(ToModel).ToArray(),
    };

    private static string Stamp(DateTime utcNow) => utcNow.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
}
