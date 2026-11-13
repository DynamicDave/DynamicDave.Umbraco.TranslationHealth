using System.Globalization;
using System.Text.Json;
using DynamicDave.Umbraco.TranslationHealth.Sites;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;

namespace DynamicDave.Umbraco.TranslationHealth.Content;

/// <summary>Pure parts of the snapshot reader, testable without Umbraco.</summary>
public static class SnapshotBuilder
{
    /// <summary>Joins the sort orders of ancestors-or-self, zero-padded to 5 digits, so a string compare gives tree order.</summary>
    public static string SortPath(IReadOnlyList<int> ancestorOrSelfSortOrders)
        => string.Join('.', ancestorOrSelfSortOrders.Select(order => order.ToString("D5", CultureInfo.InvariantCulture)));

    /// <summary>
    /// The translatable text of one culture. A culture-variant property contributes its value (rich text: its markup, plus
    /// the block values of its blocks; Block List/Grid: block values only; anything else: the value as string). An invariant
    /// property contributes only the block values of this culture. Block JSON is never hashed raw, because it carries
    /// noise (layout Udi fields differ between Umbraco 17 and 18, empty rich text blocks are normalised on save).
    /// </summary>
    public static IReadOnlyList<PropertyText> CultureTexts(
        IEnumerable<(string Alias, bool VariesByCulture, string? EditorAlias, Func<string?, object?> GetValue)> properties,
        string culture)
    {
        var result = new List<PropertyText>();
        foreach (var (alias, variesByCulture, _, getValue) in properties)
        {
            if (!variesByCulture)
            {
                AddBlockValues(result, alias, getValue(null) as string, culture, includeInvariant: false);
                continue;
            }

            switch (getValue(culture))
            {
                case null:
                    break;
                case string text:
                    AddVariantText(result, alias, text, culture);
                    break;
                case var other:
                    var scalar = Convert.ToString(other, CultureInfo.InvariantCulture);
                    if (!string.IsNullOrEmpty(scalar))
                    {
                        result.Add(new PropertyText(alias, scalar));
                    }

                    break;
            }
        }

        return result;
    }

    private static void AddVariantText(List<PropertyText> result, string alias, string text, string culture)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (text.TrimStart().StartsWith('{'))
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("markup", out var markup) && markup.ValueKind == JsonValueKind.String)
                    {
                        var html = markup.GetString();
                        if (!string.IsNullOrEmpty(html))
                        {
                            result.Add(new PropertyText(alias, html));
                        }

                        AddBlockValues(result, alias, text, culture, includeInvariant: true);
                        return;
                    }

                    if (root.TryGetProperty("contentData", out _))
                    {
                        AddBlockValues(result, alias, text, culture, includeInvariant: true);
                        return;
                    }
                }
            }
            catch (JsonException)
            {
                // Not JSON after all: plain text.
            }
        }

        result.Add(new PropertyText(alias, text));
    }

    private static void AddBlockValues(List<PropertyText> result, string alias, string? json, string culture, bool includeInvariant)
    {
        foreach (var (path, value) in BlockValueReader.CultureValues(json, culture, includeInvariant))
        {
            result.Add(new PropertyText($"{alias}/{path}", value));
        }
    }
}

