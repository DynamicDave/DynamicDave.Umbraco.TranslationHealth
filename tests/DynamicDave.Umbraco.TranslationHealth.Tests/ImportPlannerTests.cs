using DynamicDave.Umbraco.TranslationHealth.Dictionary;
using DynamicDave.Umbraco.TranslationHealth.Files;
using DynamicDave.Umbraco.TranslationHealth.Importing;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class ImportPlannerTests
{
    private static readonly IReadOnlySet<string> AllEditable = new HashSet<string> { "en-US", "nl-NL", "de-DE" };
    private static readonly IReadOnlySet<string> Installed = new HashSet<string> { "en-US", "nl-NL", "de-DE" };

    private static DictionaryEntry Entry(string key, Guid? id = null, params (string Culture, string Value)[] values) =>
        new(id ?? Guid.NewGuid(), key, null,
            values.ToDictionary(v => v.Culture, v => v.Value, StringComparer.OrdinalIgnoreCase));

    private static ImportRow Row(int number, string key, params (string Culture, string Value)[] values) =>
        new(number, key, values.ToDictionary(v => v.Culture, v => v.Value, StringComparer.OrdinalIgnoreCase), null);

    private static ImportFile Csv(params ImportRow[] rows) =>
        new(ImportFileFormat.Csv, rows, ["Notes"], null, null);

    private static ImportPlan Plan(ImportFile file, DictionaryEntry[] current, ImportOptions? options = null,
        IReadOnlySet<string>? editable = null) =>
        ImportPlanner.Plan(Guid.NewGuid(), "file.csv", file, current, "en-US", editable ?? AllEditable, Installed,
            options ?? new ImportOptions(false, false));

    [Fact]
    public void Items_of_existing_entries_carry_the_entry_id_and_new_keys_do_not()
    {
        var entry = Entry("A", null, ("en-US", "Hello"));
        var plan = Plan(Csv(Row(2, "A", ("nl-NL", "Hallo")), Row(3, "B", ("nl-NL", "Hoi"))), [entry], new ImportOptions(false, true));

        Assert.Equal(entry.Id, plan.Items.Single(i => i.Key == "A").EntryId);
        Assert.Null(plan.Items.Single(i => i.Key == "B").EntryId);
    }

    [Fact]
    public void Empty_value_is_new_value()
    {
        var plan = Plan(Csv(Row(2, "A", ("nl-NL", "Hallo"))), [Entry("A", null, ("en-US", "Hello"))]);

        var item = Assert.Single(plan.Items);
        Assert.Equal(ImportOutcome.NewValue, item.Outcome);
        Assert.Equal(ImportErrorReason.None, item.Reason);
        Assert.Null(item.CurrentValue);
        Assert.Equal("Hallo", item.NewValue);
        Assert.Equal("Hello", item.CurrentSource);
        Assert.True(item.SelectedByDefault);
        Assert.Equal(1, item.Id);
        Assert.Equal(["Notes"], plan.UnknownColumns);
    }

    [Fact]
    public void Different_value_overwrites_when_allowed()
    {
        var plan = Plan(Csv(Row(2, "A", ("nl-NL", "Nieuw"))), [Entry("A", null, ("nl-NL", "Oud"))], new ImportOptions(true, false));

        var item = Assert.Single(plan.Items);
        Assert.Equal(ImportOutcome.Overwrite, item.Outcome);
        Assert.Equal("Oud", item.CurrentValue);
        Assert.True(item.SelectedByDefault);
    }

    [Fact]
    public void Different_value_is_skipped_when_overwrite_is_off()
    {
        var plan = Plan(Csv(Row(2, "A", ("nl-NL", "Nieuw"))), [Entry("A", null, ("nl-NL", "Oud"))]);

        var item = Assert.Single(plan.Items);
        Assert.Equal(ImportOutcome.SkippedHasValue, item.Outcome);
        Assert.False(item.SelectedByDefault);
    }

    [Fact]
    public void Equal_value_is_unchanged_ignoring_line_endings()
    {
        var plan = Plan(Csv(Row(2, "A", ("nl-NL", "a\r\nb"))), [Entry("A", null, ("nl-NL", "a\nb"))], new ImportOptions(true, false));

        var item = Assert.Single(plan.Items);
        Assert.Equal(ImportOutcome.Unchanged, item.Outcome);
        Assert.False(item.SelectedByDefault);
        Assert.Equal("a\r\nb", item.NewValue);
    }

    [Fact]
    public void Unknown_key_is_new_key_when_creating()
    {
        var plan = Plan(Csv(Row(2, "New.Key", ("nl-NL", "x"), ("de-DE", "y"))), [], new ImportOptions(false, true));

        Assert.Equal(2, plan.Items.Count);
        Assert.All(plan.Items, i =>
        {
            Assert.Equal(ImportOutcome.NewKey, i.Outcome);
            Assert.True(i.SelectedByDefault);
            Assert.Null(i.CurrentSource);
        });
    }

    [Fact]
    public void Unknown_key_is_skipped_when_not_creating()
    {
        var plan = Plan(Csv(Row(2, "New.Key", ("nl-NL", "x"))), []);

        var item = Assert.Single(plan.Items);
        Assert.Equal(ImportOutcome.SkippedUnknownKey, item.Outcome);
        Assert.False(item.SelectedByDefault);
    }

    [Fact]
    public void Later_duplicate_key_is_an_error()
    {
        var plan = Plan(Csv(Row(2, "A", ("nl-NL", "1")), Row(3, "a", ("nl-NL", "2"))), [Entry("A")]);

        Assert.Equal(ImportOutcome.NewValue, plan.Items[0].Outcome);
        Assert.Equal(ImportOutcome.Error, plan.Items[1].Outcome);
        Assert.Equal(ImportErrorReason.DuplicateKey, plan.Items[1].Reason);
        Assert.False(plan.Items[1].SelectedByDefault);
    }

    [Fact]
    public void Row_without_values_is_no_earlier_occurrence()
    {
        var plan = Plan(Csv(Row(2, "A"), Row(3, "A", ("nl-NL", "1"))), [Entry("A")]);

        var item = Assert.Single(plan.Items);
        Assert.Equal(ImportOutcome.NewValue, item.Outcome);
        Assert.Equal(3, item.RowNumber);
    }

    [Fact]
    public void Language_that_is_not_installed_is_an_error()
    {
        var plan = Plan(Csv(Row(2, "A", ("fr-FR", "x"))), [Entry("A")]);

        var item = Assert.Single(plan.Items);
        Assert.Equal(ImportOutcome.Error, item.Outcome);
        Assert.Equal(ImportErrorReason.UnknownLanguage, item.Reason);
    }

    [Fact]
    public void Language_the_user_cannot_edit_is_an_error()
    {
        var plan = Plan(Csv(Row(2, "A", ("nl-NL", "x"), ("de-DE", "y"))), [Entry("A")], null, new HashSet<string> { "nl-nl" });

        Assert.Equal(ImportOutcome.NewValue, plan.Items[0].Outcome);
        Assert.Equal("nl-NL", plan.Items[0].Culture);
        Assert.Equal(ImportOutcome.Error, plan.Items[1].Outcome);
        Assert.Equal(ImportErrorReason.LanguageNotAllowed, plan.Items[1].Reason);
    }

    [Fact]
    public void Empty_key_is_an_error_per_culture()
    {
        var plan = Plan(Csv(Row(2, "  ", ("nl-NL", "x"), ("de-DE", "y"))), [Entry("A")], new ImportOptions(true, true));

        Assert.Equal(2, plan.Items.Count);
        Assert.All(plan.Items, i =>
        {
            Assert.Equal(ImportOutcome.Error, i.Outcome);
            Assert.Equal(ImportErrorReason.EmptyKey, i.Reason);
        });
    }

    [Fact]
    public void Duplicate_error_wins_over_unknown_language()
    {
        var plan = Plan(Csv(Row(2, "A", ("nl-NL", "1")), Row(3, "A", ("fr-FR", "2"))), [Entry("A")]);

        Assert.Equal(ImportErrorReason.DuplicateKey, plan.Items[1].Reason);
    }

    [Fact]
    public void Empty_cells_never_clear_values()
    {
        var plan = Plan(Csv(Row(2, "A", ("nl-NL", ""), ("de-DE", "  "))), [Entry("A", null, ("nl-NL", "Oud"), ("de-DE", "Alt"))], new ImportOptions(true, true));

        Assert.Empty(plan.Items);
    }

    [Fact]
    public void Whitespace_only_current_value_counts_as_empty()
    {
        var plan = Plan(Csv(Row(2, "A", ("nl-NL", "x"))), [Entry("A", null, ("nl-NL", "  "))]);

        var item = Assert.Single(plan.Items);
        Assert.Equal(ImportOutcome.NewValue, item.Outcome);
        Assert.Null(item.CurrentValue);
    }

    [Fact]
    public void Keys_match_case_insensitively()
    {
        var plan = Plan(Csv(Row(2, "  footer.CONTACT ", ("nl-NL", "x"))), [Entry("Footer.Contact")]);

        var item = Assert.Single(plan.Items);
        Assert.Equal(ImportOutcome.NewValue, item.Outcome);
        Assert.Equal("Footer.Contact", item.Key);
    }

    [Fact]
    public void Id_prefixed_key_matches_entry_id()
    {
        var entry = Entry("Real.Key");
        var plan = Plan(Csv(Row(2, "id:" + entry.Id, ("nl-NL", "x"))), [entry]);

        var item = Assert.Single(plan.Items);
        Assert.Equal(ImportOutcome.NewValue, item.Outcome);
        Assert.Equal("Real.Key", item.Key);
    }

    [Fact]
    public void Unknown_id_key_is_skipped_even_when_creating_keys()
    {
        var plan = Plan(Csv(Row(2, "id:" + Guid.NewGuid(), ("nl-NL", "x"))), [], new ImportOptions(false, true));

        Assert.Equal(ImportOutcome.SkippedUnknownKey, Assert.Single(plan.Items).Outcome);
    }

    private static ImportFile Xliff(string? fileSource, string key = "A") =>
        new(ImportFileFormat.Xliff,
            [new ImportRow(1, key, new Dictionary<string, string> { ["nl-NL"] = "Hallo" },
                fileSource is null ? null : new Dictionary<string, string> { ["en-US"] = fileSource })],
            [], "nl-NL", null);

    [Fact]
    public void Source_changed_is_flagged_and_not_selected()
    {
        var plan = Plan(Xliff("Old text"), [Entry("A", null, ("en-US", "New text"))]);

        var item = Assert.Single(plan.Items);
        Assert.Equal(ImportOutcome.NewValue, item.Outcome);
        Assert.True(item.SourceChanged);
        Assert.False(item.SelectedByDefault);
        Assert.Equal("Old text", item.FileSource);
        Assert.Equal("New text", item.CurrentSource);
    }

    [Fact]
    public void Unchanged_source_is_selected()
    {
        var plan = Plan(Xliff("line1\r\nline2"), [Entry("A", null, ("en-US", "line1\nline2"))]);

        var item = Assert.Single(plan.Items);
        Assert.False(item.SourceChanged);
        Assert.True(item.SelectedByDefault);
    }

    [Fact]
    public void Csv_never_flags_source_changed()
    {
        var plan = Plan(Csv(Row(2, "A", ("nl-NL", "x"))), [Entry("A", null, ("en-US", "New"))]);

        Assert.False(Assert.Single(plan.Items).SourceChanged);
    }

    [Fact]
    public void New_key_with_source_is_not_flagged_and_is_selected()
    {
        var plan = Plan(Xliff("Some text", "Brand.New"), [], new ImportOptions(false, true));

        var item = Assert.Single(plan.Items);
        Assert.Equal(ImportOutcome.NewKey, item.Outcome);
        Assert.False(item.SourceChanged);
        Assert.True(item.SelectedByDefault);
    }
}
