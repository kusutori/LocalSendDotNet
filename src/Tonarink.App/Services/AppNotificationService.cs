using System.Diagnostics;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

static class AppNotificationService
{
    private static readonly object Gate = new();
    private static bool _registered;

    public static event EventHandler? Activated;

    public static void Initialize()
    {
        lock (Gate)
        {
            if (_registered)
                return;

            try
            {
                var manager = AppNotificationManager.Default;
                manager.NotificationInvoked += OnNotificationInvoked;
                manager.Register();
                _registered = true;
            }
            catch (Exception exception)
            {
                Trace.WriteLine($"[notification] Registration failed: {exception.Message}");
            }
        }
    }

    public static void Show(string title, string message, string kind)
    {
        lock (Gate)
        {
            if (!_registered)
                return;

            try
            {
                var notification = new AppNotificationBuilder()
                    .AddText(title)
                    .AddText(message)
                    .AddArgument("action", "open")
                    .AddArgument("kind", kind)
                    .BuildNotification();

                AppNotificationManager.Default.Show(notification);
            }
            catch (Exception exception)
            {
                Trace.WriteLine($"[notification] Show failed: {exception.Message}");
            }
        }
    }

    private static void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args) =>
        Activated?.Invoke(null, EventArgs.Empty);
}
