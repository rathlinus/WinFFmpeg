using System.Globalization;
using System.Text.Json;

namespace WinFFmpeg.Core;

/// <summary>What ffprobe says about an input file. Every field is optional because ffprobe may not know.</summary>
public sealed record MediaInfo
{
    public double? DurationSeconds { get; init; }

    public bool HasVideo { get; init; }

    public bool HasAudio { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public double? FrameRate { get; init; }

    public string? VideoCodec { get; init; }

    public string? AudioCodec { get; init; }
}

public static class FfprobeParser
{
    public static readonly IReadOnlyList<string> Arguments =
        ["-v", "error", "-print_format", "json", "-show_format", "-show_streams"];

    /// <summary>Parses the output of <c>ffprobe -print_format json -show_format -show_streams</c>.</summary>
    public static MediaInfo Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        double? duration = null;
        if (root.TryGetProperty("format", out JsonElement format))
        {
            duration = ReadDouble(format, "duration");
        }

        bool hasVideo = false, hasAudio = false;
        int? width = null, height = null;
        double? frameRate = null;
        string? videoCodec = null, audioCodec = null;

        if (root.TryGetProperty("streams", out JsonElement streams) && streams.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement stream in streams.EnumerateArray())
            {
                string? type = ReadString(stream, "codec_type");
                if (type == "video" && !hasVideo && !IsAttachedPicture(stream))
                {
                    hasVideo = true;
                    videoCodec = ReadString(stream, "codec_name");
                    width = ReadInt(stream, "width");
                    height = ReadInt(stream, "height");
                    frameRate = ParseRatio(ReadString(stream, "avg_frame_rate"))
                        ?? ParseRatio(ReadString(stream, "r_frame_rate"));
                    duration ??= ReadDouble(stream, "duration");
                }
                else if (type == "audio" && !hasAudio)
                {
                    hasAudio = true;
                    audioCodec = ReadString(stream, "codec_name");
                    duration ??= ReadDouble(stream, "duration");
                }
            }
        }

        return new MediaInfo
        {
            DurationSeconds = duration is > 0 ? duration : null,
            HasVideo = hasVideo,
            HasAudio = hasAudio,
            Width = width,
            Height = height,
            FrameRate = frameRate,
            VideoCodec = videoCodec,
            AudioCodec = audioCodec,
        };
    }

    // Cover art in audio files is reported as a video stream.
    private static bool IsAttachedPicture(JsonElement stream) =>
        stream.TryGetProperty("disposition", out JsonElement disposition)
        && disposition.TryGetProperty("attached_pic", out JsonElement attached)
        && attached.ValueKind == JsonValueKind.Number
        && attached.GetInt32() == 1;

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    // ffprobe writes numbers such as the duration as JSON strings.
    private static double? ReadDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(
                value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) => parsed,
            _ => null,
        };
    }

    private static double? ParseRatio(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        string[] parts = text.Split('/');
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double numerator))
        {
            return null;
        }

        double denominator = 1;
        if (parts.Length > 1
            && !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out denominator))
        {
            return null;
        }

        return denominator > 0 && numerator > 0 ? numerator / denominator : null;
    }
}
