using DynamicDave.Umbraco.TranslationHealth.Content;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class VariantFingerprintTests
{
    [Fact]
    public void Same_content_gives_same_fingerprint_regardless_of_order()
    {
        var a = VariantFingerprint.Compute("Home", [new PropertyText("a", "one"), new PropertyText("b", "two")]);
        var b = VariantFingerprint.Compute("Home", [new PropertyText("b", "two"), new PropertyText("a", "one")]);

        Assert.Equal(a, b);
        Assert.Matches("^[0-9a-f]{64}$", a);
    }

    [Fact]
    public void Changed_value_changes_fingerprint()
    {
        var a = VariantFingerprint.Compute("Home", [new PropertyText("a", "one")]);
        var b = VariantFingerprint.Compute("Home", [new PropertyText("a", "two")]);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Changed_name_changes_fingerprint()
    {
        var a = VariantFingerprint.Compute("Home", [new PropertyText("a", "one")]);
        var b = VariantFingerprint.Compute("Start", [new PropertyText("a", "one")]);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Empty_values_are_ignored()
    {
        var none = VariantFingerprint.Compute("Home", []);
        var empty = VariantFingerprint.Compute("Home", [new PropertyText("a", ""), new PropertyText("b", "")]);

        Assert.Equal(none, empty);
    }
}
