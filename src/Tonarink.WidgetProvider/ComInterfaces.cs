using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Tonarink.WidgetProvider;

[GeneratedComInterface]
[Guid("00000001-0000-0000-C000-000000000046")]
internal partial interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(nint pUnkOuter, in Guid riid, out nint ppvObject);

    [PreserveSig]
    int LockServer(int fLock);
}
