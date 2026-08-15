using Microsoft.UI.Reactor;

static class AppWindows
{
    public static ReactorWindow OpenMain(bool startHidden)
    {
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
        return window;
    }
}
