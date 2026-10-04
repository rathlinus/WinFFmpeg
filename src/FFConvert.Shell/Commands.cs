using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using FFConvert.Core;

namespace FFConvert.Shell;

/// <summary>
/// State shared by the root command and its sub-commands for one menu. Explorer asks
/// every command for its state with the same selection, and each question costs
/// cross-process calls, so the answer is worked out once.
/// </summary>
internal sealed class MenuContext
{
    // Enough to classify a selection; converting uses every selected file.
    private const int ClassifyLimit = 100;
    private const uint FileSystemPath = 0x80058000;

    private static readonly IReadOnlyDictionary<string, string> s_nothing = new Dictionary<string, string>();

    private string? _selectionKey;
    private IReadOnlyDictionary<string, string> _visible = s_nothing;

    public IReadOnlyList<Preset> UserPresets { get; } = SettingsStore.Load().UserPresets;

    /// <summary>The entries to show for the selection: id to title.</summary>
    public IReadOnlyDictionary<string, string> Visible(IShellItemArray? items)
    {
        if (items is null || items.GetCount(out uint count) != HResult.S_OK || count == 0)
        {
            return s_nothing;
        }

        string key = $"{count}|{PathAt(items, 0)}|{PathAt(items, count - 1)}";
        if (key != _selectionKey)
        {
            _visible = ContextMenuModel.Visible(SelectionSummary.From(Paths(items, ClassifyLimit)), UserPresets);
            _selectionKey = key;
        }

        return _visible;
    }

