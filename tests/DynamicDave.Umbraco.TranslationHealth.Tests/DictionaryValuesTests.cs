using DynamicDave.Umbraco.TranslationHealth.Dictionary;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class DictionaryValuesTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("  \t", null)]
    [InlineData("", "   ")]
    [InlineData("a\r\nb", "a\nb")]
    [InlineData("a\rb", "a\nb")]
    public void Same_values(string? left, string? right) => Assert.True(DictionaryValues.SameValue(left, right));

    [Theory]
    [InlineData("a", "A")]
    [InlineData("a", null)]
    [InlineData("a ", "a")]
    public void Different_values(string? left, string? right) => Assert.False(DictionaryValues.SameValue(left, right));

    [Fact]
    public void Non_empty_treats_whitespace_as_missing_and_ignores_case()
    {
        var values = new Dictionary<string, string> { ["nl-NL"] = "  ", ["EN-us"] = "Hi" };

        Assert.Null(DictionaryValues.NonEmpty(values, "nl-nl"));
        Assert.Equal("Hi", DictionaryValues.NonEmpty(values, "en-US"));
    }
}
