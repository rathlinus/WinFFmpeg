using System.Text.Json.Serialization;

namespace WinFFmpeg.Core;

/// <summary>A named, one-click conversion shown in the context menu.</summary>
public sealed record Preset
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    public ConversionSettings Settings { get; init; } = new();

    /// <summary>Input kinds this preset can convert.</summary>
    [JsonIgnore]
    public MediaKind Inputs => FormatCatalog.Find(Settings.Format)?.Accepts ?? MediaKind.None;
}

public static class PresetCatalog
{
    public const string UserPrefix = "user-";

    public static readonly IReadOnlyList<Preset> BuiltIn =
    [
        new() { Id = "video-mp4-h264", Name = "MP4 (H.264)", Settings = new() { Format = "mp4", VideoCodec = Codecs.H264 } },
        new() { Id = "video-mp4-hevc", Name = "MP4 (HEVC)", Settings = new() { Format = "mp4", VideoCodec = Codecs.Hevc } },
        new() { Id = "video-webm", Name = "WebM (VP9)", Settings = new() { Format = "webm", VideoCodec = Codecs.Vp9 } },
        new() { Id = "video-gif", Name = "GIF", Settings = new() { Format = "gif" } },

        new() { Id = "audio-mp3", Name = "MP3", Settings = new() { Format = "mp3", Quality = Quality.High } },
        new() { Id = "audio-flac", Name = "FLAC", Settings = new() { Format = "flac" } },
        new() { Id = "audio-wav", Name = "WAV", Settings = new() { Format = "wav" } },
        new() { Id = "audio-opus", Name = "Opus", Settings = new() { Format = "opus" } },

        new() { Id = "image-png", Name = "PNG", Settings = new() { Format = "png" } },
        new() { Id = "image-jpg", Name = "JPG", Settings = new() { Format = "jpg", Quality = Quality.High } },
        new() { Id = "image-webp", Name = "WebP", Settings = new() { Format = "webp", Quality = Quality.High } },
    ];

    /// <summary>Starting point for the "Compress…" command in the custom dialog.</summary>
    public static readonly ConversionSettings CompressDefaults = new()
    {
        Format = "mp4",
        VideoCodec = Codecs.H264,
        Quality = Quality.Small,
        MaxHeight = 1080,
        Suffix = " (compressed)",
    };

    public static Preset? Find(string id, IEnumerable<Preset> userPresets) =>
        BuiltIn.Concat(userPresets).FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
}

public enum MenuEntryKind
{
    Preset,
    Separator,
    Compress,
    Custom,
}

public sealed record MenuEntry(string Id, string Title, MenuEntryKind Kind, Preset? Preset = null);

/// <summary>
/// Decides what the Explorer flyout contains. Explorer enumerates the commands once and
/// then asks each one whether it applies to the selection, so the model has two halves:
/// the fixed list and the per-selection filter.
/// </summary>
public static class ContextMenuModel
{
    public const string RootTitle = "Convert with WinFFmpeg";
    public const string CompressId = "compress";
    public const string CustomId = "custom";
    private const string CompressTitle = "Compress…";
    private const string CustomTitle = "Custom…";
    private const string ExtractAudioPresetId = "audio-mp3";
    private const string UserSeparatorId = "separator-user";
    private const string ToolsSeparatorId = "separator-tools";

    /// <summary>Every entry that can ever appear, in display order.</summary>
    public static IReadOnlyList<MenuEntry> AllEntries(IReadOnlyList<Preset> userPresets)
    {
        var entries = new List<MenuEntry>();
        entries.AddRange(PresetCatalog.BuiltIn.Select(p => new MenuEntry(p.Id, p.Name, MenuEntryKind.Preset, p)));
        entries.Add(new MenuEntry(UserSeparatorId, "", MenuEntryKind.Separator));
        entries.AddRange(userPresets.Select(p => new MenuEntry(p.Id, p.Name, MenuEntryKind.Preset, p)));
        entries.Add(new MenuEntry(ToolsSeparatorId, "", MenuEntryKind.Separator));
        entries.Add(new MenuEntry(CompressId, CompressTitle, MenuEntryKind.Compress));
        entries.Add(new MenuEntry(CustomId, CustomTitle, MenuEntryKind.Custom));
        return entries;
    }

    /// <summary>The entries to show for a selection: id to the title it gets for that selection.</summary>
    public static IReadOnlyDictionary<string, string> Visible(
        SelectionSummary selection, IReadOnlyList<Preset> userPresets)
    {
        var visible = new Dictionary<string, string>(StringComparer.Ordinal);
        if (selection.SupportedCount == 0)
        {
            return visible;
        }

        bool videoOnly = selection.Kinds == MediaKind.Video;
        bool anyBuiltIn = false;
        foreach (Preset preset in PresetCatalog.BuiltIn)
        {
            if (!Applies(preset, selection))
            {
                continue;
            }

            // A video's menu is about video. Its sound gets one entry, named for what
            // it does; the other audio formats are a click away under "Custom…".
            bool audioTarget = FormatCatalog.Find(preset.Settings.Format)?.Kind == MediaKind.Audio;
            if (videoOnly && audioTarget)
            {
                if (preset.Id != ExtractAudioPresetId)
                {
                    continue;
                }

                visible[preset.Id] = $"Extract audio ({preset.Name})";
            }
            else
            {
                visible[preset.Id] = preset.Name;
            }

            anyBuiltIn = true;
        }

        bool anyUser = false;
        foreach (Preset preset in userPresets)
        {
            if (Applies(preset, selection))
            {
                visible[preset.Id] = preset.Name;
                anyUser = true;
            }
        }

        if (anyBuiltIn && anyUser)
        {
            visible[UserSeparatorId] = "";
        }

        if (anyBuiltIn || anyUser)
        {
            visible[ToolsSeparatorId] = "";
        }

        if (videoOnly)
        {
            visible[CompressId] = CompressTitle;
        }

        visible[CustomId] = CustomTitle;
        return visible;
    }

    private static bool Applies(Preset preset, SelectionSummary selection)
    {
        OutputFormat? format = FormatCatalog.Find(preset.Settings.Format);
        if (format is null || (selection.Kinds & ~format.Accepts) != 0)
        {
            return false;
        }

        // Converting a file to the format it already has is only useful for video
        // containers, where it re-encodes with a different codec.
        bool sameFormat = selection.CommonExtension == format.Extension;
        bool reencodes = format.Kind == MediaKind.Video && format.Id != "gif";
        return !sameFormat || reencodes;
    }
}