    public static List<string> Paths(IShellItemArray? items, int limit = int.MaxValue)
    {
        var paths = new List<string>();
        if (items is null || items.GetCount(out uint count) != HResult.S_OK)
        {
            return paths;
        }

        for (uint i = 0; i < count && paths.Count < limit; i++)
        {
            if (PathAt(items, i) is { } path)
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    private static string? PathAt(IShellItemArray items, uint index)
    {
        if (items.GetItemAt(index, out IShellItem? item) != HResult.S_OK || item is null)
        {
            return null;
        }

        // Fails for items without a file system path, such as entries inside a zip folder.
        if (item.GetDisplayName(FileSystemPath, out nint name) != HResult.S_OK || name == 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(name);
        }
        finally
        {
            Marshal.FreeCoTaskMem(name);
        }
    }
}

/// <summary>The "Convert with FFConvert" entry; opens the flyout with the conversions.</summary>
[GeneratedComClass]
internal sealed partial class RootCommand : IExplorerCommand
{
    private readonly MenuContext _context = new();

    public int GetTitle(IShellItemArray? items, out nint title)
    {
        title = Marshal.StringToCoTaskMemUni(ContextMenuModel.RootTitle);
        return HResult.S_OK;
    }

    public int GetIcon(IShellItemArray? items, out nint icon)
    {
        icon = Marshal.StringToCoTaskMemUni(Path.Combine(Exports.ModuleDirectory(), "Assets", "AppIcon.ico"));
        return HResult.S_OK;
    }

    public int GetToolTip(IShellItemArray? items, out nint toolTip)
    {
        toolTip = 0;
        return HResult.E_NOTIMPL;
    }

    public int GetCanonicalName(out Guid name)
    {
        name = Exports.CommandClsid;
        return HResult.S_OK;
    }

    public int GetState(IShellItemArray? items, bool okToBeSlow, out uint state)
    {
        state = ExplorerCommandState.Hidden;
        try
        {
            if (_context.Visible(items).Count > 0)
            {
                state = ExplorerCommandState.Enabled;
            }
        }
        catch
        {
            // Stay hidden rather than fail the whole menu.
        }

        return HResult.S_OK;
    }

    public int Invoke(IShellItemArray? items, nint bindContext) => HResult.S_OK;

    public int GetFlags(out uint flags)
    {
        flags = ExplorerCommandFlags.HasSubCommands;
        return HResult.S_OK;
    }

    public int EnumSubCommands(out IEnumExplorerCommand? commands)
    {
        IExplorerCommand[] subCommands =
        [
            .. ContextMenuModel.AllEntries(_context.UserPresets).Select(e => new SubCommand(e, _context)),
        ];
        commands = new CommandEnumerator(subCommands);
        return HResult.S_OK;
    }
}

/// <summary>One entry of the flyout: a preset, a separator, "Compress…" or "Custom…".</summary>
[GeneratedComClass]
internal sealed partial class SubCommand : IExplorerCommand
{
    private readonly MenuEntry _entry;
    private readonly MenuContext _context;

    public SubCommand(MenuEntry entry, MenuContext context)
    {
        _entry = entry;
        _context = context;
    }

    public int GetTitle(IShellItemArray? items, out nint title)
    {
        // Some titles depend on the selection, e.g. "MP3" becomes "Extract audio (MP3)" for videos.
        string text = _entry.Title;
        try
        {
            if (_context.Visible(items).TryGetValue(_entry.Id, out string? forSelection))
            {
                text = forSelection;
            }
        }
        catch
        {
            // Keep the default title.
        }

        title = Marshal.StringToCoTaskMemUni(text);
        return HResult.S_OK;
    }

    public int GetIcon(IShellItemArray? items, out nint icon)
    {
        icon = 0;
        return HResult.E_NOTIMPL;
    }

    public int GetToolTip(IShellItemArray? items, out nint toolTip)
    {
        toolTip = 0;
        return HResult.E_NOTIMPL;
    }

    public int GetCanonicalName(out Guid name)
    {
        name = Guid.Empty;
        return HResult.E_NOTIMPL;
    }

    public int GetState(IShellItemArray? items, bool okToBeSlow, out uint state)
    {
        state = ExplorerCommandState.Hidden;
        try
        {
            if (_context.Visible(items).ContainsKey(_entry.Id))
            {
                state = ExplorerCommandState.Enabled;
            }
        }
        catch
        {
            // Stay hidden rather than fail the whole menu.
        }

        return HResult.S_OK;
    }

    public int Invoke(IShellItemArray? items, nint bindContext)
    {
        if (_entry.Kind == MenuEntryKind.Separator)
        {
            return HResult.S_OK;
        }

        try
        {
            Launcher.Launch(_entry.Id, MenuContext.Paths(items));
            return HResult.S_OK;
        }
        catch
        {
            return HResult.E_FAIL;
        }
    }

    public int GetFlags(out uint flags)
    {
        flags = _entry.Kind == MenuEntryKind.Separator
            ? ExplorerCommandFlags.IsSeparator
            : ExplorerCommandFlags.Default;
        return HResult.S_OK;
    }

    public int EnumSubCommands(out IEnumExplorerCommand? commands)
    {
        commands = null;
        return HResult.E_NOTIMPL;
    }
}

[GeneratedComClass]
internal sealed unsafe partial class CommandEnumerator : IEnumExplorerCommand
{
    private readonly IExplorerCommand[] _commands;
    private int _index;

    public CommandEnumerator(IExplorerCommand[] commands)
    {
        _commands = commands;
    }

    public int Next(uint count, nint* commands, uint* fetched)
    {
        uint copied = 0;
        while (copied < count && _index < _commands.Length)
        {
            commands[copied++] = (nint)ComInterfaceMarshaller<IExplorerCommand>.ConvertToUnmanaged(_commands[_index++]);
        }

        if (fetched is not null)
        {
            *fetched = copied;
        }

        return copied == count ? HResult.S_OK : HResult.S_FALSE;
    }

    public int Skip(uint count)
    {
        _index = (int)Math.Min((uint)_commands.Length, (uint)_index + count);
        return HResult.S_OK;
    }

    public int Reset()
    {
        _index = 0;
        return HResult.S_OK;
    }

    public int Clone(out IEnumExplorerCommand? clone)
    {
        clone = null;
        return HResult.E_NOTIMPL;
    }
}

internal static partial class Launcher
{
    private const string AppExecutable = "FFConvert.exe";

    /// <summary>Starts the app (or hands over to the running instance) for a menu command.</summary>
    public static void Launch(string command, IReadOnlyList<string> files)
    {
        if (files.Count == 0)
        {
            return;
        }

        // Started in the package folder, not next to the files: a process keeps its
        // working directory locked, and that would stop the user from renaming or
        // deleting the folder they are converting in.
        string packageFolder = Exports.ModuleDirectory();
        var startInfo = new ProcessStartInfo(Path.Combine(packageFolder, AppExecutable))
        {
            UseShellExecute = false,
            WorkingDirectory = packageFolder,
        };
        foreach (string argument in LaunchRequest.ToArguments(
            command, files, Path.Combine(AppPaths.DataFolder, "launch")))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process? process = Process.Start(startInfo);
        if (process is not null)
        {
            // Explorer lets this process take the foreground for the click; pass that on
            // so the app's window does not open behind the Explorer window.
            AllowSetForegroundWindow(process.Id);
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
