using Microsoft.UI.Reactor;

if (await ShareTargetActivationBroker.RedirectToPrimaryInstanceAsync())
    return;

ReactorApp.Run(_ =>
{
    ReactorApp.ShutdownPolicy = ShutdownPolicy.OnLastSurfaceClosed;
    var settings = AppSettingsStore.Load();
    var startHidden = AppPlatform.StartHidden && settings.MinimizeToTray;
    var window = ReactorApp.OpenWindow(
        new WindowSpec
        {
            Title = "LocalSend",
            Width = 1120,
            Height = 760,
            MinWidth = 360,
            MinHeight = 520,
            Icon = AppPlatform.AppWindowIcon,
            ShowInTaskbar = !startHidden,
        },
        () => new AppShell());
    if (startHidden)
        window.Hide();
});
