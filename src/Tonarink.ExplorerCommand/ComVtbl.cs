using System.Runtime.InteropServices;

namespace Tonarink.ExplorerCommand;

internal static unsafe class ComVtbl
{
    public static nint Function(nint obj, int index) => ((nint*)*(nint*)obj)[index];

    public static int QueryInterface(nint obj, in Guid iid, out nint ppv)
    {
        ppv = 0;
        if (obj == 0)
            return unchecked((int)0x80004003);

        var fn = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Function(obj, 0);
        nint pointer = 0;
        Guid id = iid;
        var result = fn(obj, &id, &pointer);
        ppv = pointer;
        return result;
    }

    public static uint AddRef(nint obj)
    {
        if (obj == 0)
            return 0;
        var fn = (delegate* unmanaged[Stdcall]<nint, uint>)Function(obj, 1);
        return fn(obj);
    }

    public static uint Release(nint obj)
    {
        if (obj == 0)
            return 0;
        var fn = (delegate* unmanaged[Stdcall]<nint, uint>)Function(obj, 2);
        return fn(obj);
    }

    public static IReadOnlyList<string> GetShellItemPaths(nint shellItemArray)
    {
        if (shellItemArray == 0)
            return [];

        var getCount = (delegate* unmanaged[Stdcall]<nint, uint*, int>)Function(shellItemArray, 7);
        uint count = 0;
        if (getCount(shellItemArray, &count) != 0 || count == 0)
            return [];

        var paths = new List<string>((int)count);
        var getItemAt = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Function(shellItemArray, 8);
        for (uint i = 0; i < count; i++)
        {
            nint item = 0;
            if (getItemAt(shellItemArray, i, &item) != 0 || item == 0)
                continue;

            try
            {
                var getDisplayName = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Function(item, 5);
                nint name = 0;
                if (getDisplayName(item, 0x80058000, &name) != 0 || name == 0)
                    continue;

                try
                {
                    var path = Marshal.PtrToStringUni(name);
                    if (!string.IsNullOrWhiteSpace(path))
                        paths.Add(path);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(name);
                }
            }
            finally
            {
                Release(item);
            }
        }

        return paths;
    }

    public static IReadOnlyList<string> GetShellItemPathsFromSite(nint site)
    {
        if (site == 0)
            return [];

        var service = new Guid("6d5140c1-7436-11ce-8034-00aa006009fa");
        if (QueryInterface(site, in service, out var provider) != 0 || provider == 0)
            return [];

        try
        {
            var folderViewId = new Guid("cde725b0-ccc9-4519-917e-325d72fab4ce");
            var queryService = (delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, nint*, int>)Function(provider, 3);
            nint folderView = 0;
            Guid serviceId = folderViewId;
            Guid interfaceId = folderViewId;
            if (queryService(provider, &serviceId, &interfaceId, &folderView) != 0 || folderView == 0)
                return [];

            try
            {
                var items = (delegate* unmanaged[Stdcall]<nint, uint, Guid*, nint*, int>)Function(folderView, 8);
                var arrayId = new Guid("b63ea76d-1f85-456f-a19c-48159efa858b");
                nint array = 0;
                if (items(folderView, 1, &arrayId, &array) != 0 || array == 0)
                    return [];

                try
                {
                    return GetShellItemPaths(array);
                }
                finally
                {
                    Release(array);
                }
            }
            finally
            {
                Release(folderView);
            }
        }
        finally
        {
            Release(provider);
        }
    }
}
