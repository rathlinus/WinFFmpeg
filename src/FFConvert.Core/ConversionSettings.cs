namespace FFConvert.Core;

public enum Quality
{
    Smallest = 1,
    Small = 2,
    Balanced = 3,
    High = 4,
    Best = 5,
}

/// <summary>Everything that decides how one file is converted. Shared by presets and the custom dialog.</summary>
public sealed record ConversionSettings
{
    /// <summary>Id of an <see cref="OutputFormat"/> in <see cref="FormatCatalog"/>.</summary>
    public string Format { get; init; } = "mp4";

    /// <summary>Codec id from <see cref="Codecs"/>; null picks the format's default.</summary>
    public string? VideoCodec { get; init; }

    /// <summary>Codec id from <see cref="Codecs"/>; null picks the format's default.</summary>
    public string? AudioCodec { get; init; }

    public Quality Quality { get; init; } = Quality.Balanced;

    /// <summary>Scale down so the height does not exceed this. Never scales up.</summary>
    public int? MaxHeight { get; init; }

    public double? FrameRate { get; init; }

    public double? TrimStartSeconds { get; init; }

    public double? TrimEndSeconds { get; init; }

    /// <summary>Appended to the output file name, e.g. " (compressed)".</summary>
    public string? Suffix { get; init; }
}

public static class Codecs
{
    public const string H264 = "h264";
    public const string Hevc = "hevc";
    public const string Vp9 = "vp9";
    public const string Av1 = "av1";
    public const string Gif = "gif";
    public const string Png = "png";
    public const string Jpeg = "jpeg";
    public const string WebP = "webp";

    public const string Aac = "aac";
    public const string Mp3 = "mp3";
    public const string Opus = "opus";
    public const string Flac = "flac";
    public const string Pcm = "pcm";

    public const string Copy = "copy";
    public const string None = "none";

    public static string DisplayName(string codec) => codec switch
    {
        H264 => "H.264",
        Hevc => "HEVC (H.265)",
        Vp9 => "VP9",
        Av1 => "AV1",
        Aac => "AAC",
        Mp3 => "MP3",
        Opus => "Opus",
        Flac => "FLAC",
        Pcm => "PCM (uncompressed)",
        Copy => "Copy (no re-encode)",
        None => "No audio",
        _ => codec.ToUpperInvariant(),
    };
}

/// <param name="Id">Stable id stored in settings and presets.</param>
/// <param name="Extension">Output file extension without the dot.</param>
/// <param name="Kind">What the output is.</param>
/// <param name="Accepts">Which kinds of input can be converted to this format.</param>
/// <param name="VideoCodecs">Selectable video codecs, default first. Empty when not selectable.</param>
/// <param name="AudioCodecs">Selectable audio codecs, default first. Empty when not selectable.</param>
public sealed record OutputFormat(
    string Id,
    string Extension,
    string DisplayName,
    MediaKind Kind,
    MediaKind Accepts,
    IReadOnlyList<string> VideoCodecs,
    IReadOnlyList<string> AudioCodecs);

public static class FormatCatalog
{
    private const MediaKind VideoOrAudio = MediaKind.Video | MediaKind.Audio;

    public static readonly IReadOnlyList<OutputFormat> All =
    [
        new("mp4", "mp4", "MP4", MediaKind.Video, MediaKind.Video,
            [Codecs.H264, Codecs.Hevc, Codecs.Av1, Codecs.Copy],
            [Codecs.Aac, Codecs.Copy, Codecs.None]),
        new("mkv", "mkv", "MKV", MediaKind.Video, MediaKind.Video,
            [Codecs.H264, Codecs.Hevc, Codecs.Av1, Codecs.Vp9, Codecs.Copy],
            [Codecs.Aac, Codecs.Opus, Codecs.Flac, Codecs.Copy, Codecs.None]),
        new("webm", "webm", "WebM", MediaKind.Video, MediaKind.Video,
            [Codecs.Vp9, Codecs.Av1],
            [Codecs.Opus, Codecs.None]),
        new("mov", "mov", "MOV", MediaKind.Video, MediaKind.Video,
            [Codecs.H264, Codecs.Hevc, Codecs.Copy],
            [Codecs.Aac, Codecs.Copy, Codecs.None]),
        new("gif", "gif", "GIF", MediaKind.Video, MediaKind.Video, [], []),

        new("mp3", "mp3", "MP3", MediaKind.Audio, VideoOrAudio, [], []),
        new("m4a", "m4a", "M4A (AAC)", MediaKind.Audio, VideoOrAudio, [], []),
        new("opus", "opus", "Opus", MediaKind.Audio, VideoOrAudio, [], []),
        new("flac", "flac", "FLAC", MediaKind.Audio, VideoOrAudio, [], []),
        new("wav", "wav", "WAV", MediaKind.Audio, VideoOrAudio, [], []),

        new("png", "png", "PNG", MediaKind.Image, MediaKind.Image, [], []),
        new("jpg", "jpg", "JPG", MediaKind.Image, MediaKind.Image, [], []),
        new("webp", "webp", "WebP", MediaKind.Image, MediaKind.Image, [], []),
    ];

    public static OutputFormat Get(string id) =>
        Find(id) ?? throw new ArgumentException($"Unknown output format '{id}'.", nameof(id));

    public static OutputFormat? Find(string id) =>
        All.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Formats every one of the given input kinds can be converted to.</summary>
    public static IReadOnlyList<OutputFormat> ForInputs(MediaKind inputs) =>
        inputs == MediaKind.None ? [] : [.. All.Where(f => (inputs & ~f.Accepts) == 0)];
}
