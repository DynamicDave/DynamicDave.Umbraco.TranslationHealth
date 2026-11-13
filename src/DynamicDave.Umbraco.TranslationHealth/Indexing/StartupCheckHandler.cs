using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace DynamicDave.Umbraco.TranslationHealth.Indexing;

/// <summary>Tries the <see cref="StartupCheck"/> when the application has started; the worker repeats it until it ran.</summary>
public sealed class StartupCheckHandler(StartupCheck check) : INotificationHandler<UmbracoApplicationStartedNotification>
{
    public void Handle(UmbracoApplicationStartedNotification notification) => check.Run();
}
