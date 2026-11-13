using System.Text.Json.Serialization;
using DynamicDave.Umbraco.TranslationHealth.Files;

namespace DynamicDave.Umbraco.TranslationHealth.Importing;

[JsonConverter(typeof(JsonStringEnumConverter<ImportOutcome>))]
public enum ImportOutcome { NewValue, Overwrite, Unchanged, NewKey, SkippedHasValue, SkippedUnknownKey, Error }

[JsonConverter(typeof(JsonStringEnumConverter<ImportErrorReason>))]
public enum ImportErrorReason { None, LanguageNotAllowed, UnknownLanguage, DuplicateKey, EmptyKey }

public sealed record ImportOptions(bool Overwrite, bool CreateKeys);

/// <summary>The planned result for one non-empty (row, culture) cell of an import file.</summary>
public sealed record PlanItem(
    int Id,
    int RowNumber,
    string Key,
    string Culture,
    ImportOutcome Outcome,
    ImportErrorReason Reason,
    string? CurrentValue,
    string NewValue,
    string? FileSource,
    string? CurrentSource,
    bool SourceChanged,
    bool SelectedByDefault,
    Guid? EntryId = null);

public sealed record ImportPlan(
    Guid Id,
    string FileName,
    ImportFileFormat Format,
    ImportOptions Options,
    IReadOnlyList<string> UnknownColumns,
    IReadOnlyList<PlanItem> Items);
