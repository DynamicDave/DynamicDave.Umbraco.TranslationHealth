using DynamicDave.Umbraco.TranslationHealth.Dictionary;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class MissingReportTests
{
    private static readonly string[] Cultures = ["en-US", "nl-NL", "de-DE"];

    private static DictionaryEntry Entry(string key, params (string Culture, string Value)[] values) =>
        new(Guid.NewGuid(), key, null, values.ToDictionary(v => v.Culture, v => v.Value, StringComparer.OrdinalIgnoreCase));

    private static List<DictionaryEntry> Sample() =>
    [
        Entry("Footer.Contact.Phone", ("en-US", "Phone"), ("nl-NL", "Telefoon")),
        Entry("Footer.Contact.Fax", ("en-US", "Fax")),
        Entry("Footer.Title", ("en-US", "T"), ("nl-NL", "T"), ("de-DE", "T")),
        Entry("Greeting", ("en-US", "Hello"), ("nl-NL", "  ")),
    ];

    [Fact]
    public void Report_counts_missing_per_culture()
    {
        var (counts, items) = MissingReport.Build(Sample(), Cultures, null, null, null);

        Assert.Equal([0, 2, 3], counts.Select(c => c.Missing));
        Assert.Equal(["en-US", "nl-NL", "de-DE"], counts.Select(c => c.Culture));
        Assert.Equal(3, items.Count);
        Assert.DoesNotContain(items, i => i.Key == "Footer.Title");
    }

    [Fact]
    public void Report_filters_by_culture_group_and_search()
    {
        var entries = Sample();

        var (counts, items) = MissingReport.Build(entries, Cultures, "nl-NL", null, null);
        Assert.Equal(["Greeting", "Footer.Contact.Fax"], items.Select(i => i.Key));
        Assert.Equal([0, 2, 3], counts.Select(c => c.Missing));

        (counts, items) = MissingReport.Build(entries, Cultures, null, "footer.contact", "FAX");
        Assert.Equal(["Footer.Contact.Fax"], items.Select(i => i.Key));
        Assert.Equal([0, 1, 1], counts.Select(c => c.Missing));
        Assert.Equal(["nl-NL", "de-DE"], items.Single().MissingCultures);

        (_, items) = MissingReport.Build(entries, Cultures, null, "", "greet");
        Assert.Equal(["Greeting"], items.Select(i => i.Key));
    }

    [Fact]
    public void Report_group_filter_includes_subgroups()
    {
        var (_, items) = MissingReport.Build(Sample(), Cultures, null, "Footer", null);

        Assert.Equal(["Footer.Contact.Fax", "Footer.Contact.Phone"], items.Select(i => i.Key));
        Assert.All(items, i => Assert.Equal("Footer.Contact", i.Group));

        (_, items) = MissingReport.Build(Sample(), Cultures, null, "Foot", null);
        Assert.Empty(items);
    }

    [Fact]
    public void Report_sorts_by_group_then_key()
    {
        var (_, items) = MissingReport.Build(Sample(), Cultures, null, null, null);

        Assert.Equal(["Greeting", "Footer.Contact.Fax", "Footer.Contact.Phone"], items.Select(i => i.Key));
    }
}
