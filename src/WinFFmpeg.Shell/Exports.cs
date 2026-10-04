using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace WinFFmpeg.Shell;

/// <summary>The entry points COM calls on an in-process server DLL.</summary>
internal static unsafe partial class Exports
{
    // Must match the Clsid in Package.appxmanifest.
    internal static readonly Guid CommandClsid = new("7C1D9A46-5B0E-4E8E-9C7B-2F5A3D6E8B14");

    private const uint FromAddress = 0x4;
    private const uint UnchangedRefCount = 0x2;

    [UnmanagedCallersOnly(EntryPoint = "DllGetClassObject")]
    public static int DllGetClassObject(Guid* clsid, Guid* riid, void** result)
    {
        if (result is null)
        {
            return HResult.E_POINTER;
        }

        *result = null;
        if (clsid is null || riid is null || *clsid != CommandClsid)
        {
            return HResult.CLASS_E_CLASSNOTAVAILABLE;
        }

        try
        {
            nint factory = (nint)ComInterfaceMarshaller<IClassFactory>.ConvertToUnmanaged(new ClassFactory());
            int hr = Marshal.QueryInterface(factory, in *riid, out nint requested);
            Marshal.Release(factory);
            *result = (void*)requested;
            return hr;
        }
        catch
        {
            // An exception must never unwind into COM.
            return HResult.E_FAIL;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "DllCanUnloadNow")]
    public static int DllCanUnloadNow() => HResult.S_FALSE;

    /// <summary>Folder this DLL was loaded from, which is the package's install folder.</summary>
    internal static string ModuleDirectory()
    {
        nint address = (nint)(delegate* unmanaged<int>)&DllCanUnloadNow;
        if (!GetModuleHandleExW(FromAddress | UnchangedRefCount, address, out nint module))
        {
            return AppContext.BaseDirectory;
        }

        char* buffer = stackalloc char[1024];
        uint length = GetModuleFileNameW(module, buffer, 1024);
        return length is > 0 and < 1024
            ? Path.GetDirectoryName(new string(buffer, 0, (int)length)) ?? AppContext.BaseDirectory
            : AppContext.BaseDirectory;
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetModuleHandleExW(uint flags, nint address, out nint module);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetModuleFileNameW(nint module, char* fileName, uint size);
}

[GeneratedComClass]
internal sealed partial class ClassFactory : IClassFactory
{
    public int CreateInstance(nint outer, in Guid riid, out nint instance)
    {
        instance = 0;
        if (outer != 0)
        {
            return HResult.CLASS_E_NOAGGREGATION;
        }

        try
        {
            unsafe
            {
                nint command = (nint)ComInterfaceMarshaller<IExplorerCommand>.ConvertToUnmanaged(new RootCommand());
                int hr = Marshal.QueryInterface(command, in riid, out instance);
                Marshal.Release(command);
                return hr;
            }
        }
        catch
        {
            return HResult.E_FAIL;
        }
    }

    public int LockServer(bool doLock) => HResult.S_OK;
}
