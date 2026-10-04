using System.Globalization;

namespace FFConvert.Core;

/// <summary>Names of hardware encoders that were verified to work on this machine.</summary>
public sealed record HardwareEncoders(string? H264, string? Hevc)
{
    public static readonly HardwareEncoders None = new(null, null);

    public bool Any => H264 is not null || Hevc is not null;
}

/// <summary>Turns <see cref="ConversionSettings"/> into an ffmpeg command line.</summary>
public static class ArgumentBuilder
{
    public static List<string> Build(
        string inputPath,
        string outputPath,
        ConversionSettings settings,
        MediaInfo? info = null,
        HardwareEncoders? hardware = null)
    {
        OutputFormat format = FormatCatalog.Get(settings.Format);

        // stdin stays open on purpose: cancelling sends "q" so ffmpeg can stop cleanly.
        var args = new List<string> { "-hide_banner", "-y", "-loglevel", "warning", "-nostats", "-progress", "pipe:1" };

        double start = Math.Max(0, settings.TrimStartSeconds ?? 0);
        if (start > 0)
        {
            args.AddRange(["-ss", Number(start)]);
        }

        args.AddRange(["-i", inputPath]);

        if (settings.TrimEndSeconds is { } end && end > start)
        {
            args.AddRange(["-t", Number(end - start)]);
        }

        switch (format.Kind)
        {
            case MediaKind.Video when format.Id == "gif":
                AddGif(args, settings);
                break;
            case MediaKind.Video:
                AddVideo(args, format, settings, info, hardware);
                break;
            case MediaKind.Audio:
                AddAudioOnly(args, format, settings.Quality);
                break;
            case MediaKind.Image:
                AddImage(args, format, settings, info);
                break;
        }

        args.Add(outputPath);
        return args;
    }

    /// <summary>Length of the output in seconds, for progress reporting; null when unknown.</summary>
    public static double? ExpectedDuration(ConversionSettings settings, MediaInfo? info)
    {
        double start = Math.Max(0, settings.TrimStartSeconds ?? 0);
        double? end = settings.TrimEndSeconds is { } e && e > start ? e : null;

        if (info?.DurationSeconds is { } total)
        {
            double duration = Math.Min(end ?? total, total) - start;
            return duration > 0 ? duration : null;
        }

        return end - start;
    }

    /// <summary>Whether the conversion uses a video encoder a GPU could take over.</summary>
    public static bool CanUseHardware(ConversionSettings settings)
    {
        OutputFormat? format = FormatCatalog.Find(settings.Format);
        if (format is null || format.Kind != MediaKind.Video || format.VideoCodecs.Count == 0)
        {
            return false;
        }

        string codec = settings.VideoCodec ?? format.VideoCodecs[0];
        return codec is Codecs.H264 or Codecs.Hevc;
    }

    private static void AddVideo(
        List<string> args, OutputFormat format, ConversionSettings settings, MediaInfo? info, HardwareEncoders? hardware)
    {
        string videoCodec = Pick(settings.VideoCodec, format.VideoCodecs);
        string audioCodec = Pick(settings.AudioCodec, format.AudioCodecs);
        bool keepAudio = audioCodec != Codecs.None;

        args.AddRange(["-map", "0:v:0"]);
        if (keepAudio)
        {
            // The "?" keeps silent videos from failing the mapping.
            args.AddRange(["-map", "0:a:0?"]);
        }

        if (videoCodec == Codecs.Copy)
        {
            args.AddRange(["-c:v", "copy"]);
        }
        else
        {
            var filters = new List<string>();
            if (settings.FrameRate is { } fps && fps > 0)
            {
                filters.Add($"fps={Number(fps)}");
            }

            if (ScaleFilter(settings.MaxHeight, info, evenDimensions: true) is { } scale)
            {
                filters.Add(scale);
            }

            if (filters.Count > 0)
            {
                args.AddRange(["-vf", string.Join(',', filters)]);
            }

            AddVideoEncoder(args, videoCodec, settings.Quality, hardware);
            args.AddRange(["-pix_fmt", "yuv420p"]);
        }

        if (keepAudio)
        {
            AddAudioEncoder(args, audioCodec, settings.Quality);
        }

        if (format.Id is "mp4" or "mov")
        {
            args.AddRange(["-movflags", "+faststart"]);
        }
    }

