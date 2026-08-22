using System.Runtime.InteropServices;
using Tonarink.WidgetProvider;
using WinRT;

internal static partial class Program
{
    private const uint CowaitDispatchCalls = 0x8;
    private const uint CowaitDispatchWindowMessages = 0x10;
    private const uint Infinite = 0xFFFFFFFF;

    [LibraryImport("ole32.dll")]
    private static unsafe partial int CoWaitForMultipleHandles(
        uint dwFlags,
        uint dwTimeout,
        uint cHandles,
        nint* pHandles,
        out uint lpdwindex);

    [MTAThread]
    private static unsafe void Main(string[] args)
    {
        WidgetLog.Write($"start args=[{string.Join(" | ", args)}] dir={AppContext.BaseDirectory}");
        if (!args.Any(static argument => argument.Contains("RegisterProcessAsComServer", StringComparison.OrdinalIgnoreCase)))
        {
            WidgetLog.Write("exit: not a COM server launch");
            return;
        }

        try
        {
            ComWrappersSupport.InitializeComWrappers();
            WidgetLog.Write("ComWrappers initialized");
            using (ComServer.Register())
            {
                WidgetLog.Write("class object registered");
                var handle = WidgetProvider.Idle.SafeWaitHandle.DangerousGetHandle();
                var flags = CowaitDispatchCalls | CowaitDispatchWindowMessages;
                var result = CoWaitForMultipleHandles(flags, Infinite, 1, &handle, out _);
                WidgetLog.Write($"wait finished hr=0x{result:X8}");
            }
        }
        catch (Exception exception)
        {
            WidgetLog.Write($"fatal {exception}");
        }
    }
}
