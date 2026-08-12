using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Windows.AppLifecycle;
using Package = Windows.ApplicationModel.Package;

static class ShareTargetActivationBroker
{
    private const string InstanceKey = "LocalSendDotNet.Primary";
    private static readonly ConcurrentQueue<AppActivationArguments> PendingActivations = new();

    public static event EventHandler? ActivationReceived;

    public static bool HasPendingActivations => !PendingActivations.IsEmpty;

    public static async Task<bool> RedirectToPrimaryInstanceAsync()
    {
        if (!HasPackageIdentity())
            return false;

        var current = AppInstance.GetCurrent();
        var primary = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!primary.IsCurrent)
        {
            await primary.RedirectActivationToAsync(current.GetActivatedEventArgs());
            return true;
        }

        primary.Activated += OnActivated;
        Enqueue(current.GetActivatedEventArgs());
        return false;
    }

    public static bool TryDequeue(out AppActivationArguments? activation) =>
        PendingActivations.TryDequeue(out activation);

    private static void OnActivated(object? sender, AppActivationArguments activation) =>
        Enqueue(activation);

    private static void Enqueue(AppActivationArguments activation)
    {
        PendingActivations.Enqueue(activation);
        ActivationReceived?.Invoke(null, EventArgs.Empty);
    }

    private static bool HasPackageIdentity()
    {
        try
        {
            _ = Package.Current.Id.Name;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (COMException)
        {
            return false;
        }
    }
}
