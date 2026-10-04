using System.Collections.ObjectModel;
using WinFFmpeg.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace WinFFmpeg.App;

/// <summary>A file in the list of the convert page.</summary>
public sealed class FileItem
{
    public FileItem(string path)
    {
        Path = path;
        Kind = MediaTypes.Classify(path);
    }

    public string Path { get; }

    public MediaKind Kind { get; }

    public string Name => System.IO.Path.GetFileName(Path);

    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";

    public string Glyph => Kind switch
    {
        MediaKind.Video => "",
        MediaKind.Audio => "",
        _ => "",
    };

    // What a screen reader announces for the list item.
    public override string ToString() => Name;
}

public sealed partial class ConvertPage : UserControl
{
    private static readonly (string Label, int? Height)[] s_resolutions =
    [
        ("Original", null), ("2160p (4K)", 2160), ("1440p", 1440), ("1080p", 1080),
        ("720p", 720), ("480p", 480), ("360p", 360),
    ];

    private static readonly (string Label, double? Rate)[] s_frameRates =
    [
        ("Original", null), ("60 fps", 60), ("30 fps", 30), ("24 fps", 24), ("15 fps", 15), ("10 fps", 10),
    ];

    private static readonly string[] s_qualityLabels =
    [
        "Smallest file", "Small file", "Balanced", "High quality", "Best quality",
    ];

    private readonly ObservableCollection<FileItem> _files = [];
    private IReadOnlyList<OutputFormat> _formats = [];
    private IReadOnlyList<string> _videoCodecs = [];
    private IReadOnlyList<string> _audioCodecs = [];
    private string? _suffix;

    // Set while the code fills the boxes, so selection handlers do not react to it.
    private bool _updating;

    public ConvertPage()
    {
        InitializeComponent();

        FilesList.ItemsSource = _files;
        ResolutionBox.ItemsSource = s_resolutions.Select(r => r.Label).ToList();
        ResolutionBox.SelectedIndex = 0;
        FrameRateBox.ItemsSource = s_frameRates.Select(r => r.Label).ToList();
        FrameRateBox.SelectedIndex = 0;
        RefreshFormats();
    }

    public event EventHandler? SettingsRequested;

    /// <summary>Replaces the file list, optionally starting from the given settings.</summary>
    public void LoadFiles(IReadOnlyList<string> files, ConversionSettings? defaults)
    {
        _files.Clear();
        _suffix = null;
        AddFiles(files);
        if (defaults is not null)
        {
            Apply(defaults);
        }
    }

    private OutputFormat? SelectedFormat =>
        FormatBox.SelectedIndex >= 0 && FormatBox.SelectedIndex < _formats.Count
            ? _formats[FormatBox.SelectedIndex]
            : null;

    private string? SelectedVideoCodec =>
        VideoCodecBox.SelectedIndex >= 0 && VideoCodecBox.SelectedIndex < _videoCodecs.Count
            ? _videoCodecs[VideoCodecBox.SelectedIndex]
            : null;

    private string? SelectedAudioCodec =>
        AudioCodecBox.SelectedIndex >= 0 && AudioCodecBox.SelectedIndex < _audioCodecs.Count
            ? _audioCodecs[AudioCodecBox.SelectedIndex]
            : null;

    private void AddFiles(IEnumerable<string> paths)
    {
        int skipped = 0;
        foreach (string path in paths)
        {
            if (MediaTypes.Classify(path) == MediaKind.None)
            {
                skipped++;
            }
            else if (!_files.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                _files.Add(new FileItem(path));
            }
        }

        RefreshFormats();
        if (skipped > 0)
        {
            ShowMessage(
                InfoBarSeverity.Warning,
                skipped == 1 ? "1 file was skipped because it is not a media file." : $"{skipped} files were skipped because they are not media files.");
        }
    }

