using Microsoft.UI.Reactor;

if (await ShareTargetActivationBroker.RedirectToPrimaryInstanceAsync())
    return;

ReactorApp.Run(_ =>
{
    ReactorApp.OpenWindow(
        new WindowSpec
        {
            Title = "LocalSend",
            Width = 1120,
            Height = 760,
            MinWidth = 360,
            MinHeight = 520,
        },
        () => new AppShell());
});
