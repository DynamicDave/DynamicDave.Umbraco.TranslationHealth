using DynamicDave.Umbraco.TranslationHealth.Security;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Strings;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class AccessPolicyTests
{
    private const int English = 1;
    private const int Dutch = 2;
    private const int German = 3;

    private static readonly IShortStringHelper Helper = new DefaultShortStringHelper(Options.Create(new RequestHandlerSettings()));

    private static UserGroup Group(bool all, params int[] allowed)
    {
        var group = new UserGroup(Helper) { HasAccessToAllLanguages = all };
        foreach (var id in allowed)
        {
            group.AddAllowedLanguage(id);
        }

        return group;
    }

    private static ILanguage Language(int id, string iso) => new Language(iso, iso) { Id = id };

    [Fact]
    public void Group_with_all_languages_can_edit_any()
    {
        Assert.True(AccessPolicy.CanEditLanguage([Group(true)], German));
    }

    [Fact]
    public void Group_limited_to_nl_cannot_edit_de()
    {
        var groups = new[] { Group(false, Dutch) };

        Assert.True(AccessPolicy.CanEditLanguage(groups, Dutch));
        Assert.False(AccessPolicy.CanEditLanguage(groups, German));
    }

    [Fact]
    public void Any_group_granting_is_enough()
    {
        var groups = new[] { Group(false, Dutch), Group(false, German) };

        Assert.True(AccessPolicy.CanEditLanguage(groups, German));
        Assert.False(AccessPolicy.CanEditLanguage(groups, English));
    }

    [Fact]
    public void Group_without_all_languages_and_without_a_list_grants_nothing()
    {
        // Umbraco: HasAccessToAllLanguages || AllowedLanguages.Contains(id); an empty list does not mean all.
        Assert.False(AccessPolicy.CanEditLanguage([Group(false)], English));
        Assert.False(AccessPolicy.CanEditLanguage([], English));
    }

    [Fact]
    public void EditableCultures_lists_only_allowed()
    {
        var languages = new[] { Language(English, "en-US"), Language(Dutch, "nl-NL"), Language(German, "de-DE") };

        var cultures = AccessPolicy.EditableCultures([Group(false, Dutch)], languages);

        Assert.Equal(["nl-NL"], cultures);
        Assert.Contains("NL-nl", cultures);
        Assert.Equal(3, AccessPolicy.EditableCultures([Group(true)], languages).Count);
    }
}
