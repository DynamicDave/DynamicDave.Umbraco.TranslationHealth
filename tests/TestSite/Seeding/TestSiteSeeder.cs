using System.Text.Json;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.ContentEditing;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Extensions;

namespace TestSite.Seeding;

/// <summary>
/// Seeds the translation health test data: four languages (en-US default, nl-NL, de-DE, fr-FR), the sites "Corporate"
/// (en/nl/de domains), "Webshop" (de/fr domains, no en-US) and "Plain" (no domains), 66 pages each in a mix of
/// translation states, and a dictionary tree. Set TH_SEED_LARGE to a number to add an extra root "Load" with that many pages.
/// </summary>
public class TestSiteSeeder(
    IRuntimeState runtimeState,
    IContentService contentService,
    IContentTypeService contentTypeService,
    ILanguageService languageService,
    IDataTypeService dataTypeService,
    IDomainService domainService,
    IDictionaryItemService dictionaryItemService,
    PropertyEditorCollection propertyEditors,
    IConfigurationEditorJsonSerializer configurationSerializer,
    IShortStringHelper shortStringHelper,
    ILogger<TestSiteSeeder> logger)
    : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private static readonly Guid SuperUser = Constants.Security.SuperUserKey;
    private static readonly string[] Cultures = ["en-US", "nl-NL", "de-DE", "fr-FR"];

    private IContentType _page = null!;
    private IContentType _textBlock = null!;
    private IContentType _settings = null!;

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        if (runtimeState.Level != RuntimeLevel.Run || contentService.GetRootContent().Any())
        {
            return; // not ready, or already seeded
        }

        logger.LogInformation("Seeding translation health test data");
        await EnsureLanguagesAsync();
        await CreateContentTypesAsync();

        await SeedSiteAsync("Corporate", ("corporate-en.localhost:44417", "en-US"), ("corporate-nl.localhost:44417", "nl-NL"), ("corporate-de.localhost:44417", "de-DE"));
        await SeedSiteAsync("Webshop", ("webshop-de.localhost:44417", "de-DE"), ("webshop-fr.localhost:44417", "fr-FR"));
        await SeedSiteAsync("Plain");

        var settings = contentService.Create("Settings", Constants.System.Root, _settings);
        contentService.Save(settings);
        Publish(settings, "*");

        await SeedDictionaryAsync();

        if (int.TryParse(Environment.GetEnvironmentVariable("TH_SEED_LARGE"), out var large) && large > 0)
        {
            SeedLarge(large);
        }

        logger.LogInformation("Seeded translation health test data");
    }

    private async Task EnsureLanguagesAsync()
    {
        foreach (var (iso, name) in new[] { ("nl-NL", "Dutch (Netherlands)"), ("de-DE", "German (Germany)"), ("fr-FR", "French (France)") })
        {
            if (await languageService.GetAsync(iso) is null)
            {
                LogIfFailed($"Create language {iso}", await languageService.CreateAsync(new Language(iso, name), SuperUser));
            }
        }
    }

    private async Task CreateContentTypesAsync()
    {
        var textstring = await RequiredDataTypeAsync(Constants.DataTypes.Guids.TextstringGuid);
        var richText = await RequiredDataTypeAsync(Constants.DataTypes.Guids.RichtextEditorGuid);

        var textBlock = new ContentType(shortStringHelper, Constants.System.Root)
        {
            Alias = "textBlock",
            Name = "Text block",
            IsElement = true,
            Variations = ContentVariation.Culture,
        };
        textBlock.AddPropertyType(Property(textstring, "headline", "Headline", ContentVariation.Culture), "content", "Content");
        textBlock.AddPropertyType(Property(textstring, "note", "Note"), "content", "Content");
        await CreateContentTypeAsync(textBlock);
        _textBlock = textBlock;

        var blocks = await CreateDataTypeAsync("Blocks", Constants.PropertyEditors.Aliases.BlockList, new Dictionary<string, object>
        {
            ["blocks"] = new[] { new BlockListConfiguration.BlockConfiguration { ContentElementTypeKey = textBlock.Key } },
        });

        var page = new ContentType(shortStringHelper, Constants.System.Root)
        {
            Alias = "translatedPage",
            Name = "Translated page",
            AllowedAsRoot = true,
            Variations = ContentVariation.Culture,
        };
        page.AddPropertyType(Property(textstring, "title", "Title", ContentVariation.Culture), "content", "Content");
        page.AddPropertyType(Property(richText, "body", "Body", ContentVariation.Culture), "content", "Content");
        page.AddPropertyType(Property(blocks, "blocks", "Blocks"), "content", "Content"); // invariant, the block element varies
        page.AddPropertyType(Property(textstring, "code", "Code"), "content", "Content");
        page.AllowedContentTypes = [new ContentTypeSort(page.Key, 0, page.Alias)];
        await CreateContentTypeAsync(page);
        _page = page;

        var settings = new ContentType(shortStringHelper, Constants.System.Root)
        {
            Alias = "settingsNode",
            Name = "Settings node",
            AllowedAsRoot = true,
        };
        settings.AddPropertyType(Property(textstring, "siteName", "Site name"), "content", "Content");
        await CreateContentTypeAsync(settings);
        _settings = settings;
    }

    private async Task SeedSiteAsync(string name, params (string Domain, string Culture)[] domains)
    {
        var index = 0;
        var root = CreatePage(name, parent: null, index++);
        for (var s = 1; s <= 5; s++)
        {
            var section = CreatePage($"{name} section {s}", root, index++);
            for (var c = 1; c <= 3; c++)
            {
                var child = CreatePage($"{name} page {s}.{c}", section, index++);
                for (var g = 1; g <= 3; g++)
                {
                    CreatePage($"{name} item {s}.{c}.{g}", child, index++);
                }
            }
        }

        if (domains.Length > 0)
        {
            var model = new DomainsUpdateModel
            {
                DefaultIsoCode = domains[0].Culture,
                Domains = domains.Select(d => new DomainModel { DomainName = d.Domain, IsoCode = d.Culture }).ToArray(),
            };
            LogIfFailed($"Assign domains of {name}", await domainService.UpdateDomainsAsync(root.Key, model));
        }
    }

    /// <summary>
    /// Page state mix by running index (modulo 12): 1 nl missing, 2 de draft, 3 fr published then en changed later (outdated by date),
    /// 4 de and fr missing, 5 en-only block headline; everything else fully published.
    /// </summary>
    private IContent CreatePage(string name, IContent? parent, int index)
    {
        var state = index % 12;
        var content = contentService.Create(name, parent?.Id ?? Constants.System.Root, _page);
        var present = state switch
        {
            1 => Cultures.Where(c => c != "nl-NL").ToArray(),
            4 => ["en-US", "nl-NL"],
            _ => Cultures,
        };

        foreach (var culture in present)
        {
            content.SetCultureName(Localized(name, culture), culture);
            content.SetValue("title", Localized(name, culture), culture);
            content.SetValue("body", RichText(Localized(name, culture)), culture);
        }

        content.SetValue("code", $"P{index:000}");
        // The published block value only keeps published cultures, so a draft culture must not have a block value
        // or the invariant property would flag the default culture as edited.
        var blockCultures = state switch
        {
            5 => ["en-US"],
            2 => present.Where(c => c != "de-DE").ToArray(),
            _ => present,
        };
        content.SetValue("blocks", BlockList(blockCultures, name));
        contentService.Save(content);

        switch (state)
        {
            case 2:
                // de stays a draft: it is saved above but never published
                Publish(content, present.Where(c => c != "de-DE").ToArray());
                break;
            case 3:
                Publish(content, present);
                Thread.Sleep(60);
                content.SetValue("title", name + " (changed in English)", "en-US");
                content.SetValue("body", RichText(name + " changed"), "en-US");
                contentService.Save(content); // en now has pending changes and a newer update date than fr
                break;
            default:
                Publish(content, present);
                break;
        }

        return content;
    }

    private static string Localized(string name, string culture) => culture switch
    {
        "nl-NL" => name + " (NL)",
        "de-DE" => name + " (DE)",
        "fr-FR" => name + " (FR)",
        _ => name,
    };

    private static string RichText(string text) => JsonSerializer.Serialize(new { markup = $"<p>{text}</p>", blocks = (object?)null });

    // Block list value as stored by Umbraco 15+: the element varies by culture inside an invariant property, so the
    // headline values and the expose entries carry a culture, the invariant note does not.
    private string BlockList(IReadOnlyCollection<string> cultures, string headline)
    {
        // The property order and value order below are exactly what Umbraco writes on publish. A different (raw) form
        // makes the draft differ from the published value, which flags the invariant block property as edited.
        var contentKey = Guid.NewGuid();
        var editorAlias = Constants.PropertyEditors.Aliases.TextBox;
        return JsonSerializer.Serialize(new
        {
            contentData = new[]
            {
                new
                {
                    contentTypeKey = _textBlock.Key,
                    key = contentKey,
                    values = new[] { new { editorAlias, culture = (string?)null, segment = (string?)null, alias = "note", value = "Same in every language" } }
                        .Concat(cultures.OrderBy(c => c, StringComparer.Ordinal)
                            .Select(c => new { editorAlias, culture = (string?)c, segment = (string?)null, alias = "headline", value = Localized(headline, c) }))
                        .ToArray(),
                },
            },
            settingsData = Array.Empty<object>(),
            expose = cultures.Select(c => new { contentKey, culture = (string?)c, segment = (string?)null }).ToArray(),
            layout = new Dictionary<string, object>
            {
                ["Umbraco.BlockList"] = new[] { new { contentUdi = (string?)null, settingsUdi = (string?)null, contentKey, settingsKey = (Guid?)null } },
            },
        });
    }

    private async Task SeedDictionaryAsync()
    {
        var languages = (await languageService.GetAllAsync()).ToDictionary(l => l.IsoCode);

        async Task<IDictionaryItem> Add(string key, Guid? parent, params (string Culture, string Value)[] values)
        {
            var item = new DictionaryItem(parent, key)
            {
                Translations = values.Select(v => (IDictionaryTranslation)new DictionaryTranslation(languages[v.Culture], v.Value)).ToList(),
            };
            var result = await dictionaryItemService.CreateAsync(item, SuperUser);
            if (!result.Success)
            {
                logger.LogWarning("Create dictionary item {Key} failed: {Status}", key, result.Status);
            }

            return item;
        }

        var footer = await Add("Footer", null, ("en-US", "Footer"), ("nl-NL", "Voettekst"), ("de-DE", "Fußzeile"), ("fr-FR", "Pied de page"));
        var contact = await Add("Footer.Contact", footer.Key, ("en-US", "Contact"), ("nl-NL", "Contact"), ("de-DE", "Kontakt"));
        await Add("Footer.Contact.Phone", contact.Key, ("en-US", "Phone"), ("nl-NL", "Telefoon"));
        await Add("Footer.Contact.Email", contact.Key, ("en-US", "Email"), ("nl-NL", "E-mail"), ("de-DE", "E-Mail"), ("fr-FR", "E-mail"));

        await Add("Forms.Submit", null, ("en-US", "Submit"), ("nl-NL", "Verzenden"), ("fr-FR", "Envoyer"));
        await Add("Forms.Required", null, ("en-US", "This field is required"), ("de-DE", "Dieses Feld ist erforderlich"));

        var buttons = await Add("Buttons", null, ("en-US", "Buttons"));
        await Add("ReadMore", buttons.Key, ("en-US", "Read more"), ("nl-NL", "Lees meer"), ("de-DE", "Weiterlesen"), ("fr-FR", "Lire la suite"));
        await Add("Greeting", null, ("en-US", "Hello <b>{0}</b> & \"you\""), ("nl-NL", "Hallo <b>{0}</b> & \"jij\""));
    }

    private void SeedLarge(int count)
    {
        logger.LogInformation("Seeding {Count} load pages", count);
        var load = contentService.Create("Load", Constants.System.Root, _page);
        foreach (var culture in Cultures)
        {
            load.SetCultureName("Load" + (culture == "en-US" ? string.Empty : " " + culture), culture);
        }

        contentService.Save(load);
        Publish(load, Cultures);

        for (var i = 1; i <= count; i++)
        {
            var page = contentService.Create($"Load {i}", load.Id, _page);
            foreach (var culture in Cultures)
            {
                page.SetCultureName($"Load {i} {culture}", culture);
                page.SetValue("title", $"Load {i} {culture}", culture);
            }

            page.SetValue("code", $"L{i}");
            contentService.Save(page);
            if (i % 4 != 0)
            {
                Publish(page, Cultures);
            }

            if (i % 500 == 0)
            {
                logger.LogInformation("Seeded {Done} of {Count} load pages", i, count);
            }
        }
    }

    private PropertyType Property(IDataType dataType, string alias, string name, ContentVariation variations = ContentVariation.Nothing)
        => new(shortStringHelper, dataType, alias) { Name = name, Variations = variations };

    private async Task<IDataType> RequiredDataTypeAsync(Guid key)
        => await dataTypeService.GetAsync(key) ?? throw new InvalidOperationException($"Default data type {key} not found");

    private async Task<IDataType> CreateDataTypeAsync(string name, string editorAlias, IDictionary<string, object> configuration)
    {
        if (!propertyEditors.TryGet(editorAlias, out var editor))
        {
            throw new InvalidOperationException($"Property editor {editorAlias} not found");
        }

        var dataType = new DataType(editor, configurationSerializer, Constants.System.Root)
        {
            Name = name,
            DatabaseType = ValueStorageType.Ntext,
            ConfigurationData = configuration,
        };
        LogIfFailed($"Create data type {name}", await dataTypeService.CreateAsync(dataType, SuperUser));
        return dataType;
    }

    private async Task CreateContentTypeAsync(IContentType type)
        => LogIfFailed($"Create content type {type.Alias}", await contentTypeService.CreateAsync(type, SuperUser));

    private void Publish(IContent content, params string[] cultures)
    {
        var result = contentService.Publish(content, cultures);
        if (!result.Success)
        {
            logger.LogWarning("Publishing {Name} failed: {Result}", content.Name, result.Result);
        }
    }

    private void LogIfFailed(string action, dynamic attempt)
    {
        if (!attempt.Success)
        {
            logger.LogWarning("{Action} failed: {Status}", action, (object?)attempt.Status);
        }
    }
}
