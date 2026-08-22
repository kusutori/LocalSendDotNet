using System.Diagnostics;
using System.Text.Json;

namespace Tonarink.WidgetProvider;

internal static class WidgetCommands
{
    public static bool AppIsRunning()
    {
        try
        {
            if (Mutex.TryOpenExisting(WidgetPaths.MutexName, out var mutex))
            {
                mutex.Dispose();
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    public static void OpenApp()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "tonarink:",
                UseShellExecute = true,
            });
        }
        catch
        {
        }
    }

    public static void ToggleServer(bool currentlyOn)
    {
        if (!AppIsRunning())
        {
            OpenApp();
            return;
        }

        Send(currentlyOn ? "stop-server" : "start-server");
    }

    public static void Send(string verb)
    {
        try
        {
            var json = JsonSerializer.Serialize(
                new WidgetCommandFile { Verb = verb },
                WidgetJsonContext.Default.WidgetCommandFile);
            File.WriteAllText(WidgetPaths.CommandPath(), json);
            using var handle = EventWaitHandle.OpenExisting(WidgetPaths.CommandEventName);
            handle.Set();
        }
        catch
        {
            OpenApp();
        }
    }
}
