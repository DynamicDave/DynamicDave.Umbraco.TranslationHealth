using DynamicDave.Umbraco.TranslationHealth.Status;

namespace DynamicDave.Umbraco.TranslationHealth.Overview;

public enum CellFilter
{
    Missing,
    Draft,
    PendingChanges,
    Outdated,
    AnyIssue,
}

public record OverviewFilter(
    Guid? SiteRootKey,
    string? ContentTypeAlias,
    string? Culture,
    CellFilter? Status,
    string? NameSearch,
    bool OnlyIssues);

/// <summary>The content the user may see: the start node ids, or null for everything.</summary>
public record StartNodeScope(IReadOnlyCollection<int>? StartNodeIds);

public record OverviewRow(DocumentRow Document, IReadOnlyList<VariantRow> Variants);

public record OverviewPage(int Total, IReadOnlyList<OverviewRow> Items);

/// <summary>Per language: pages that use it (Not applicable excluded) and how they stand. Complete = Published and not outdated.</summary>
public record LanguageTotal(string Culture, int Pages, int Complete, int Missing, int Draft, int PendingChanges, int Outdated);
