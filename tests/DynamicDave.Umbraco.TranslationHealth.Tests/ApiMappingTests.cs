using System.Text;
using System.Text.Json;
using DynamicDave.Umbraco.TranslationHealth.Dictionary;
using DynamicDave.Umbraco.TranslationHealth.Documents;
using DynamicDave.Umbraco.TranslationHealth.Files;
using DynamicDave.Umbraco.TranslationHealth.Importing;
using DynamicDave.Umbraco.TranslationHealth.Indexing;
using DynamicDave.Umbraco.TranslationHealth.Jobs;
using DynamicDave.Umbraco.TranslationHealth.Models;
using DynamicDave.Umbraco.TranslationHealth.Overview;
using DynamicDave.Umbraco.TranslationHealth.Status;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class ApiMappingTests
{
    private static readonly DateTime T0 = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);

    private static VariantRow Variant(Guid doc, string culture, CellStatus status, bool outdated = false) =>
        new(doc, culture, status, outdated, outdated ? TrackingMode.Fingerprint : TrackingMode.Date, T0, T0.AddDays(1), $"{culture} name",
            "fp", "base", BaselineSource.Saved, null, null, outdated ? T0.AddDays(2) : null);

    private static DocumentRow Document(Guid key) => new(key, 5, Guid.NewGuid(), "-1,1,5", "0001,0005", 2, "translatedPage", Guid.NewGuid(), "Page");

    private static PlanItem Item(int id, ImportOutcome outcome, string key = "K", string culture = "nl-NL", bool selected = false) =>
        new(id, id + 1, key, culture, outcome, ImportErrorReason.None, null, "v", null, null, false, selected, null);

    private static ImportPlan Plan(params PlanItem[] items) =>
        new(Guid.NewGuid(), "file.csv", ImportFileFormat.Csv, new ImportOptions(false, true), ["Extra"], items);

    [Fact]
    public void Variant_row_maps_to_cell_model()
    {
        var doc = Guid.NewGuid();
        var cell = ApiMapping.ToCell(Variant(doc, "nl-NL", CellStatus.PendingChanges, outdated: true));

        Assert.Equal("nl-NL", cell.Culture);
        Assert.Equal(CellStatus.PendingChanges, cell.Status);
        Assert.True(cell.Outdated);
        Assert.Equal(TrackingMode.Fingerprint, cell.Mode);
        Assert.Equal("nl-NL name", cell.Name);
        Assert.Equal(T0.AddDays(2), cell.OutdatedSinceUtc);
    }

    [Fact]
    public void Overview_row_has_one_cell_per_culture_and_fills_missing_rows_as_not_applicable()
    {
        var key = Guid.NewGuid();
        var row = new OverviewRow(Document(key), [Variant(key, "nl-NL", CellStatus.Published), Variant(key, "en-US", CellStatus.Published)]);

        var model = ApiMapping.ToRowModel(row, ["en-US", "nl-NL", "de-DE"]);

        Assert.Equal(["en-US", "nl-NL", "de-DE"], model.Cells.Select(c => c.Culture));
        Assert.Equal(CellStatus.NotApplicable, model.Cells[2].Status);
        Assert.False(model.Cells[2].Outdated);
        Assert.Equal(key, model.Key);
        Assert.Equal(2, model.Level);
        Assert.Equal("translatedPage", model.ContentTypeAlias);
    }

    [Fact]
    public void Culture_order_puts_the_default_first_and_keeps_the_rest()
    {
        Assert.Equal(["nl-NL", "en-US", "de-DE"], ApiMapping.CultureOrder(["en-US", "nl-NL", "de-DE"], "nl-nl"));
    }

    [Fact]
    public void Plan_counts_group_by_outcome()
    {
        var plan = Plan(Item(1, ImportOutcome.NewValue), Item(2, ImportOutcome.NewValue), Item(3, ImportOutcome.Error), Item(4, ImportOutcome.Unchanged));

        var response = ApiMapping.ToPlanResponse(plan);

        Assert.Equal(plan.Id, response.PlanId);
        Assert.Equal(4, response.Total);
        Assert.Equal(2, response.Counts.Single(c => c.Outcome == ImportOutcome.NewValue).Count);
        Assert.Equal(1, response.Counts.Single(c => c.Outcome == ImportOutcome.Error).Count);
        Assert.Equal(0, response.Counts.Single(c => c.Outcome == ImportOutcome.NewKey).Count);
        Assert.Equal(Enum.GetValues<ImportOutcome>().Length, response.Counts.Length);
        Assert.Equal(["Extra"], response.UnknownColumns);
    }

    [Fact]
    public void Plan_response_carries_the_first_25_items_and_the_items_endpoint_pages_and_filters()
    {
        var items = Enumerable.Range(1, 40).Select(i => Item(i, i % 2 == 0 ? ImportOutcome.NewValue : ImportOutcome.Unchanged)).ToArray();
        var plan = Plan(items);

        Assert.Equal(25, ApiMapping.ToPlanResponse(plan).Items.Length);

        var page = ApiMapping.ToPlanItems(plan, [ImportOutcome.NewValue], 5, 10);
        Assert.Equal(20, page.Total);
        Assert.Equal(10, page.Items.Length);
        Assert.Equal(12, page.Items[0].Id);

        Assert.Equal(40, ApiMapping.ToPlanItems(plan, null, 0, 100).Total);
        Assert.Equal(40, ApiMapping.ToPlanItems(plan, [], 0, 100).Total);
    }

    [Fact]
    public void Stored_plan_round_trips_with_its_user()
    {
        var user = Guid.NewGuid();
        var plan = Plan(Item(1, ImportOutcome.NewKey, selected: true), Item(2, ImportOutcome.Error));

        var json = ApiMapping.SerializePlan(user, plan);
        var stored = ApiMapping.ReadPlan(json);

        Assert.NotNull(stored);
        Assert.Equal(user, stored.UserKey);
        Assert.Equal(plan.Id, stored.Plan.Id);
        Assert.Equal(ImportFileFormat.Csv, stored.Plan.Format);
        Assert.Equal(ImportOutcome.NewKey, stored.Plan.Items[0].Outcome);
        Assert.True(stored.Plan.Items[0].SelectedByDefault);
        Assert.True(stored.Plan.Options.CreateKeys);
        Assert.Null(ApiMapping.ReadPlan("not json"));
    }

    [Theory]
    [InlineData("a.csv", "Key;nl\n", ImportFileFormat.Csv)]
    [InlineData("A.CSV", "<xliff", ImportFileFormat.Csv)]
    [InlineData("a.xlf", "Key,nl", ImportFileFormat.Xliff)]
    [InlineData("a.xliff", "", ImportFileFormat.Xliff)]
    public void Format_follows_the_file_extension(string name, string head, ImportFileFormat expected) =>
        Assert.Equal(expected, ApiMapping.DetectFormat(name, Encoding.UTF8.GetBytes(head)));

    [Theory]
    [InlineData("export", "﻿<?xml version=\"1.0\"?><xliff", ImportFileFormat.Xliff)]
    [InlineData("export.txt", "  <xliff version=\"1.2\">", ImportFileFormat.Xliff)]
    [InlineData("export.txt", "Key;nl-NL\nA;b", ImportFileFormat.Csv)]
    [InlineData("export", "Key,nl-NL\nA,b", ImportFileFormat.Csv)]
    [InlineData("export.bin", "plain words only", null)]
    [InlineData("export.bin", "", null)]
    public void Format_is_sniffed_from_the_content_for_other_names(string name, string head, ImportFileFormat? expected) =>
        Assert.Equal(expected, ApiMapping.DetectFormat(name, Encoding.UTF8.GetBytes(head)));

    [Fact]
    public void Selection_accepts_only_ids_of_the_plan_and_drops_cultures_the_user_cannot_edit_now()
    {
        var plan = Plan(Item(1, ImportOutcome.NewValue, culture: "nl-NL"), Item(2, ImportOutcome.NewValue, culture: "de-DE"), Item(3, ImportOutcome.Error));
        var editable = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "NL-nl" };

        var (valid, selected) = ApiMapping.Select(plan, [1, 2, 1], editable);

        Assert.True(valid);
        Assert.Equal([1], selected);

        Assert.False(ApiMapping.Select(plan, [1, 99], editable).Valid);
    }

    [Fact]
    public void Export_filter_applies_group_and_missing_in()
    {
        DictionaryEntry E(string key, params (string, string)[] values) =>
            new(Guid.NewGuid(), key, null, values.ToDictionary(v => v.Item1, v => v.Item2, StringComparer.OrdinalIgnoreCase));
        var entries = new[]
        {
            E("Footer.Contact.Phone", ("en-US", "Phone"), ("nl-NL", "Telefoon")),
            E("Footer.Contact.Fax", ("en-US", "Fax")),
            E("Footer.Title", ("en-US", "T")),
            E("Greeting", ("en-US", "Hi"), ("nl-NL", "Hoi")),
        };

        Assert.Equal(4, ApiMapping.FilterForExport(entries, null, null).Count);
        Assert.Equal(["Footer.Contact.Fax", "Footer.Title"], ApiMapping.FilterForExport(entries, "nl-NL", "Footer").Select(e => e.Key));
        Assert.Equal(["Footer.Contact.Phone", "Footer.Contact.Fax"], ApiMapping.FilterForExport(entries, null, "footer.contact").Select(e => e.Key));
        Assert.Equal(["Footer.Contact.Fax", "Footer.Title"], ApiMapping.FilterForExport(entries, "nl-NL", null).Select(e => e.Key));
        Assert.Empty(ApiMapping.FilterForExport(entries, "en-US", null));
    }

    [Fact]
    public void Groups_list_every_group_and_its_parents_once()
    {
        DictionaryEntry E(string key) => new(Guid.NewGuid(), key, null, new Dictionary<string, string>());

        var groups = ApiMapping.Groups([E("Footer.Contact.Phone"), E("Footer.Contact.Fax"), E("Footer.Title"), E("Greeting")]);

        Assert.Equal(["Footer", "Footer.Contact"], groups);
    }

    [Fact]
    public void File_names_carry_the_time_and_the_culture()
    {
        var now = new DateTime(2026, 11, 13, 9, 5, 0, DateTimeKind.Utc);

        Assert.Equal("translation-overview-20261113-0905.csv", ApiMapping.OverviewFileName(now));
        Assert.Equal("dictionary-20261113-0905.csv", ApiMapping.DictionaryFileName(ImportFileFormat.Csv, null, now));
        Assert.Equal("dictionary-nl-NL-20261113-0905.xlf", ApiMapping.DictionaryFileName(ImportFileFormat.Xliff, "nl-NL", now));
    }

    [Fact]
    public void Status_reports_a_stable_error_code_and_the_dates()
    {
        var error = JsonSerializer.Serialize(new IndexError(T0, "Boom: secret detail"));

        var status = ApiMapping.ToStatus(error, T0.AddHours(1).ToString("o"));

        Assert.Equal("indexFailed", status.LastIndexError);
        Assert.Equal(T0, status.LastIndexErrorUtc);
        Assert.Equal(T0.AddHours(1), status.LastRebuildUtc);

        var clean = ApiMapping.ToStatus("", null);
        Assert.Null(clean.LastIndexError);
        Assert.Null(clean.LastIndexErrorUtc);
        Assert.Null(clean.LastRebuildUtc);
        Assert.Null(ApiMapping.ToStatus("garbage", "garbage").LastRebuildUtc);
    }

    [Fact]
    public void Site_is_visible_when_the_user_sees_its_root_or_a_start_node_lies_inside_it()
    {
        Assert.True(ApiMapping.SiteVisible(7, "-1,7", null, []));
        Assert.True(ApiMapping.SiteVisible(7, "-1,7", [-1], []));
        Assert.True(ApiMapping.SiteVisible(7, "-1,7", [7], []));
        Assert.True(ApiMapping.SiteVisible(7, "-1,7", [20], ["-1,7,12,20"]));
        Assert.False(ApiMapping.SiteVisible(7, "-1,7", [20], ["-1,8,20"]));
        Assert.False(ApiMapping.SiteVisible(7, "-1,7", [], []));
    }

    [Fact]
    public void Document_translation_maps_with_the_mark_permission()
    {
        var doc = Guid.NewGuid();
        var translation = new DocumentTranslation("nl-NL", "Dutch", false, Variant(doc, "nl-NL", CellStatus.Published, outdated: true), "Dave");

        var model = ApiMapping.ToModel(translation, canMarkUpToDate: true);

        Assert.Equal("Dutch", model.LanguageName);
        Assert.Equal("Dave", model.MarkedByName);
        Assert.True(model.CanMarkUpToDate);
        Assert.Equal(T0.AddDays(2), model.OutdatedSinceUtc);
        Assert.Equal(T0.AddDays(1), model.PublishDateUtc);
    }

    [Fact]
    public void Job_state_maps_without_internal_result_pointers()
    {
        var state = new JobState(Guid.NewGuid(), JobKind.Rebuild, JobStatus.Running, Guid.NewGuid(), "Dave", T0, T0, null, 3, 10, JobPhases.Indexing, null, "{}");

        var model = ApiMapping.ToModel(state);

        Assert.Equal(state.Id, model.Id);
        Assert.Equal(JobKind.Rebuild, model.Kind);
        Assert.Equal(3, model.Done);
        Assert.Equal(10, model.Total);
        Assert.Equal("indexing", model.Phase);
    }

    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(0, 100, true)]
    [InlineData(-1, 25, false)]
    [InlineData(0, 0, false)]
    [InlineData(0, 101, false)]
    public void Paging_is_validated(int skip, int take, bool valid) => Assert.Equal(valid, Paging.IsValid(skip, take));

    [Theory]
    [InlineData(1000, true)]
    [InlineData(1001, false)]
    public void Plan_items_come_in_pages_of_up_to_1000(int take, bool valid)
        => Assert.Equal(valid, Paging.IsValid(0, take, Paging.MaxPlanItemsTake));
}
