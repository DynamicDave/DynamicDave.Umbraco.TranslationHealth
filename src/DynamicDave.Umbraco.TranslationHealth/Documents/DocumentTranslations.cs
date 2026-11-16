using DynamicDave.Umbraco.TranslationHealth.Indexing;
using DynamicDave.Umbraco.TranslationHealth.Persistence;
using DynamicDave.Umbraco.TranslationHealth.Sites;
using DynamicDave.Umbraco.TranslationHealth.Status;
using Umbraco.Cms.Core.Services;

namespace DynamicDave.Umbraco.TranslationHealth.Documents;

public record DocumentTranslation(string Culture, string LanguageName, bool IsDefault, VariantRow Row, string? MarkedByName);

/// <summary>The "Translations" tab of a document: its indexed rows with language names and the name of whoever marked a language up to date.</summary>
public sealed class DocumentTranslations(
    ITranslationStatusStore store,
    Reindexer reindexer,
    ReindexQueue queue,
    ISiteSource sites,
    ILanguageService languageService,
    IUserService userService)
{
    /// <summary>
    /// The languages of the document in Umbraco's language order with the default language first (not applicable ones
    /// included). Documents without rows, and documents still waiting in the <see cref="ReindexQueue"/> (the tab reloads
    /// right after a save, before the worker gets to it), are indexed first; null when there are still no rows (the
    /// document is not culture-variant, is excluded or does not exist). A queued key stays queued: the worker indexes it
    /// again, which changes nothing.
    /// </summary>
    public IReadOnlyList<DocumentTranslation>? Get(Guid documentKey)
    {
        var rows = store.GetVariants(documentKey);
        if (rows.Count == 0 || queue.Contains(documentKey))
        {
            reindexer.Reindex(documentKey, IndexMode.Save);
            rows = store.GetVariants(documentKey);
        }

        if (rows.Count == 0)
        {
            return null;
        }

        var defaultCulture = sites.DefaultCulture();
        var languages = languageService.GetAllAsync().GetAwaiter().GetResult().ToList();
        var userNames = new Dictionary<Guid, string?>();

        return rows
            .Select(row =>
            {
                var language = languages.FirstOrDefault(l => Same(l.IsoCode, row.Culture));
                var isDefault = Same(row.Culture, defaultCulture);
                return new
                {
                    Order = isDefault ? -1 : language is null ? int.MaxValue : languages.IndexOf(language),
                    Translation = new DocumentTranslation(
                        row.Culture,
                        language?.CultureName ?? row.Culture,
                        isDefault,
                        row,
                        row.MarkedByUserKey is { } userKey ? UserName(userKey, userNames) : null),
                };
            })
            .OrderBy(item => item.Order)
            .Select(item => item.Translation)
            .ToList();
    }

    // A deleted user has no name: the tab then shows only the date.
    private string? UserName(Guid userKey, Dictionary<Guid, string?> cache)
    {
        if (!cache.TryGetValue(userKey, out var name))
        {
            name = userService.GetAsync(userKey).GetAwaiter().GetResult()?.Name;
            cache[userKey] = name;
        }

        return name;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
