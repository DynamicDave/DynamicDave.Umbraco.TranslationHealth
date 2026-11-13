using DynamicDave.Umbraco.TranslationHealth.Sites;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace DynamicDave.Umbraco.TranslationHealth.Composers;

public class TranslationHealthComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddOptions<TranslationHealthOptions>().Bind(builder.Config.GetSection(Constants.OptionsSection));
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ISiteSource, UmbracoSiteSource>();
    }
}
