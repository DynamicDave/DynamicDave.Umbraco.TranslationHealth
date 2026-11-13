using System.Reflection;
using DynamicDave.Umbraco.TranslationHealth.Content;
using DynamicDave.Umbraco.TranslationHealth.Documents;
using DynamicDave.Umbraco.TranslationHealth.Indexing;
using DynamicDave.Umbraco.TranslationHealth.Persistence;
using DynamicDave.Umbraco.TranslationHealth.Status;
using DynamicDave.Umbraco.TranslationHealth.Tests.Fakes;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class DocumentTranslationsTests
{
    private static readonly DateTime Now = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeSnapshotSource _source = new();
    private readonly FakeSiteSource _sites = new() { Default = "nl-NL" };
    private readonly FakeStatusStore _store = new();
    private readonly Services _services = new();
    private readonly DocumentTranslations _translations;

    public DocumentTranslationsTests()
    {
        _sites.Cultures.Clear();
        _sites.Cultures.AddRange(["en-US", "nl-NL", "de-DE"]);
        var reindexer = new Reindexer(_source, _sites, _store, new ManualTimeProvider(new DateTimeOffset(Now)));
        _translations = new DocumentTranslations(_store, reindexer, _sites, _services.Languages, _services.Users);
    }

    private static VariantRow Row(Guid key, string culture, CellStatus status, Guid? markedBy = null) => new(
        key, culture, status, false, TrackingMode.Fingerprint, Now, null, null, null, null, null, markedBy, markedBy is null ? null : Now, null);

    private void Seed(Guid key, params VariantRow[] rows) => _store.Save(new IndexResult(
        new DocumentRow(key, 1, null, "-1,1", "00001", 1, "page", Guid.Empty, "Doc"), rows));

    [Fact]
    public void Get_reindexes_when_rows_are_missing()
    {
        var key = Guid.NewGuid();
        _source.Add(FakeSnapshotSource.Doc(
            key,
            1,
            new CultureSnapshot("en-US", VariantBaseState.Published, Now, Now, "Home", "a"),
            new CultureSnapshot("nl-NL", VariantBaseState.Draft, Now, null, "Thuis", "b")));

        var result = _translations.Get(key);

        Assert.NotNull(result);
        Assert.Contains(result, t => t.Culture == "en-US" && t.Row.Status == CellStatus.Published);
        Assert.NotEmpty(_store.GetVariants(key));
    }

    [Fact]
    public void Get_returns_null_when_there_are_no_rows_after_the_reindex()
    {
        Assert.Null(_translations.Get(Guid.NewGuid()));
    }

    [Fact]
    public void Default_language_is_listed_first_and_the_rest_keeps_the_core_order()
    {
        var key = Guid.NewGuid();
        Seed(key, Row(key, "de-DE", CellStatus.Missing), Row(key, "nl-NL", CellStatus.Published), Row(key, "en-US", CellStatus.NotApplicable));

        var result = _translations.Get(key)!;

        Assert.Equal(["nl-NL", "en-US", "de-DE"], result.Select(t => t.Culture));
        Assert.True(result[0].IsDefault);
        Assert.All(result.Skip(1), t => Assert.False(t.IsDefault));
        Assert.Equal("Dutch", result[0].LanguageName);
        Assert.Contains(result, t => t.Row.Status == CellStatus.NotApplicable);
    }

    [Fact]
    public void Marked_by_name_comes_from_the_user_service_and_is_null_for_a_deleted_user()
    {
        var key = Guid.NewGuid();
        var known = Guid.NewGuid();
        var deleted = Guid.NewGuid();
        _services.UserNames[known] = "Jane Editor";
        Seed(key, Row(key, "nl-NL", CellStatus.Published), Row(key, "en-US", CellStatus.Published, known), Row(key, "de-DE", CellStatus.Published, deleted));

        var result = _translations.Get(key)!;

        Assert.Null(result.Single(t => t.Culture == "nl-NL").MarkedByName);
        Assert.Equal("Jane Editor", result.Single(t => t.Culture == "en-US").MarkedByName);
        Assert.Null(result.Single(t => t.Culture == "de-DE").MarkedByName);
    }

    [Fact]
    public void Row_culture_casing_may_differ_from_the_language()
    {
        var key = Guid.NewGuid();
        Seed(key, Row(key, "nl-nl", CellStatus.Published), Row(key, "EN-us", CellStatus.Draft));

        var result = _translations.Get(key)!;

        Assert.True(result[0].IsDefault);
        Assert.Equal("Dutch", result[0].LanguageName);
        Assert.Equal("English", result[1].LanguageName);
    }

    [Fact]
    public void Unknown_culture_is_listed_last_with_its_code_as_name()
    {
        var key = Guid.NewGuid();
        Seed(key, Row(key, "fr-FR", CellStatus.Draft), Row(key, "de-DE", CellStatus.Missing), Row(key, "nl-NL", CellStatus.Published));

        var result = _translations.Get(key)!;

        Assert.Equal(["nl-NL", "de-DE", "fr-FR"], result.Select(t => t.Culture));
        Assert.Equal("fr-FR", result[2].LanguageName);
    }

    [Fact]
    public void Default_language_is_first_even_when_it_is_last_in_core_order()
    {
        _sites.Default = "de-DE";
        var key = Guid.NewGuid();
        Seed(key, Row(key, "en-US", CellStatus.Published), Row(key, "nl-NL", CellStatus.Published), Row(key, "de-DE", CellStatus.Published));

        Assert.Equal(["de-DE", "en-US", "nl-NL"], _translations.Get(key)!.Select(t => t.Culture));
    }

    /// <summary>The Umbraco services the class reads, as proxies that answer only the calls it makes (the interfaces are large and differ per version).</summary>
    private sealed class Services
    {
        public Dictionary<Guid, string> UserNames { get; } = [];

        public ILanguageService Languages { get; } = Proxy<ILanguageService>.Create(
            (name, _) => name == "GetAllAsync"
                ? Task.FromResult<IEnumerable<ILanguage>>(
                [
                    new Language("en-US", "English") { Id = 1 },
                    new Language("nl-NL", "Dutch") { Id = 2 },
                    new Language("de-DE", "German") { Id = 3 },
                ])
                : throw new NotSupportedException(name));

        public IUserService Users { get; }

        public Services()
        {
            Users = Proxy<IUserService>.Create((name, args) =>
            {
                if (name != "GetAsync" || args is not [Guid key])
                {
                    throw new NotSupportedException(name);
                }

                return Task.FromResult<IUser?>(
                    UserNames.TryGetValue(key, out var userName) ? new User(new GlobalSettings()) { Name = userName } : null);
            });
        }
    }

    private class Proxy<T> : DispatchProxy
        where T : class
    {
        private Func<string, object?[]?, object?> _handler = (name, _) => throw new NotSupportedException(name);

        public static T Create(Func<string, object?[]?, object?> handler)
        {
            var proxy = DispatchProxy.Create<T, Proxy<T>>();
            ((Proxy<T>)(object)proxy)._handler = handler;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => _handler(targetMethod!.Name, args);
    }
}
