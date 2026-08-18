using Microsoft.UI.Reactor;

if (await ShareTargetActivationBroker.RedirectToPrimaryInstanceAsync())
    return;

ToolkitXamlMetadata.Register();
ReactorApp.Run(_ =>
{
    ReactorApp.ShutdownPolicy = ShutdownPolicy.OnLastSurfaceClosed;
    var settings = AppSettingsStore.Load();
    AppWindows.OpenMain(startHidden: AppPlatform.StartHidden && settings.MinimizeToTray);
});
