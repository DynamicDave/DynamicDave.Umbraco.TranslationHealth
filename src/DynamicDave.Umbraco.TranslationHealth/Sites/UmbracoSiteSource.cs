using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Infrastructure.Scoping;

namespace DynamicDave.Umbraco.TranslationHealth.Sites;

/// <summary>
/// Reads sites and their languages from Umbraco. Candidates are the top-level documents plus every document that
/// carries a domain (a nested root), because <c>IDomainService</c> cannot list all domains in Umbraco 18.
/// The result is cached for 60 seconds; <see cref="ClearCache"/> drops it.
/// </summary>
public sealed class UmbracoSiteSource(
    IDocumentNavigationQueryService navigation,
    IContentService contentService,
    IDomainService domainService,
    ILanguageService languageService,
    IScopeProvider scopeProvider,
    TimeProvider timeProvider) : ISiteSource
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private readonly object _lock = new();
    private Snapshot? _snapshot;
    private DateTimeOffset _loadedAt;

    public IReadOnlyList<SiteInfo> GetSites() => Current().Sites;

    public string DefaultCulture() => Current().DefaultCulture;

    public IReadOnlyList<string> AllCultures() => Current().AllCultures;

    public void ClearCache()
    {
        lock (_lock) _snapshot = null;
    }

    private Snapshot Current()
    {
        lock (_lock)
        {
            var now = timeProvider.GetUtcNow();
            if (_snapshot is null || now - _loadedAt >= CacheDuration)
            {
                _snapshot = Load();
                _loadedAt = now;
            }

            return _snapshot;
        }
    }

    // Sync over async: used from background jobs and notification handlers without a synchronization context.
    private Snapshot Load()
    {
        var languages = languageService.GetAllAsync().GetAwaiter().GetResult().ToList();
        var defaultCulture = languages.FirstOrDefault(l => l.IsDefault)?.IsoCode
            ?? languageService.GetDefaultIsoCodeAsync().GetAwaiter().GetResult();
        var allCultures = languages.Select(l => l.IsoCode).ToList();

        var candidates = new List<IContent>();
        var seen = new HashSet<int>();
        if (navigation.TryGetRootKeys(out var rootKeys))
        {
            foreach (var key in rootKeys)
            {
                if (contentService.GetById(key) is { Trashed: false } root && seen.Add(root.Id)) candidates.Add(root);
            }
        }

        foreach (var id in DomainRootIds())
        {
            if (seen.Contains(id)) continue;
            if (contentService.GetById(id) is { Trashed: false } root && seen.Add(root.Id)) candidates.Add(root);
        }

        var sites = new List<SiteInfo>();
        foreach (var root in candidates)
        {
            // Wildcard domains carry no host but set the site's culture, so they are included.
            var cultures = domainService.GetAssignedDomainsAsync(root.Key, true).GetAwaiter().GetResult()
                .Select(d => d.LanguageIsoCode)
                .OfType<string>()
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var hasDomains = cultures.Count > 0;
            var missingDefault = hasDomains && !cultures.Contains(defaultCulture, StringComparer.OrdinalIgnoreCase);
            sites.Add(new SiteInfo(root.Key, root.Id, NameOf(root, defaultCulture), cultures, hasDomains, missingDefault));
        }

        return new Snapshot(sites, defaultCulture, allCultures);
    }

    private List<int> DomainRootIds()
    {
        using var scope = scopeProvider.CreateScope(autoComplete: true);
        return scope.Database.Fetch<int>(
            "SELECT DISTINCT domainRootStructureID FROM umbracoDomain WHERE domainRootStructureID IS NOT NULL");
    }

    private static string NameOf(IContent root, string defaultCulture)
    {
        var names = root.CultureInfos?.Values.ToList() ?? [];
        var name = names.FirstOrDefault(n => string.Equals(n.Culture, defaultCulture, StringComparison.OrdinalIgnoreCase))?.Name
            ?? names.Select(n => n.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
        return name ?? root.Name ?? string.Empty;
    }

    private sealed record Snapshot(IReadOnlyList<SiteInfo> Sites, string DefaultCulture, IReadOnlyList<string> AllCultures);
}
