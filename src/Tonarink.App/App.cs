using Microsoft.UI.Reactor;

if (await ShareTargetActivationBroker.RedirectToPrimaryInstanceAsync())
    return;

ToolkitXamlMetadata.Register();
WidgetAppHost.Start();
try
{
    ReactorApp.Run(_ =>
    {
        ReactorApp.ShutdownPolicy = ShutdownPolicy.OnLastSurfaceClosed;
        var settings = AppSettingsStore.Load();
        AppWindows.OpenMain(startHidden: AppPlatform.StartHidden && settings.MinimizeToTray);
        AppNotificationService.Initialize();
    });
}
finally
{
    WidgetAppHost.Stop();
}
