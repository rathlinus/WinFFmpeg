using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace WinFFmpeg.Shell;

// Shell COM interfaces, declared with exactly the methods and order of the Windows SDK
// headers (ShObjIdl_core.h). Parameters this extension never uses are left as nint.

internal static class HResult
{
    public const int S_OK = 0;
    public const int S_FALSE = 1;
    public const int E_NOTIMPL = unchecked((int)0x80004001);
    public const int E_POINTER = unchecked((int)0x80004003);
    public const int E_FAIL = unchecked((int)0x80004005);
    public const int CLASS_E_NOAGGREGATION = unchecked((int)0x80040110);
    public const int CLASS_E_CLASSNOTAVAILABLE = unchecked((int)0x80040111);
}

internal static class ExplorerCommandState
{
    public const uint Enabled = 0;
    public const uint Hidden = 2;
}

internal static class ExplorerCommandFlags
{
    public const uint Default = 0;
    public const uint HasSubCommands = 0x1;
    public const uint IsSeparator = 0x8;
}

[GeneratedComInterface]
[Guid("00000001-0000-0000-C000-000000000046")]
internal partial interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(nint outer, in Guid riid, out nint instance);

    [PreserveSig]
    int LockServer([MarshalAs(UnmanagedType.Bool)] bool doLock);
}

[GeneratedComInterface]
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
internal partial interface IShellItem
{
    [PreserveSig]
    int BindToHandler(nint bindContext, in Guid handlerId, in Guid riid, out nint handler);

    [PreserveSig]
    int GetParent(out nint parent);

    /// <param name="name">A string allocated with CoTaskMemAlloc that the caller frees.</param>
    [PreserveSig]
    int GetDisplayName(uint sigdn, out nint name);

    [PreserveSig]
    int GetAttributes(uint mask, out uint attributes);

    [PreserveSig]
    int Compare(nint other, uint hint, out int order);
}

[GeneratedComInterface]
[Guid("b63ea76d-1f85-456f-a19c-48159efa858b")]
internal partial interface IShellItemArray
{
    [PreserveSig]
    int BindToHandler(nint bindContext, in Guid handlerId, in Guid riid, out nint handler);

    [PreserveSig]
    int GetPropertyStore(int flags, in Guid riid, out nint store);

    [PreserveSig]
    int GetPropertyDescriptionList(nint keyType, in Guid riid, out nint list);

    [PreserveSig]
    int GetAttributes(int flags, uint mask, out uint attributes);

    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int GetItemAt(uint index, out IShellItem? item);

    [PreserveSig]
    int EnumItems(out nint enumerator);
}

[GeneratedComInterface]
[Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9")]
internal partial interface IExplorerCommand
{
    /// <param name="title">A string allocated with CoTaskMemAlloc that the caller frees.</param>
    [PreserveSig]
    int GetTitle(IShellItemArray? items, out nint title);

    [PreserveSig]
    int GetIcon(IShellItemArray? items, out nint icon);

    [PreserveSig]
    int GetToolTip(IShellItemArray? items, out nint toolTip);

    [PreserveSig]
    int GetCanonicalName(out Guid name);

    [PreserveSig]
    int GetState(IShellItemArray? items, [MarshalAs(UnmanagedType.Bool)] bool okToBeSlow, out uint state);

    [PreserveSig]
    int Invoke(IShellItemArray? items, nint bindContext);

    [PreserveSig]
    int GetFlags(out uint flags);

    [PreserveSig]
    int EnumSubCommands(out IEnumExplorerCommand? commands);
}

[GeneratedComInterface]
[Guid("a88826f8-186f-4987-aade-ea0cef8fbfe8")]
internal unsafe partial interface IEnumExplorerCommand
{
    /// <param name="commands">Receives up to <paramref name="count"/> IExplorerCommand pointers.</param>
    [PreserveSig]
    int Next(uint count, nint* commands, uint* fetched);

    [PreserveSig]
    int Skip(uint count);

    [PreserveSig]
    int Reset();

    [PreserveSig]
    int Clone(out IEnumExplorerCommand? clone);
}
