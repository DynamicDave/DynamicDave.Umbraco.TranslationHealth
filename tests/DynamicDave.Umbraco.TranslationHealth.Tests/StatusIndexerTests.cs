using DynamicDave.Umbraco.TranslationHealth.Content;
using DynamicDave.Umbraco.TranslationHealth.Status;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class StatusIndexerTests
{
    private const string En = "en-US";
    private const string Nl = "nl-NL";
    private static readonly Guid DocKey = Guid.NewGuid();
    private static readonly Guid Site = Guid.NewGuid();
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private static CultureSnapshot C(string culture, VariantBaseState state, string? fp, int days = 0) =>
        new(culture, state, state == VariantBaseState.NotCreated ? null : T0.AddDays(days), null, culture, fp);

    private static DocumentSnapshot Doc(params CultureSnapshot[] cultures) =>
        new(DocKey, 1, null, "-1,1", "00001", 1, "page", cultures);

    private static IndexResult Run(DocumentSnapshot doc, IReadOnlyList<VariantRow>? previous = null,
        IndexMode mode = IndexMode.Save, string[]? siteCultures = null) =>
        StatusIndexer.Index(doc, Site, En, siteCultures ?? [En, Nl], previous ?? [], mode, Now);

    private static VariantRow Nld(IndexResult r) => r.Variants.Single(v => v.Culture == Nl);

    private static VariantRow Stored(CultureSnapshot c, string? baseline = null, BaselineSource? src = null) =>
        new(DocKey, c.Culture, CellStatus.Published, false, baseline is null ? TrackingMode.Date : TrackingMode.Fingerprint,
            c.UpdateDateUtc, null, c.Name, c.Fingerprint, baseline, src, null, null, null);

    [Fact]
    public void Culture_outside_site_is_not_applicable()
    {
        var r = Run(Doc(C(En, VariantBaseState.Published, "a"), C(Nl, VariantBaseState.Published, "b")), siteCultures: [En]);
        var v = Nld(r);
        Assert.Equal(CellStatus.NotApplicable, v.Status);
        Assert.False(v.Outdated);
        Assert.Null(v.TextFingerprint);
        Assert.Null(v.UpdateDateUtc);
    }

    [Fact]
    public void Not_created_is_missing()
    {
        var r = Run(Doc(C(En, VariantBaseState.Published, "a"), C(Nl, VariantBaseState.NotCreated, null)));
        Assert.Equal(CellStatus.Missing, Nld(r).Status);
        Assert.False(Nld(r).Outdated);
    }

    [Theory]
    [InlineData(VariantBaseState.Draft, CellStatus.Draft)]
    [InlineData(VariantBaseState.Published, CellStatus.Published)]
    [InlineData(VariantBaseState.PublishedPendingChanges, CellStatus.PendingChanges)]
    public void Base_states_map_to_cells(VariantBaseState state, CellStatus expected)
    {
        var r = Run(Doc(C(En, VariantBaseState.Published, "a"), C(Nl, state, "b")));
        Assert.Equal(expected, Nld(r).Status);
    }

    [Fact]
    public void First_index_uses_date_mode()
    {
        var r = Run(Doc(C(En, VariantBaseState.Published, "a"), C(Nl, VariantBaseState.Published, "b")));
        var v = Nld(r);
        Assert.Equal(TrackingMode.Date, v.Mode);
        Assert.Null(v.BaselineFingerprint);
        Assert.Equal("b", v.TextFingerprint);
        Assert.Equal(Site, r.Document.SiteRootKey);
    }

    [Fact]
    public void Date_mode_outdated_when_default_is_newer()
    {
        var r = Run(Doc(C(En, VariantBaseState.Published, "a", 10), C(Nl, VariantBaseState.Published, "b", 1)));
        var v = Nld(r);
        Assert.True(v.Outdated);
        Assert.Equal(T0.AddDays(10), v.OutdatedSinceUtc);
    }

    [Fact]
    public void Date_mode_not_outdated_when_translation_is_newer()
    {
        var r = Run(Doc(C(En, VariantBaseState.Published, "a", 1), C(Nl, VariantBaseState.Published, "b", 10)));
        Assert.False(Nld(r).Outdated);
        Assert.Null(Nld(r).OutdatedSinceUtc);
    }

    [Fact]
    public void Translation_text_change_records_baseline()
    {
        var prev = new[] { Stored(C(En, VariantBaseState.Published, "a")), Stored(C(Nl, VariantBaseState.Published, "b")) };
        var r = Run(Doc(C(En, VariantBaseState.Published, "a"), C(Nl, VariantBaseState.Published, "b2")), prev);
        var v = Nld(r);
        Assert.Equal("a", v.BaselineFingerprint);
        Assert.Equal(BaselineSource.Saved, v.BaselineSource);
        Assert.Equal(TrackingMode.Fingerprint, v.Mode);
        Assert.False(v.Outdated);
    }

    [Fact]
    public void Default_text_change_after_baseline_is_outdated()
    {
        var prev = new[]
        {
            Stored(C(En, VariantBaseState.Published, "a")),
            Stored(C(Nl, VariantBaseState.Published, "b"), "a", BaselineSource.Saved),
        };
        var r = Run(Doc(C(En, VariantBaseState.Published, "a2"), C(Nl, VariantBaseState.Published, "b")), prev);
        var v = Nld(r);
        Assert.True(v.Outdated);
        Assert.Equal(Now, v.OutdatedSinceUtc);
    }

    [Fact]
    public void Republish_without_text_change_is_not_outdated()
    {
        var prev = new[]
        {
            Stored(C(En, VariantBaseState.Published, "a", 0)),
            Stored(C(Nl, VariantBaseState.Published, "b", 0), "a", BaselineSource.Saved),
        };
        var r = Run(Doc(C(En, VariantBaseState.Published, "a", 30), C(Nl, VariantBaseState.Published, "b", 0)), prev);
        Assert.False(Nld(r).Outdated);
        Assert.Equal("a", Nld(r).BaselineFingerprint);
    }

    [Fact]
    public void Saving_default_and_translation_together_is_not_outdated()
    {
        var prev = new[]
        {
            Stored(C(En, VariantBaseState.Published, "a")),
            Stored(C(Nl, VariantBaseState.Published, "b"), "a", BaselineSource.Saved),
        };
        var r = Run(Doc(C(En, VariantBaseState.Published, "a2"), C(Nl, VariantBaseState.Published, "b2")), prev);
        Assert.False(Nld(r).Outdated);
        Assert.Equal("a2", Nld(r).BaselineFingerprint);
    }

    [Fact]
    public void New_culture_in_existing_document_records_baseline()
    {
        var prev = new[]
        {
            Stored(C(En, VariantBaseState.Published, "a")),
            Stored(C(Nl, VariantBaseState.Published, null)) with { Status = CellStatus.Missing },
        };
        var r = Run(Doc(C(En, VariantBaseState.Published, "a"), C(Nl, VariantBaseState.Draft, "b")), prev);
        Assert.Equal("a", Nld(r).BaselineFingerprint);
        Assert.Equal(BaselineSource.Saved, Nld(r).BaselineSource);
    }

    [Fact]
    public void Outdated_since_is_kept_while_outdated()
    {
        var since = Now.AddDays(-5);
        var prev = new[]
        {
            Stored(C(En, VariantBaseState.Published, "a2")),
            Stored(C(Nl, VariantBaseState.Published, "b"), "a", BaselineSource.Saved) with { Outdated = true, OutdatedSinceUtc = since },
        };
        var r = Run(Doc(C(En, VariantBaseState.Published, "a3"), C(Nl, VariantBaseState.Published, "b")), prev);
        Assert.True(Nld(r).Outdated);
        Assert.Equal(since, Nld(r).OutdatedSinceUtc);
    }

    [Fact]
    public void Rebuild_keeps_baselines_and_marks()
    {
        var user = Guid.NewGuid();
        var prev = new[]
        {
            Stored(C(En, VariantBaseState.Published, "a")),
            Stored(C(Nl, VariantBaseState.Published, "b"), "a", BaselineSource.MarkedUpToDate) with { MarkedByUserKey = user, MarkedUtc = T0 },
        };
        var r = Run(Doc(C(En, VariantBaseState.Published, "a"), C(Nl, VariantBaseState.Published, "b2")), prev, IndexMode.Rebuild);
        var v = Nld(r);
        Assert.Equal("a", v.BaselineFingerprint);
        Assert.Equal(BaselineSource.MarkedUpToDate, v.BaselineSource);
        Assert.Equal(user, v.MarkedByUserKey);
        Assert.Equal(T0, v.MarkedUtc);
    }

    [Fact]
    public void Rebuild_clearing_baselines_returns_to_date_mode()
    {
        var prev = new[]
        {
            Stored(C(En, VariantBaseState.Published, "a2", 10)),
            Stored(C(Nl, VariantBaseState.Published, "b", 1), "a", BaselineSource.Saved) with { Outdated = true, OutdatedSinceUtc = Now.AddDays(-2) },
        };
        var r = Run(Doc(C(En, VariantBaseState.Published, "a2", 10), C(Nl, VariantBaseState.Published, "b", 1)), prev, IndexMode.RebuildClearBaselines);
        var v = Nld(r);
        Assert.Equal(TrackingMode.Date, v.Mode);
        Assert.Null(v.BaselineFingerprint);
        Assert.Null(v.BaselineSource);
        Assert.True(v.Outdated);
        Assert.Equal(T0.AddDays(10), v.OutdatedSinceUtc);
    }

    [Fact]
    public void Mark_up_to_date_clears_outdated()
    {
        var user = Guid.NewGuid();
        var row = Stored(C(Nl, VariantBaseState.Published, "b"), "a", BaselineSource.Saved) with { Outdated = true, OutdatedSinceUtc = T0 };
        var marked = StatusIndexer.MarkUpToDate(row, "a2", user, Now);
        Assert.Equal("a2", marked.BaselineFingerprint);
        Assert.Equal(BaselineSource.MarkedUpToDate, marked.BaselineSource);
        Assert.Equal(user, marked.MarkedByUserKey);
        Assert.Equal(Now, marked.MarkedUtc);
        Assert.Equal(TrackingMode.Fingerprint, marked.Mode);
        Assert.False(marked.Outdated);
        Assert.Null(marked.OutdatedSinceUtc);
    }

    [Fact]
    public void Default_language_is_never_outdated()
    {
        var r = Run(Doc(C(En, VariantBaseState.Published, "a", 1), C(Nl, VariantBaseState.Published, "b", 10)));
        var d = r.Variants.Single(v => v.Culture == En);
        Assert.False(d.Outdated);
        Assert.Null(d.BaselineFingerprint);
    }

    [Fact]
    public void No_source_text_is_never_outdated()
    {
        var prev = new[]
        {
            Stored(C(En, VariantBaseState.Published, "a")),
            Stored(C(Nl, VariantBaseState.Published, "b"), "a", BaselineSource.Saved),
        };
        var r = Run(Doc(C(En, VariantBaseState.NotCreated, null), C(Nl, VariantBaseState.Published, "b")), prev);
        Assert.False(Nld(r).Outdated);
    }
}
