using Microsoft.Extensions.Configuration;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class TranslationHealthOptionsTests
{
    private static TranslationHealthOptions Bind(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var options = new TranslationHealthOptions();
        configuration.GetSection(Constants.OptionsSection).Bind(options);
        return options;
    }

    [Fact]
    public void Options_bind_with_defaults()
    {
        var options = Bind([]);

        Assert.Empty(options.ExcludedDocumentTypes);
    }

    [Fact]
    public void Options_bind_from_configuration()
    {
        var options = Bind(new() { ["DynamicDave:TranslationHealth:ExcludedDocumentTypes:0"] = "settings" });

        Assert.Equal(["settings"], options.ExcludedDocumentTypes);
    }
}