    private static void AddVideoEncoder(List<string> args, string codec, Quality quality, HardwareEncoders? hardware)
    {
        string? hardwareEncoder = codec switch
        {
            Codecs.H264 => hardware?.H264,
            Codecs.Hevc => hardware?.Hevc,
            _ => null,
        };

        if (hardwareEncoder is not null)
        {
            AddHardwareEncoder(args, hardwareEncoder, codec, quality);
            return;
        }

        switch (codec)
        {
            case Codecs.H264:
                args.AddRange(["-c:v", "libx264", "-preset", "medium", "-crf", Level(quality, 30, 26, 23, 20, 17)]);
                break;
            case Codecs.Hevc:
                // hvc1 is the tag Apple players and Windows' own apps expect.
                args.AddRange(["-c:v", "libx265", "-preset", "medium", "-crf", Level(quality, 32, 29, 26, 23, 20), "-tag:v", "hvc1"]);
                break;
            case Codecs.Vp9:
                args.AddRange(["-c:v", "libvpx-vp9", "-crf", Level(quality, 40, 36, 32, 28, 24), "-b:v", "0",
                    "-deadline", "good", "-cpu-used", "2", "-row-mt", "1"]);
                break;
            case Codecs.Av1:
                args.AddRange(["-c:v", "libsvtav1", "-preset", "8", "-crf", Level(quality, 42, 38, 34, 30, 26)]);
                break;
            default:
                throw new ArgumentException($"Unknown video codec '{codec}'.");
        }
    }

    private static void AddHardwareEncoder(List<string> args, string encoder, string codec, Quality quality)
    {
        string level = codec == Codecs.Hevc
            ? Level(quality, 34, 30, 27, 24, 21)
            : Level(quality, 32, 28, 25, 22, 19);

        args.AddRange(["-c:v", encoder]);
        if (encoder.EndsWith("_nvenc", StringComparison.Ordinal))
        {
            args.AddRange(["-preset", "p5", "-rc", "vbr", "-cq", level, "-b:v", "0"]);
        }
        else if (encoder.EndsWith("_qsv", StringComparison.Ordinal))
        {
            args.AddRange(["-preset", "medium", "-global_quality", level]);
        }
        else if (encoder.EndsWith("_amf", StringComparison.Ordinal))
        {
            args.AddRange(["-quality", "balanced", "-rc", "cqp", "-qp_i", level, "-qp_p", level]);
        }

        if (codec == Codecs.Hevc)
        {
            args.AddRange(["-tag:v", "hvc1"]);
        }
    }

    private static void AddAudioEncoder(List<string> args, string codec, Quality quality)
    {
        switch (codec)
        {
            case Codecs.Copy:
                args.AddRange(["-c:a", "copy"]);
                break;
            case Codecs.Aac:
                args.AddRange(["-c:a", "aac", "-b:a", Level(quality, 96, 128, 160, 192, 256) + "k"]);
                break;
            case Codecs.Mp3:
                args.AddRange(["-c:a", "libmp3lame", "-q:a", Level(quality, 7, 5, 4, 2, 0)]);
                break;
            case Codecs.Opus:
                args.AddRange(["-c:a", "libopus", "-b:a", Level(quality, 64, 96, 128, 160, 192) + "k"]);
                break;
            case Codecs.Flac:
                args.AddRange(["-c:a", "flac"]);
                break;
            case Codecs.Pcm:
                args.AddRange(["-c:a", "pcm_s16le"]);
                break;
            default:
                throw new ArgumentException($"Unknown audio codec '{codec}'.");
        }
    }

