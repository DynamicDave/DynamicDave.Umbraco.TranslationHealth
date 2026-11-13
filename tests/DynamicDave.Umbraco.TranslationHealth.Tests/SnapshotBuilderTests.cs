using DynamicDave.Umbraco.TranslationHealth.Content;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class SnapshotBuilderTests
{
    private const string Key1 = "9ffa301c-590f-4fbe-8841-b51e0629d006";

    private static (string Alias, bool VariesByCulture, string? EditorAlias, Func<string?, object?> GetValue) Property(
        string alias, bool varies, Func<string?, object?> getValue, string? editor = null)
        => (alias, varies, editor, getValue);

    private static string BlockList(string layoutItem, string headlineEn = "Hello", string headlineNl = "Hallo")
        => "{\"contentData\":[{\"contentTypeKey\":\"b995c5dc-e0fe-4bf1-9a14-dde13ef7d655\",\"key\":\"" + Key1 + "\",\"values\":["
           + "{\"editorAlias\":\"Umbraco.TextBox\",\"culture\":null,\"segment\":null,\"alias\":\"note\",\"value\":\"Same\"},"
           + "{\"editorAlias\":\"Umbraco.TextBox\",\"culture\":\"en-US\",\"segment\":null,\"alias\":\"headline\",\"value\":\"" + headlineEn + "\"},"
           + "{\"editorAlias\":\"Umbraco.TextBox\",\"culture\":\"nl-NL\",\"segment\":null,\"alias\":\"headline\",\"value\":\"" + headlineNl + "\"}]}],"
           + "\"settingsData\":[],\"expose\":[],\"layout\":{\"Umbraco.BlockList\":[" + layoutItem + "]}}";

    [Fact]
    public void SortPath_pads_and_joins()
        => Assert.Equal("00001.00012.00003", SnapshotBuilder.SortPath([1, 12, 3]));

    [Fact]
    public void Variant_property_text_is_taken_per_culture()
    {
        var texts = SnapshotBuilder.CultureTexts(
            [Property("title", true, c => c == "en-US" ? "Hello" : "Hallo"), Property("count", true, _ => 5), Property("empty", true, _ => null)],
            "nl-NL");

        Assert.Equal(
            [new PropertyText("title", "Hallo"), new PropertyText("count", "5")],
            texts);
    }

    [Fact]
    public void Invariant_block_property_contributes_only_that_cultures_block_values()
    {
        var texts = SnapshotBuilder.CultureTexts(
            [Property("blocks", false, _ => BlockList("{\"contentKey\":\"" + Key1 + "\"}"))],
            "nl-NL");

        Assert.Equal([new PropertyText($"blocks/{Key1}/headline", "Hallo")], texts);
    }

    [Fact]
    public void Invariant_plain_property_is_ignored()
    {
        var texts = SnapshotBuilder.CultureTexts(
            [Property("code", false, _ => "ABC-123"), Property("number", false, _ => 7)],
            "en-US");

        Assert.Empty(texts);
    }

    [Fact]
    public void Variant_rich_text_uses_markup_and_block_values()
    {
        var rte = "{\"markup\":\"<p>Text</p>\",\"blocks\":" + BlockList("{\"contentKey\":\"" + Key1 + "\"}") + "}";

        var texts = SnapshotBuilder.CultureTexts([Property("rte", true, _ => rte, "Umbraco.RichText")], "en-US");

        Assert.Equal(
            [
                new PropertyText("rte", "<p>Text</p>"),
                new PropertyText($"rte/{Key1}/note", "Same"),
                new PropertyText($"rte/{Key1}/headline", "Hello"),
            ],
            texts);
    }

    [Theory]
    [InlineData("{\"markup\":\"<p>Hi</p>\",\"blocks\":null}")]
    [InlineData("{\"markup\":\"<p>Hi</p>\",\"blocks\":{\"contentData\":[],\"settingsData\":[],\"expose\":[],\"layout\":{}}}")]
    public void Rich_text_without_blocks_gives_just_the_markup(string rte)
    {
        var texts = SnapshotBuilder.CultureTexts([Property("rte", true, _ => rte)], "en-US");

        Assert.Equal([new PropertyText("rte", "<p>Hi</p>")], texts);
    }

    [Fact]
    public void Variant_block_value_includes_invariant_element_values()
    {
        var texts = SnapshotBuilder.CultureTexts(
            [Property("list", true, _ => BlockList("{\"contentKey\":\"" + Key1 + "\"}"))],
            "en-US");

        Assert.Equal(
            [new PropertyText($"list/{Key1}/note", "Same"), new PropertyText($"list/{Key1}/headline", "Hello")],
            texts);
    }

    [Fact]
    public void Layout_noise_does_not_change_texts()
    {
        var on17 = BlockList("{\"contentUdi\":null,\"settingsUdi\":null,\"contentKey\":\"" + Key1 + "\",\"settingsKey\":null}");
        var on18 = BlockList("{\"contentKey\":\"" + Key1 + "\",\"settingsKey\":null}");

        Assert.NotEqual(on17, on18);
        foreach (var variesByCulture in new[] { true, false })
        {
            Assert.Equal(
                SnapshotBuilder.CultureTexts([Property("list", variesByCulture, _ => on17)], "en-US"),
                SnapshotBuilder.CultureTexts([Property("list", variesByCulture, _ => on18)], "en-US"));
        }
    }

    [Fact]
    public void Plain_text_that_looks_like_json_stays_text()
    {
        var texts = SnapshotBuilder.CultureTexts([Property("note", true, _ => "{not json")], "en-US");

        Assert.Equal([new PropertyText("note", "{not json")], texts);
    }
}
