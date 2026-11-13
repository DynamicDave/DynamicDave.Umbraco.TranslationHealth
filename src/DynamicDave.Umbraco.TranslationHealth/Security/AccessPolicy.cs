using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Membership;

namespace DynamicDave.Umbraco.TranslationHealth.Security;

/// <summary>
/// Language permissions of a user, decided by Umbraco's own rule <c>IReadOnlyUserGroup.HasAccessToLanguage</c>
/// (Umbraco.Core/Models/Membership/IReadOnlyUserGroup.cs: <c>HasAccessToAllLanguages || AllowedLanguages.Contains(id)</c>;
/// <c>UserExtensions.CalculateAllowedLanguageIds</c> combines groups the same way). A group that does not have access
/// to all languages and lists none grants nothing. The member is on the same interface in Umbraco 17 and 18.
/// </summary>
public static class AccessPolicy
{
    public static bool CanEditLanguage(IEnumerable<IReadOnlyUserGroup> groups, int languageId) =>
        groups.Any(group => group.HasAccessToLanguage(languageId));

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
