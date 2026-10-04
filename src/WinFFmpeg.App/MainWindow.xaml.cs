using WinFFmpeg.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinFFmpeg.App;

/// <summary>The full converter: choose files and options, or change settings.</summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        WindowHelper.Initialize(this, width: 760, height: 860, minWidth: 520, minHeight: 480);
    }

    /// <summary>Replaces the file list, optionally starting from the given settings.</summary>
    public void LoadFiles(IReadOnlyList<string> files, ConversionSettings? defaults)
    {
        ShowSettings(false);
        ConvertView.LoadFiles(files, defaults);
    }

    private void OnSettingsRequested(object? sender, EventArgs e) => ShowSettings(true);

    private void OnBackRequested(TitleBar sender, object args) => ShowSettings(false);

    private void ShowSettings(bool show)
    {
        if (show)
        {
            SettingsView.Refresh();
        }

        SettingsView.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ConvertView.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        AppTitleBar.IsBackButtonVisible = show;
        AppTitleBar.Subtitle = show ? "Settings" : "";
    }
}
