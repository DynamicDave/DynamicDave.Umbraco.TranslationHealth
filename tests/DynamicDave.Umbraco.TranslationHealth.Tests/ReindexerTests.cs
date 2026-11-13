using DynamicDave.Umbraco.TranslationHealth.Content;
using DynamicDave.Umbraco.TranslationHealth.Indexing;
using DynamicDave.Umbraco.TranslationHealth.Sites;
using DynamicDave.Umbraco.TranslationHealth.Status;
using DynamicDave.Umbraco.TranslationHealth.Tests.Fakes;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class ReindexerTests
{
    private const string En = "en-US";
    private const string Nl = "nl-NL";
    private static readonly DateTimeOffset Start = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly FakeSnapshotSource _source = new();
    private readonly FakeSiteSource _sites = new();
    private readonly FakeStatusStore _store = new();
    private readonly ManualTimeProvider _time = new(Start);
    private readonly Reindexer _reindexer;

    public ReindexerTests() => _reindexer = new Reindexer(_source, _sites, _store, _time);

    public static CultureSnapshot C(string culture, VariantBaseState state, string? fingerprint) =>
        new(culture, state, state == VariantBaseState.NotCreated ? null : T0, null, $"{culture} name", fingerprint);

    private DocumentSnapshot AddDocument(Guid key, int id, string en, string? nl)
    {
        var document = FakeSnapshotSource.Doc(
            key,
            id,
            C(En, VariantBaseState.Published, en),
            nl is null ? C(Nl, VariantBaseState.NotCreated, null) : C(Nl, VariantBaseState.Published, nl));
        _source.Add(document);
        return document;
    }

    [Fact]
    public void Reindex_saves_rows_for_variant_document()
    {
        var key = Guid.NewGuid();
        AddDocument(key, 1, "a", "b");

        _reindexer.Reindex(key, IndexMode.Save);

        Assert.Equal(key, _store.GetDocument(key)!.Key);
        Assert.Equal(["en-US", "nl-NL"], _store.GetVariants(key).Select(v => v.Culture).Order());
        Assert.All(_store.GetVariants(key), v => Assert.Equal(CellStatus.Published, v.Status));
        Assert.Equal("en-US name", _store.GetDocument(key)!.Name);
    }

    [Fact]
    public void Reindex_removes_rows_when_document_is_gone()
    {
        var key = Guid.NewGuid();
        AddDocument(key, 1, "a", "b");
        _reindexer.Reindex(key, IndexMode.Save);

        _source.Remove(key);
        _reindexer.Reindex(key, IndexMode.Save);

        Assert.Null(_store.GetDocument(key));
        Assert.Empty(_store.GetVariants(key));
    }

    [Fact]
    public void Remove_deletes_the_rows()
    {
        var key = Guid.NewGuid();
        AddDocument(key, 1, "a", "b");
        _reindexer.Reindex(key, IndexMode.Save);

        _reindexer.Remove(key);

        Assert.Null(_store.GetDocument(key));
    }

    [Fact]
    public void Reindex_marks_cultures_outside_site_not_applicable()
    {
        var root = Guid.NewGuid();
        var key = Guid.NewGuid();
        _sites.Sites.Add(new SiteInfo(root, 1, "Site", [En], true, false));
        _source.Add(new DocumentSnapshot(
            key, 2, root, "-1,1,2", "00001.00001", 2, "page",
            [C(En, VariantBaseState.Published, "a"), C(Nl, VariantBaseState.Published, "b")]));

        _reindexer.Reindex(key, IndexMode.Save);

        Assert.Equal(CellStatus.NotApplicable, _store.GetVariants(key).Single(v => v.Culture == Nl).Status);
        Assert.Equal(CellStatus.Published, _store.GetVariants(key).Single(v => v.Culture == En).Status);
        Assert.Equal(root, _store.GetDocument(key)!.SiteRootKey);
    }

    [Fact]
    public void Reindex_of_a_document_without_a_site_uses_all_languages_and_no_site_key()
    {
        var key = Guid.NewGuid();
        AddDocument(key, 7, "a", "b");

        _reindexer.Reindex(key, IndexMode.Save);

        Assert.Equal(Guid.Empty, _store.GetDocument(key)!.SiteRootKey);
        Assert.All(_store.GetVariants(key), v => Assert.NotEqual(CellStatus.NotApplicable, v.Status));
    }

    [Fact]
    public void A_text_change_of_the_default_language_makes_a_translation_with_a_baseline_outdated()
    {
        var key = Guid.NewGuid();
        AddDocument(key, 1, "a", "b");
        _reindexer.Reindex(key, IndexMode.Save);

        // The translation is saved with new text: its baseline becomes the current default text.
        _source.Replace(FakeSnapshotSource.Doc(key, 1, C(En, VariantBaseState.Published, "a"), C(Nl, VariantBaseState.Published, "b2")));
        _reindexer.Reindex(key, IndexMode.Save);

        _source.Replace(FakeSnapshotSource.Doc(key, 1, C(En, VariantBaseState.Published, "a2"), C(Nl, VariantBaseState.Published, "b2")));
        _reindexer.Reindex(key, IndexMode.Save);

        var nl = _store.GetVariants(key).Single(v => v.Culture == Nl);
        Assert.True(nl.Outdated);
        Assert.Equal(TrackingMode.Fingerprint, nl.Mode);
        Assert.Equal(Start.UtcDateTime, nl.OutdatedSinceUtc);
    }

    [Fact]
    public void Mark_up_to_date_saves_the_marked_row()
    {
        var key = Guid.NewGuid();
        var user = Guid.NewGuid();
        AddDocument(key, 1, "a", "b");
        _reindexer.Reindex(key, IndexMode.Save);

        var marked = _reindexer.MarkUpToDate(key, "nl-nl", user);

        Assert.Equal("a", marked.BaselineFingerprint);
        Assert.Equal(BaselineSource.MarkedUpToDate, marked.BaselineSource);
        Assert.Equal(user, marked.MarkedByUserKey);
        Assert.Equal(Start.UtcDateTime, marked.MarkedUtc);
        Assert.False(marked.Outdated);
        Assert.Equal(marked, _store.GetVariants(key).Single(v => v.Culture == Nl));
    }

    [Fact]
    public void Mark_up_to_date_throws_when_the_row_cannot_be_marked()
    {
        var key = Guid.NewGuid();
        AddDocument(key, 1, "a", null);
        _reindexer.Reindex(key, IndexMode.Save);

        Assert.Throws<InvalidOperationException>(() => _reindexer.MarkUpToDate(key, Nl, Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => _reindexer.MarkUpToDate(key, En, Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => _reindexer.MarkUpToDate(key, "de-DE", Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => _reindexer.MarkUpToDate(Guid.NewGuid(), Nl, Guid.NewGuid()));
    }

    [Fact]
    public void Writes_of_one_reindexer_do_not_interleave()
    {
        var key = Guid.NewGuid();
        AddDocument(key, 1, "a", "b");
        _reindexer.Reindex(key, IndexMode.Save);

        var entered = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        _store.OnSave = () =>
        {
            _store.OnSave = null;
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        };

        var reindex = Task.Run(() => _reindexer.Reindex(key, IndexMode.Save));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        var mark = Task.Run(() => _reindexer.MarkUpToDate(key, Nl, Guid.NewGuid()));

        Assert.False(mark.Wait(TimeSpan.FromMilliseconds(300)));
        release.Set();
        Assert.True(Task.WaitAll([reindex, mark], TimeSpan.FromSeconds(10)));

        // The mark ran after the save and its baseline survives the next save of the same text.
        _reindexer.Reindex(key, IndexMode.Save);
        Assert.Equal(BaselineSource.MarkedUpToDate, _store.GetVariants(key).Single(v => v.Culture == Nl).BaselineSource);
    }
}
