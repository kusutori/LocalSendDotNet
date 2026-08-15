using System.Runtime.InteropServices;
using Microsoft.UI.Reactor;
using Microsoft.Windows.AppLifecycle;
using Package = Windows.ApplicationModel.Package;

static class AppPlatform
{
    public const string MinimizedArgument = "--minimized";
    public const string StartupTaskId = "LocalSendStartup";

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LocalSendDotNet");

    public static bool StartHidden { get; } = DetectStartHidden();

    public static bool HasPackageIdentity()
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

    public static string ExecutablePath =>
        Environment.ProcessPath
        ?? throw new InvalidOperationException("The current process path is unavailable.");

    public static WindowIcon AppWindowIcon
    {
        get
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            return File.Exists(path)
                ? WindowIcon.FromPath(path)
                : WindowIcon.FromPath(ExecutablePath);
        }
    }

    private static bool DetectStartHidden()
    {
        if (Environment.GetCommandLineArgs().Any(static argument =>
                string.Equals(argument, MinimizedArgument, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!HasPackageIdentity())
            return false;

        try
        {
            return AppInstance.GetCurrent().GetActivatedEventArgs()?.Kind
                == ExtendedActivationKind.StartupTask;
        }
        catch
        {
            return false;
        }
    }
}