    private static void AddAudioOnly(List<string> args, OutputFormat format, Quality quality)
    {
        // Mapping only the audio also drops cover art, which most of these containers reject.
        args.AddRange(["-map", "0:a:0"]);

        string codec = format.Id switch
        {
            "mp3" => Codecs.Mp3,
            "m4a" => Codecs.Aac,
            "opus" => Codecs.Opus,
            "flac" => Codecs.Flac,
            "wav" => Codecs.Pcm,
            _ => throw new ArgumentException($"Unknown audio format '{format.Id}'."),
        };
        AddAudioEncoder(args, codec, quality);
    }

    private static void AddGif(List<string> args, ConversionSettings settings)
    {
        (int fps, int width) = settings.Quality switch
        {
            Quality.Smallest => (8, 320),
            Quality.Small => (10, 400),
            Quality.Balanced => (12, 480),
            Quality.High => (15, 640),
            _ => (20, 800),
        };

        string rate = Number(settings.FrameRate is > 0 ? settings.FrameRate.Value : fps);
        string scale = settings.MaxHeight is { } height
            ? $"scale=-1:'min(ih,{height})':flags=lanczos"
            : $"scale='min(iw,{width})':-1:flags=lanczos";

        // A palette built from the clip itself avoids the banding of the default 256 colours.
        string filter = $"fps={rate},{scale},split[a][b];[a]palettegen=stats_mode=diff[p];"
            + "[b][p]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle";

        args.AddRange(["-map", "0:v:0", "-vf", filter, "-loop", "0"]);
    }

    private static void AddImage(List<string> args, OutputFormat format, ConversionSettings settings, MediaInfo? info)
    {
        args.AddRange(["-map", "0:v:0", "-frames:v", "1"]);

        if (ScaleFilter(settings.MaxHeight, info, evenDimensions: false) is { } scale)
        {
            args.AddRange(["-vf", scale]);
        }

        switch (format.Id)
        {
            case "png":
                // -update stops the image muxer from reading "%" in a file name as a sequence pattern.
                args.AddRange(["-c:v", "png", "-update", "1"]);
                break;
            case "jpg":
                args.AddRange(["-c:v", "mjpeg", "-q:v", Level(settings.Quality, 12, 8, 5, 3, 2), "-update", "1"]);
                break;
            case "webp":
                args.AddRange(["-c:v", "libwebp"]);
                args.AddRange(settings.Quality == Quality.Best
                    ? ["-lossless", "1"]
                    : ["-quality", Level(settings.Quality, 50, 65, 80, 90, 100)]);
                break;
            default:
                throw new ArgumentException($"Unknown image format '{format.Id}'.");
        }
    }

    private static string? ScaleFilter(int? maxHeight, MediaInfo? info, bool evenDimensions)
    {
        bool shrink = maxHeight is { } limit && (info?.Height is null || info.Height > limit);
        if (shrink)
        {
            return evenDimensions
                ? $"scale=-2:'trunc(min(ih,{maxHeight})/2)*2'"
                : $"scale=-1:'min(ih,{maxHeight})'";
        }

        // 4:2:0 encoders reject odd dimensions; round down when they are odd or unknown.
        bool mayBeOdd = info?.Width is null || info.Height is null || info.Width % 2 != 0 || info.Height % 2 != 0;
        return evenDimensions && mayBeOdd ? "scale=trunc(iw/2)*2:trunc(ih/2)*2" : null;
    }

    private static string Pick(string? requested, IReadOnlyList<string> available)
    {
        if (requested is not null && available.Contains(requested))
        {
            return requested;
        }

        return available.Count > 0 ? available[0] : Codecs.None;
    }

    private static string Level(Quality quality, int smallest, int small, int balanced, int high, int best) =>
        (quality switch
        {
            Quality.Smallest => smallest,
            Quality.Small => small,
            Quality.Balanced => balanced,
            Quality.High => high,
            _ => best,
        }).ToString(CultureInfo.InvariantCulture);

    // ffmpeg only understands "." as the decimal separator.
    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