    /// <summary>Offers the formats every file in the list can be converted to.</summary>
    private void RefreshFormats()
    {
        string? previous = SelectedFormat?.Id;
        MediaKind kinds = _files.Aggregate(MediaKind.None, (all, f) => all | f.Kind);
        _formats = _files.Count == 0 ? FormatCatalog.All : FormatCatalog.ForInputs(kinds);

        _updating = true;
        FormatBox.ItemsSource = _formats.Select(f => f.DisplayName).ToList();
        int index = _formats.ToList().FindIndex(f => f.Id == previous);
        FormatBox.SelectedIndex = _formats.Count == 0 ? -1 : Math.Max(index, 0);
        _updating = false;

        bool hasFiles = _files.Count > 0;
        bool convertible = hasFiles && _formats.Count > 0;
        EmptyState.Visibility = hasFiles ? Visibility.Collapsed : Visibility.Visible;
        FilesList.Visibility = hasFiles ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.Visibility = hasFiles ? Visibility.Visible : Visibility.Collapsed;
        OptionsPanel.IsHitTestVisible = convertible;
        OptionsPanel.Opacity = convertible ? 1 : 0.5;
        ConvertButton.IsEnabled = convertible;
        SavePresetButton.IsEnabled = convertible;

        if (hasFiles && _formats.Count == 0)
        {
            ShowMessage(
                InfoBarSeverity.Warning,
                "Videos and images cannot be converted to the same format. Convert them separately.");
        }
        else if (Message.Severity == InfoBarSeverity.Warning && !convertible)
        {
            Message.IsOpen = false;
        }

        RefreshCodecs();
    }

    private void RefreshCodecs()
    {
        OutputFormat? format = SelectedFormat;
        string? previousVideo = SelectedVideoCodec;
        string? previousAudio = SelectedAudioCodec;
        _videoCodecs = format?.VideoCodecs ?? [];
        _audioCodecs = format?.AudioCodecs ?? [];

        _updating = true;
        VideoCodecBox.ItemsSource = _videoCodecs.Select(Codecs.DisplayName).ToList();
        VideoCodecBox.SelectedIndex = _videoCodecs.Count == 0 ? -1 : Math.Max(_videoCodecs.ToList().IndexOf(previousVideo ?? ""), 0);
        AudioCodecBox.ItemsSource = _audioCodecs.Select(Codecs.DisplayName).ToList();
        AudioCodecBox.SelectedIndex = _audioCodecs.Count == 0 ? -1 : Math.Max(_audioCodecs.ToList().IndexOf(previousAudio ?? ""), 0);
        _updating = false;

        RefreshRows();
    }

    /// <summary>Shows only the options that affect the chosen format.</summary>
    private void RefreshRows()
    {
        OutputFormat? format = SelectedFormat;
        bool video = format?.Kind == MediaKind.Video;
        bool encodesVideo = video && SelectedVideoCodec != Codecs.Copy;
        bool image = format?.Kind == MediaKind.Image;
        bool lossless = format?.Id is "flac" or "wav" or "png";
        bool copiesEverything = video && SelectedVideoCodec == Codecs.Copy
            && SelectedAudioCodec is Codecs.Copy or Codecs.None;

        VideoCodecRow.Visibility = Visible(_videoCodecs.Count > 0);
        AudioCodecRow.Visibility = Visible(_audioCodecs.Count > 0);
        QualityRow.Visibility = Visible(format is not null && !lossless && !copiesEverything);
        ResolutionRow.Visibility = Visible(encodesVideo || image);
        FrameRateRow.Visibility = Visible(encodesVideo);
        TrimRow.Visibility = Visible(format is not null && !image);
    }

    private void Apply(ConversionSettings settings)
    {
        int formatIndex = _formats.ToList().FindIndex(f => f.Id == settings.Format);
        if (formatIndex < 0)
        {
            return;
        }

        FormatBox.SelectedIndex = formatIndex;
        RefreshCodecs();

        _updating = true;
        if (settings.VideoCodec is { } videoCodec && _videoCodecs.ToList().IndexOf(videoCodec) is var v and >= 0)
        {
            VideoCodecBox.SelectedIndex = v;
        }

        if (settings.AudioCodec is { } audioCodec && _audioCodecs.ToList().IndexOf(audioCodec) is var a and >= 0)
        {
            AudioCodecBox.SelectedIndex = a;
        }

        _updating = false;

        QualitySlider.Value = (int)settings.Quality;
        ResolutionBox.SelectedIndex = Math.Max(Array.FindIndex(s_resolutions, r => r.Height == settings.MaxHeight), 0);
        FrameRateBox.SelectedIndex = Math.Max(Array.FindIndex(s_frameRates, r => r.Rate == settings.FrameRate), 0);
        _suffix = settings.Suffix;
        RefreshRows();
    }