/// <summary>
/// Reads document snapshots through <c>IContentService</c>. Members that moved to <c>IPublishableContentBase</c> in
/// Umbraco 18 are bound by name. The state of a published culture compares the draft with the published fingerprint
/// instead of using <c>IsCultureEdited</c>, which is wrong for block-only edits (prototype findings, section 1).
/// All injected services are singletons in Umbraco, so this class is registered as a singleton.
/// </summary>
public sealed class UmbracoContentSnapshotSource(
    IContentService contentService,
    IContentTypeService contentTypeService,
    IEntityService entityService,
    IDocumentNavigationQueryService navigation,
    ICoreScopeProvider scopeProvider,
    ISiteSource sites,
    IOptionsMonitor<TranslationHealthOptions> options,
    TimeProvider timeProvider) : IContentSnapshotSource
{
    private static readonly Func<IContent, string, bool> IsCulturePublished =
        InterfaceMethod.BindOpen<Func<IContent, string, bool>>(typeof(IContent), "IsCulturePublished");

    private static readonly Func<IContent, string, DateTime?> GetPublishDate =
        InterfaceMethod.BindOpen<Func<IContent, string, DateTime?>>(typeof(IContent), "GetPublishDate");

    private static readonly Func<IContent, string, string?> GetPublishName =
        InterfaceMethod.BindOpen<Func<IContent, string, string?>>(typeof(IContent), "GetPublishName");

    private static readonly TimeSpan TypeIdsCacheDuration = TimeSpan.FromSeconds(60);

    private readonly object _typeIdsLock = new();
    private int[] _typeIds = [];
    private DateTimeOffset _typeIdsLoadedAt = DateTimeOffset.MinValue;

    public DocumentSnapshot? Get(Guid key)
    {
        var content = contentService.GetById(key);
        if (content is null || content.Trashed || !IsIncluded(content.ContentType.Variations, content.ContentType.Alias))
        {
            return null;
        }

        return Build([content])[0];
    }

    public IReadOnlyList<DocumentSnapshot> GetPage(long pageIndex, int pageSize, out long total)
    {
        var typeIds = IncludedTypeIds();
        if (typeIds.Length == 0)
        {
            total = 0;
            return [];
        }

        var notTrashed = scopeProvider.CreateQuery<IContent>().Where(content => content.Trashed == false);
        var page = contentService.GetPagedOfTypes(typeIds, pageIndex, pageSize, out total, notTrashed, Ordering.By("path")).ToList();
        return Build(page);
    }

    // Culture-variant, non-excluded document types; GetAll clones every type, so it is cached for a minute.
    private int[] IncludedTypeIds()
    {
        lock (_typeIdsLock)
        {
            var now = timeProvider.GetUtcNow();
            if (now - _typeIdsLoadedAt >= TypeIdsCacheDuration)
            {
                _typeIds = contentTypeService.GetAll()
                    .Where(type => IsIncluded(type.Variations, type.Alias))
                    .Select(type => type.Id)
                    .ToArray();
                _typeIdsLoadedAt = now;
            }

            return _typeIds;
        }
    }

    public IReadOnlyList<Guid> DescendantKeys(Guid key)
        => navigation.TryGetDescendantsKeys(key, out var keys) ? keys.ToList() : [];

    private bool IsIncluded(ContentVariation variations, string alias)
        => (variations & ContentVariation.Culture) != 0
            && !options.CurrentValue.ExcludedDocumentTypes.Contains(alias, StringComparer.OrdinalIgnoreCase);

    private List<DocumentSnapshot> Build(IReadOnlyList<IContent> documents)
    {
        // The ancestor id list is bounded by the page size (at most 100 documents times their depth).
        // One light query for the ancestors of the whole page: their keys and sort orders.
        var ancestorIds = documents
            .SelectMany(d => PathIds(d.Path).Where(id => id != d.Id))
            .Distinct()
            .ToArray();
        var ancestors = ancestorIds.Length == 0
            ? []
            : entityService.GetAll(UmbracoObjectTypes.Document, ancestorIds).ToDictionary(e => e.Id);

        var cultures = sites.AllCultures();
        var result = new List<DocumentSnapshot>(documents.Count);
        foreach (var content in documents)
        {
            var sortOrders = PathIds(content.Path)
                .Select(id => id == content.Id ? content.SortOrder : ancestors.TryGetValue(id, out var a) ? a.SortOrder : 0)
                .ToList();
            Guid? parentKey = ancestors.TryGetValue(content.ParentId, out var parent) ? parent.Key : null;

            result.Add(new DocumentSnapshot(
                content.Key,
                content.Id,
                parentKey,
                content.Path,
                SnapshotBuilder.SortPath(sortOrders),
                content.Level,
                content.ContentType.Alias,
                cultures.Select(culture => BuildCulture(content, culture)).ToList()));
        }

        return result;
    }

    private static CultureSnapshot BuildCulture(IContent content, string culture)
    {
        if (!content.IsCultureAvailable(culture))
        {
            return new CultureSnapshot(culture, VariantBaseState.NotCreated, null, null, null, null);
        }

        // IsCulturePublished, GetPublishDate and GetPublishName moved to IPublishableContentBase in Umbraco 18: see the bound delegates.

        var name = content.GetCultureName(culture);
        var fingerprint = VariantFingerprint.Compute(name, Texts(content, culture, published: false));

        var state = VariantBaseState.Draft;
        if (IsCulturePublished(content, culture))
        {
            var publishedFingerprint = VariantFingerprint.Compute(GetPublishName(content, culture), Texts(content, culture, published: true));
            state = publishedFingerprint == fingerprint ? VariantBaseState.Published : VariantBaseState.PublishedPendingChanges;
        }

        return new CultureSnapshot(
            culture,
            state,
            ToUtc(content.GetUpdateDate(culture)),
            ToUtc(GetPublishDate(content, culture)),
            name,
            fingerprint);
    }

    private static IReadOnlyList<PropertyText> Texts(IContent content, string culture, bool published)
        => SnapshotBuilder.CultureTexts(
            content.Properties.Select(property => (
                property.Alias,
                (property.PropertyType.Variations & ContentVariation.Culture) != 0,
                (string?)property.PropertyType.PropertyEditorAlias,
                (Func<string?, object?>)(c => content.GetValue(property.Alias, c, null, published)))),
            culture);

    private static IEnumerable<int> PathIds(string path)
        => path.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.Parse(part, CultureInfo.InvariantCulture))
            .Where(id => id > 0);

    // Umbraco stores UTC; make sure the Kind says so whatever the provider returned.
    private static DateTime? ToUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } => value,
        { Kind: DateTimeKind.Local } => value.Value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
    };
}
