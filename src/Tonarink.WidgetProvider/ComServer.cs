using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;
using WinRT;

namespace Tonarink.WidgetProvider;

internal static class ComServer
{
    public const string Clsid = "C4A91E3B-6F27-4D8A-9B15-E0D2C7A84F36";
    private const string IUnknown = "00000000-0000-0000-C000-000000000046";
    private const string IInspectable = "AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90";
    private const int ClassENoAggregation = unchecked((int)0x80040110);
    private const int ENoInterface = unchecked((int)0x80004002);

    public static IDisposable Register<T>()
        where T : IWidgetProvider, new()
    {
        uint cookie;
        ClassObject.Register(typeof(T).GUID, new WidgetProviderFactory<T>(), out cookie);
        return new Revoker(cookie);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("00000001-0000-0000-C000-000000000046")]
    private interface IClassFactory
    {
        [PreserveSig]
        int CreateInstance(nint pUnkOuter, ref Guid riid, out nint ppvObject);

        [PreserveSig]
        int LockServer(bool fLock);
    }

    private sealed class WidgetProviderFactory<T> : IClassFactory
        where T : IWidgetProvider, new()
    {
        public int CreateInstance(nint pUnkOuter, ref Guid riid, out nint ppvObject)
        {
            ppvObject = 0;
            if (pUnkOuter != 0)
                return ClassENoAggregation;

            if (riid != typeof(T).GUID
                && riid != typeof(IWidgetProvider).GUID
                && riid != Guid.Parse(IUnknown)
                && riid != Guid.Parse(IInspectable))
            {
                return ENoInterface;
            }

            ppvObject = MarshalInspectable<IWidgetProvider>.FromManaged(new T());
            return 0;
        }

        public int LockServer(bool fLock)
        {
            _ = fLock;
            return 0;
        }
    }

    private static class ClassObject
    {
        public static void Register(Guid clsid, object factory, out uint cookie)
        {
            [DllImport("ole32.dll")]
            static extern int CoRegisterClassObject(
                [MarshalAs(UnmanagedType.LPStruct)] Guid rclsid,
                [MarshalAs(UnmanagedType.IUnknown)] object pUnk,
                uint dwClsContext,
                uint flags,
                out uint lpdwRegister);

            const uint clsctxLocalServer = 0x4;
            const uint regclsMultipleUse = 0x1;
            var result = CoRegisterClassObject(clsid, factory, clsctxLocalServer, regclsMultipleUse, out cookie);
            if (result != 0)
                Marshal.ThrowExceptionForHR(result);
        }

        public static void Revoke(uint cookie)
        {
            [DllImport("ole32.dll")]
            static extern int CoRevokeClassObject(uint dwRegister);

            _ = CoRevokeClassObject(cookie);
        }
    }

    private sealed class Revoker(uint cookie) : IDisposable
    {
        public void Dispose() => ClassObject.Revoke(cookie);
    }
}
