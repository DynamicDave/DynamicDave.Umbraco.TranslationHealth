using System.Text.Json;
using DynamicDave.Umbraco.TranslationHealth.Content;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class BlockValueReaderTests
{
    private const string Key1 = "9ffa301c-590f-4fbe-8841-b51e0629d006";
    private const string Key2 = "11111111-590f-4fbe-8841-b51e0629d006";

    private static string Entry(string alias, string? culture, string valueJson) =>
        "{\"editorAlias\":\"Umbraco.TextBox\",\"culture\":" + (culture is null ? "null" : "\"" + culture + "\"")
        + ",\"segment\":null,\"alias\":\"" + alias + "\",\"value\":" + valueJson + "}";

    private static string NestedEntry(string alias, string? culture, string valueJson) =>
        "{\"editorAlias\":\"Umbraco.BlockList\",\"culture\":" + (culture is null ? "null" : "\"" + culture + "\"")
        + ",\"segment\":null,\"alias\":\"" + alias + "\",\"value\":" + valueJson + "}";

    private static string Block(string key, params string[] entries) =>
        "{\"contentTypeKey\":\"b995c5dc-e0fe-4bf1-9a14-dde13ef7d655\",\"key\":\"" + key + "\",\"values\":[" + string.Join(",", entries) + "]}";

    private static string List(string content, string settings = "") =>
        "{\"contentData\":[" + content + "],\"settingsData\":[" + settings + "],"
        + "\"expose\":[{\"contentKey\":\"" + Key1 + "\",\"culture\":\"en-US\",\"segment\":null}],"
        + "\"layout\":{\"Umbraco.BlockList\":[{\"contentUdi\":null,\"settingsUdi\":null,\"contentKey\":\"" + Key1 + "\",\"settingsKey\":null}]}}";

    [Fact]
    public void Reads_culture_values_from_block_list()
    {
        var json = List(
            Block(Key1,
                Entry("note", null, "\"Same in every language\""),
                Entry("headline", "en-US", "\"Hello\""),
                Entry("headline", "nl-NL", "\"Hallo\"")) + "," +
            Block(Key2, Entry("headline", "nl-NL", "\"Tweede\""), Entry("headline", "en-US", "\"Second\"")));

        var values = BlockValueReader.CultureValues(json, "en-US");

        Assert.Equal(
            [($"{Key1}/headline", "Hello"), ($"{Key2}/headline", "Second")],
            values);
    }

    [Fact]
    public void Culture_comparison_is_case_insensitive()
    {
        var json = List(Block(Key1, Entry("headline", "en-US", "\"Hello\"")));

        Assert.Single(BlockValueReader.CultureValues(json, "EN-us"));
    }

    [Fact]
    public void Reads_culture_values_from_rich_text_blocks()
    {
        var block = Block(Key1, Entry("note", null, "\"Invariant\""), Entry("headline", "en-US", "\"RTE headline\""));
        var json = "{\"markup\":\"<p>Text</p>\",\"blocks\":" + List(block) + "}";

        var values = BlockValueReader.CultureValues(json, "en-US");

        Assert.Equal([($"{Key1}/headline", "RTE headline")], values);
    }

    [Theory]
    [InlineData("{\"markup\":\"<p>x</p>\",\"blocks\":null}")]
    [InlineData("{\"markup\":\"<p>x</p>\",\"blocks\":{\"contentData\":[],\"settingsData\":[],\"expose\":[],\"layout\":{}}}")]
    [InlineData("{\"markup\":\"<p>x</p>\"}")]
    public void Rich_text_without_blocks_gives_nothing(string json)
    {
        Assert.Empty(BlockValueReader.CultureValues(json, "en-US"));
    }

    [Fact]
    public void Reads_settings_data()
    {
        var json = List(Block(Key1, Entry("headline", "en-US", "\"Content\"")),
            Block(Key2, Entry("label", "en-US", "\"Setting\"")));

        var values = BlockValueReader.CultureValues(json, "en-US");

        Assert.Contains(($"{Key2}/label", "Setting"), values);
        Assert.Equal(2, values.Count);
    }

    [Fact]
    public void Recurses_into_nested_blocks()
    {
        const string innerKey = "f3e7e86e-0000-4000-8000-000000000001";
        var inner = List(Block(innerKey,
            Entry("note", null, "\"Inner invariant\""),
            Entry("headline", "en-US", "\"Inner en\""),
            Entry("headline", "nl-NL", "\"Inner nl\"")));

        // Umbraco writes the nested list as a JSON-encoded string.
        var asString = List(Block(Key1,
            Entry("title", "en-US", "\"Wrapper en\""),
            NestedEntry("inner", null, JsonSerializer.Serialize(inner))));
        // Raw SetValue leaves an object.
        var asObject = List(Block(Key1,
            Entry("title", "en-US", "\"Wrapper en\""),
            NestedEntry("inner", null, inner)));

        foreach (var json in new[] { asString, asObject })
        {
            var values = BlockValueReader.CultureValues(json, "en-US");

            Assert.Equal(2, values.Count);
            Assert.Contains(($"{Key1}/title", "Wrapper en"), values);
            Assert.Contains(($"{innerKey}/headline", "Inner en"), values);
            Assert.DoesNotContain(values, v => v.Value.Contains("contentData"));
        }
    }

    [Fact]
    public void Culture_variant_nested_property_is_filtered_by_outer_culture()
    {
        const string innerKey = "f3e7e86e-0000-4000-8000-000000000002";
        var inner = List(Block(innerKey, Entry("headline", "en-US", "\"Inner en\"")));
        var json = List(Block(Key1, NestedEntry("innerVar", "nl-NL", JsonSerializer.Serialize(inner))));

        Assert.Empty(BlockValueReader.CultureValues(json, "en-US"));
    }

    [Fact]
    public void Ignores_layout_and_expose()
    {
        var v17 = List(Block(Key1, Entry("headline", "en-US", "\"Hello\"")));
        var v18 = v17.Replace("\"contentUdi\":null,\"settingsUdi\":null,", "");

        Assert.Equal(BlockValueReader.CultureValues(v17, "en-US"), BlockValueReader.CultureValues(v18, "en-US"));
        Assert.Single(BlockValueReader.CultureValues(v18, "en-US"));
    }

    [Fact]
    public void Ignores_invariant_values_and_settings_without_culture()
    {
        var json = List(
            Block(Key1, Entry("note", null, "\"Invariant\"")),
            Block(Key2, Entry("flag", null, "\"x\"")));

        Assert.Empty(BlockValueReader.CultureValues(json, "en-US"));
    }

    [Fact]
    public void Converts_numbers_and_booleans_and_skips_null()
    {
        var json = List(Block(Key1,
            Entry("count", "en-US", "12.5"),
            Entry("whole", "en-US", "7"),
            Entry("on", "en-US", "true"),
            Entry("off", "en-US", "false"),
            Entry("nothing", "en-US", "null")));

        var values = BlockValueReader.CultureValues(json, "en-US");

        Assert.Equal(
            [($"{Key1}/count", "12.5"), ($"{Key1}/whole", "7"), ($"{Key1}/on", "true"), ($"{Key1}/off", "false")],
            values);
    }

    [Theory]
    [InlineData("\"plain text\"")]
    [InlineData("[1,2]")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{not json")]
    public void Non_block_json_gives_nothing(string json)
    {
        Assert.Empty(BlockValueReader.CultureValues(json, "en-US"));
    }

    [Fact]
    public void Null_input_gives_nothing()
    {
        Assert.Empty(BlockValueReader.CultureValues(null, "en-US"));
    }

    [Theory]
    [InlineData("null", true)]
    [InlineData("null", false)]
    [InlineData("{\"contentData\":[],\"settingsData\":[],\"expose\":[],\"layout\":{}}", true)]
    [InlineData("{\"contentData\":[],\"settingsData\":[],\"expose\":[],\"layout\":{}}", false)]
    public void Reads_rich_text_markup_inside_a_block(string blocks, bool asString)
    {
        var rich = "{\"markup\":\"<p>Hi</p>\",\"blocks\":" + blocks + "}";
        var value = asString ? JsonSerializer.Serialize(rich) : rich;
        var json = List(Block(Key1, Entry("body", "en-US", value), Entry("body", "nl-NL", value)));

        var values = BlockValueReader.CultureValues(json, "en-US");

        Assert.Equal([($"{Key1}/body", "<p>Hi</p>")], values);
    }

    [Fact]
    public void Rich_text_inside_a_block_also_reads_its_own_blocks()
    {
        const string innerKey = "f3e7e86e-0000-4000-8000-000000000003";
        var innerBlocks = List(Block(innerKey, Entry("headline", "en-US", "\"Deep\"")));
        var rich = "{\"markup\":\"<p>Hi</p>\",\"blocks\":" + innerBlocks + "}";
        var json = List(Block(Key1, Entry("body", "en-US", JsonSerializer.Serialize(rich))));

        var values = BlockValueReader.CultureValues(json, "en-US");

        Assert.Equal([($"{Key1}/body", "<p>Hi</p>"), ($"{innerKey}/headline", "Deep")], values);
    }
}
