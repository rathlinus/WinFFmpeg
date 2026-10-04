using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace WinFFmpeg.App;

internal static partial class WindowHelper
{
    private const double DefaultDpi = 96;

    public static string IconPath { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");

    /// <summary>
    /// Sets the window's size and minimum size in device-independent pixels and centres
    /// it on its display.
    /// </summary>
    public static void Initialize(Window window, int width, int height, int minWidth, int minHeight)
    {
        AppWindow appWindow = window.AppWindow;
        appWindow.SetIcon(IconPath);

        double scale = GetDpiForWindow(Handle(window)) / DefaultDpi;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(minWidth * scale);
            presenter.PreferredMinimumHeight = (int)(minHeight * scale);
        }

        RectInt32 workArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        int pixelWidth = Math.Min((int)(width * scale), workArea.Width);
        int pixelHeight = Math.Min((int)(height * scale), workArea.Height);
        appWindow.MoveAndResize(new RectInt32(
            workArea.X + (workArea.Width - pixelWidth) / 2,
            workArea.Y + (workArea.Height - pixelHeight) / 2,
            pixelWidth,
            pixelHeight));
    }

    public static void BringToFront(Window window)
    {
        window.Activate();
        SetForegroundWindow(Handle(window));
    }

    public static bool IsForeground(Window window) => GetForegroundWindow() == Handle(window);

    /// <summary>Opens Explorer with the file selected, or the folder when the file is gone.</summary>
    public static void RevealInExplorer(string path)
    {
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\""))?.Dispose();
        }
        else if (Path.GetDirectoryName(path) is { } folder && Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\""))?.Dispose();
        }
    }

    private static nint Handle(Window window) => WinRT.Interop.WindowNative.GetWindowHandle(window);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();
}
