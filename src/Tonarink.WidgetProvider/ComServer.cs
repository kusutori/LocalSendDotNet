using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Microsoft.Windows.Widgets.Providers;
using WinRT;

namespace Tonarink.WidgetProvider;

internal static partial class ComServer
{
    public const string Clsid = "C4A91E3B-6F27-4D8A-9B15-E0D2C7A84F36";
    private static readonly Guid ClassId = new(Clsid);
    private static readonly Guid IUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IInspectable = new("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90");
    private const uint ClsctxLocalServer = 0x4;
    private const uint RegclsMultipleUse = 0x1;
    private const int ClassENoAggregation = unchecked((int)0x80040110);
    private const int ENoInterface = unchecked((int)0x80004002);

    public static readonly StrategyBasedComWrappers Wrappers = new();

    public static IDisposable Register()
    {
        var unknown = Wrappers.GetOrCreateComInterfaceForObject(
            new WidgetProviderFactory(),
            CreateComInterfaceFlags.None);
        var result = CoRegisterClassObject(ClassId, unknown, ClsctxLocalServer, RegclsMultipleUse, out var cookie);
        Marshal.Release(unknown);
        if (result != 0)
            Marshal.ThrowExceptionForHR(result);

        return new Revoker(cookie);
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoRegisterClassObject(
        in Guid rclsid,
        nint pUnk,
        uint dwClsContext,
        uint flags,
        out uint lpdwRegister);

    [LibraryImport("ole32.dll")]
    private static partial int CoRevokeClassObject(uint dwRegister);

    [GeneratedComClass]
    private sealed partial class WidgetProviderFactory : IClassFactory
    {
        public int CreateInstance(nint pUnkOuter, in Guid riid, out nint ppvObject)
        {
            ppvObject = 0;
            WidgetLog.Write($"CreateInstance riid={riid}");
            if (pUnkOuter != 0)
                return ClassENoAggregation;

            if (riid != ClassId
                && riid != typeof(IWidgetProvider).GUID
                && riid != IUnknown
                && riid != IInspectable)
            {
                WidgetLog.Write("CreateInstance E_NOINTERFACE");
                return ENoInterface;
            }

            try
            {
                ppvObject = MarshalInspectable<IWidgetProvider>.FromManaged(new WidgetProvider());
                WidgetLog.Write($"CreateInstance ok ptr=0x{ppvObject:X}");
                return 0;
            }
            catch (Exception exception)
            {
                WidgetLog.Write($"CreateInstance failed {exception}");
                return unchecked((int)0x80004005);
            }
        }

        public int LockServer(int fLock)
        {
            _ = fLock;
            return 0;
        }
    }

    private sealed class Revoker(uint cookie) : IDisposable
    {
        public void Dispose() => _ = CoRevokeClassObject(cookie);
    }
}