    /// <summary>Reads the options into settings. Null, with a message shown, when they are not valid.</summary>
    private ConversionSettings? BuildSettings()
    {
        if (SelectedFormat is not { } format)
        {
            return null;
        }

        double? start = null, end = null;
        if (TrimRow.Visibility == Visibility.Visible)
        {
            if (!TimeText.TryParse(TrimStartBox.Text, out start) || !TimeText.TryParse(TrimEndBox.Text, out end))
            {
                ShowMessage(InfoBarSeverity.Error, "Write trim times as seconds or as minutes:seconds, for example 90 or 1:30.");
                return null;
            }

            if (end is not null && end <= (start ?? 0))
            {
                ShowMessage(InfoBarSeverity.Error, "The trim end has to be after the start.");
                return null;
            }
        }

        bool scales = ResolutionRow.Visibility == Visibility.Visible && ResolutionBox.SelectedIndex > 0;
        bool resamples = FrameRateRow.Visibility == Visibility.Visible && FrameRateBox.SelectedIndex > 0;

        return new ConversionSettings
        {
            Format = format.Id,
            VideoCodec = SelectedVideoCodec,
            AudioCodec = SelectedAudioCodec,
            Quality = (Quality)(int)QualitySlider.Value,
            MaxHeight = scales ? s_resolutions[ResolutionBox.SelectedIndex].Height : null,
            FrameRate = resamples ? s_frameRates[FrameRateBox.SelectedIndex].Rate : null,
            TrimStartSeconds = start,
            TrimEndSeconds = end,
            Suffix = _suffix,
        };
    }

    private void OnFormatChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating)
        {
            RefreshCodecs();
        }
    }

    private void OnCodecChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating)
        {
            RefreshRows();
        }
    }

    private void OnQualityChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        // Fires once while the page is still being built, before the label exists.
        if (QualityLabel is not null)
        {
            QualityLabel.Text = s_qualityLabels[Math.Clamp((int)e.NewValue, 1, 5) - 1];
        }
    }

    private async void OnAddFilesClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker(XamlRoot.ContentIslandEnvironment.AppWindowId);
        foreach (string extension in MediaTypes.AllExtensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        IReadOnlyList<PickFileResult> picked = await picker.PickMultipleFilesAsync();
        AddFiles(picked.Select(p => p.Path));
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _files.Clear();
        _suffix = null;
        RefreshFormats();
    }

    private void OnRemoveFileClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is FileItem file)
        {
            _files.Remove(file);
            RefreshFormats();
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Add to WinFFmpeg";
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        IReadOnlyList<IStorageItem> items = await e.DataView.GetStorageItemsAsync();
        AddFiles(items.OfType<StorageFile>().Select(f => f.Path));
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnConvertClick(object sender, RoutedEventArgs e)
    {
        if (BuildSettings() is not { } settings)
        {
            return;
        }

        Message.IsOpen = false;
        App.Current.StartConversion(_files.Select(f => f.Path).ToList(), settings);
    }

    private async void OnSavePresetClick(object sender, RoutedEventArgs e)
    {
        if (BuildSettings() is not { } settings || SelectedFormat is not { } format)
        {
            return;
        }

        // A trim belongs to the files at hand, not to a reusable preset.
        settings = settings with { TrimStartSeconds = null, TrimEndSeconds = null };

        var nameBox = new TextBox
        {
            Header = "Name shown in the right-click menu",
            Text = format.VideoCodecs.Count > 0 && settings.VideoCodec is { } codec
                ? $"{format.DisplayName} ({Codecs.DisplayName(codec)}, {s_qualityLabels[(int)settings.Quality - 1].ToLowerInvariant()})"
                : $"{format.DisplayName} ({s_qualityLabels[(int)settings.Quality - 1].ToLowerInvariant()})",
            MaxLength = 60,
        };
        nameBox.SelectAll();

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Save as preset",
            Content = nameBox,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(nameBox.Text))
        {
            return;
        }

        App.Current.Settings.UserPresets.Add(new Preset
        {
            Id = $"{PresetCatalog.UserPrefix}{Guid.NewGuid():N}",
            Name = nameBox.Text.Trim(),
            Settings = settings,
        });
        App.Current.SaveSettings();
        ShowMessage(InfoBarSeverity.Success, "Preset saved. It now appears in the right-click menu.");
    }

    private void ShowMessage(InfoBarSeverity severity, string text)
    {
        Message.Severity = severity;
        Message.Message = text;
        Message.IsOpen = true;
    }

    private static Visibility Visible(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
