using System.Runtime.InteropServices;
using Tonarink.WidgetProvider;
using WinRT;

internal static class Program
{
    private const string ComServerArgument = "-RegisterProcessAsComServer";

    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();

    [MTAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], ComServerArgument, StringComparison.Ordinal))
            return;

        ComWrappersSupport.InitializeComWrappers();
        using (ComServer.Register<WidgetProvider>())
        {
            if (GetConsoleWindow() != 0)
            {
                Console.ReadLine();
                return;
            }

            WidgetProvider.Idle.WaitOne();
        }
    }
}
