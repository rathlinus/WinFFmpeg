using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using FFConvert.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using Windows.ApplicationModel;

namespace FFConvert.App;

public sealed partial class SettingsPage : UserControl
{
    // Set while the switches are loaded from the settings, so that does not count as a change.
    private bool _loading;

    public SettingsPage()
    {
        InitializeComponent();

        About.Text = $"FFConvert {InstalledVersion()}";
    }

    /// <summary>The version of the installed package, which is what distinguishes one build from the next.</summary>
    private static string InstalledVersion()
    {
        try
        {
            PackageVersion version = Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
        catch (InvalidOperationException)
        {
            // Running outside the package, as when started from the build output.
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";
        }
    }

    private static AppSettings Settings => App.Current.Settings;

    /// <summary>Loads the current settings into the page. Called each time it is shown.</summary>
    public void Refresh()
    {
        _loading = true;
        HardwareSwitch.IsOn = Settings.UseHardwareEncoding;
        NotifySwitch.IsOn = Settings.NotifyWhenDone;
        CloseSwitch.IsOn = Settings.CloseWhenDone;
        _loading = false;

        RefreshOutput();
        RefreshPresets();
        _ = RefreshFfmpegAsync();
    }

    private async Task RefreshFfmpegAsync()
    {
        FfmpegResetButton.Visibility = Settings.FfmpegPath is null ? Visibility.Collapsed : Visibility.Visible;

        FfmpegPaths? paths = FfmpegLocator.Locate(Settings.FfmpegPath);
        MissingBar.IsOpen = paths is null;
        if (paths is null)
        {
            FfmpegStatus.Text = Settings.FfmpegPath is null
                ? "Not found on PATH or in the usual places"
                : $"Not found in {Settings.FfmpegPath}";
            return;
        }

        FfmpegStatus.Text = paths.Ffmpeg;
        if (await ReadVersionAsync(paths.Ffmpeg) is { } version)
        {
            FfmpegStatus.Text = $"{paths.Ffmpeg}  ·  version {version}";
        }
    }

    /// <summary>The version from the first line of <c>ffmpeg -version</c>, e.g. "7.1" or a git build id.</summary>
    private static async Task<string?> ReadVersionAsync(string ffmpeg)
    {
        try
        {
            string? firstLine = null;
            await FfmpegProcess.RunAsync(ffmpeg, ["-version"], line => firstLine ??= line, CancellationToken.None);
            string[] words = firstLine?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
            return words.Length >= 3 && words[1] == "version" ? words[2] : null;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private void RefreshOutput()
    {
        bool custom = Settings.OutputFolder is not null;
        OutputStatus.Text = custom ? Settings.OutputFolder : "The same folder as the original file";
        OutputResetButton.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshPresets()
    {
        // A new list each time: the repeater only notices a different instance.
        PresetList.ItemsSource = Settings.UserPresets.ToList();
        NoPresets.Visibility = Settings.UserPresets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        Settings.UseHardwareEncoding = HardwareSwitch.IsOn;
        Settings.NotifyWhenDone = NotifySwitch.IsOn;
        Settings.CloseWhenDone = CloseSwitch.IsOn;
        App.Current.SaveSettings();
    }

    private async void OnFfmpegBrowseClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker(XamlRoot.ContentIslandEnvironment.AppWindowId);
        picker.FileTypeFilter.Add(".exe");

        if (await picker.PickSingleFileAsync() is { } picked)
        {
            Settings.FfmpegPath = Path.GetDirectoryName(picked.Path);
            App.Current.SaveSettings();
            await RefreshFfmpegAsync();
        }
    }

    private async void OnFfmpegResetClick(object sender, RoutedEventArgs e)
    {
        Settings.FfmpegPath = null;
        App.Current.SaveSettings();
        await RefreshFfmpegAsync();
    }

    private void OnInstallClick(object sender, RoutedEventArgs e)
    {
        try
        {
            // In its own console window, so the download progress and any prompts are visible.
            Process.Start(new ProcessStartInfo("winget", "install --id Gyan.FFmpeg --exact") { UseShellExecute = true })
                ?.Dispose();
            FfmpegStatus.Text = "Installing… Open Settings again when the installer window has closed.";
        }
        catch (Win32Exception)
        {
            FfmpegStatus.Text = "winget is not available. Download ffmpeg from ffmpeg.org and choose it with Browse.";
        }
    }

    private async void OnOutputBrowseClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker(XamlRoot.ContentIslandEnvironment.AppWindowId);
        if (await picker.PickSingleFolderAsync() is { } picked)
        {
            Settings.OutputFolder = picked.Path;
            App.Current.SaveSettings();
            RefreshOutput();
        }
    }

    private void OnOutputResetClick(object sender, RoutedEventArgs e)
    {
        Settings.OutputFolder = null;
        App.Current.SaveSettings();
        RefreshOutput();
    }

    private void OnDeletePresetClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Preset preset)
        {
            Settings.UserPresets.Remove(preset);
            App.Current.SaveSettings();
            RefreshPresets();
        }
    }
}
