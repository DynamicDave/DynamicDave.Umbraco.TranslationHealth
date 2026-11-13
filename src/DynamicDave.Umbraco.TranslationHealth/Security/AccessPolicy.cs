using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Membership;

namespace DynamicDave.Umbraco.TranslationHealth.Security;

/// <summary>
/// Language permissions of a user, decided like Umbraco does (<c>IReadOnlyUserGroup.HasAccessToLanguage</c> and
/// <c>UserExtensions.CalculateAllowedLanguageIds</c>): a group grants a language when it has access to all languages or
/// lists the language. A group that does not have access to all languages and lists none grants nothing.
/// </summary>
public static class AccessPolicy
{
    public static bool CanEditLanguage(IEnumerable<IReadOnlyUserGroup> groups, int languageId) =>
        groups.Any(group => group.HasAccessToAllLanguages || group.AllowedLanguages.Contains(languageId));

    /// <summary>The cultures (ISO codes, case-insensitive) of <paramref name="languages"/> the groups may edit.</summary>
    public static IReadOnlySet<string> EditableCultures(IEnumerable<IReadOnlyUserGroup> groups, IEnumerable<ILanguage> languages)
    {
        var groupList = groups.ToList();
        return languages
            .Where(language => CanEditLanguage(groupList, language.Id))
            .Select(language => language.IsoCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
