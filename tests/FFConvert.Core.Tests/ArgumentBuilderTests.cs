using System.Globalization;
using FFConvert.Core;

namespace FFConvert.Core.Tests;

public class ArgumentBuilderTests
{
    private static readonly MediaInfo s_hd = new() { HasVideo = true, HasAudio = true, Width = 1920, Height = 1080, DurationSeconds = 60 };

    private static string Build(ConversionSettings settings, MediaInfo? info = null, HardwareEncoders? hardware = null) =>
        string.Join(' ', ArgumentBuilder.Build("in.mkv", "out.partial", settings, info, hardware));

    [Fact]
    public void Mp4DefaultsToH264AndAac()
    {
        string args = Build(new ConversionSettings { Format = "mp4" }, s_hd);

        Assert.Contains("-i in.mkv", args);
        Assert.Contains("-map 0:v:0 -map 0:a:0?", args);
        Assert.Contains("-c:v libx264 -preset medium -crf 23", args);
        Assert.Contains("-pix_fmt yuv420p", args);
        Assert.Contains("-c:a aac -b:a 160k", args);
        Assert.Contains("-movflags +faststart", args);
        Assert.EndsWith("out.partial", args);
    }

    [Fact]
    public void ReportsProgressOnStandardOutput()
    {
        string args = Build(new ConversionSettings { Format = "mp4" });

        Assert.StartsWith("-hide_banner -y -loglevel warning -nostats -progress pipe:1", args);
    }

    [Fact]
    public void EvenSourceNeedsNoScaleFilter()
    {
        string args = Build(new ConversionSettings { Format = "mp4" }, s_hd);

        Assert.DoesNotContain("-vf", args);
    }

    [Theory]
    [InlineData(641, 360)]
    [InlineData(640, 361)]
    public void OddSourceIsRoundedToEvenDimensions(int width, int height)
    {
        string args = Build(new ConversionSettings { Format = "mp4" }, s_hd with { Width = width, Height = height });

        Assert.Contains("-vf scale=trunc(iw/2)*2:trunc(ih/2)*2", args);
    }

    [Fact]
    public void UnknownSourceIsRoundedToEvenDimensions()
    {
        string args = Build(new ConversionSettings { Format = "mp4" }, info: null);

        Assert.Contains("-vf scale=trunc(iw/2)*2:trunc(ih/2)*2", args);
    }

    [Fact]
    public void MaxHeightScalesLargerSourcesDown()
    {
        string args = Build(new ConversionSettings { Format = "mp4", MaxHeight = 720 }, s_hd);

        Assert.Contains("-vf scale=-2:'trunc(min(ih,720)/2)*2'", args);
    }

    [Fact]
    public void MaxHeightLeavesSmallerSourcesAlone()
    {
        string args = Build(new ConversionSettings { Format = "mp4", MaxHeight = 1440 }, s_hd);

        Assert.DoesNotContain("-vf", args);
    }

    [Fact]
    public void HevcIsTaggedForAppleAndWindowsPlayers()
    {
        string args = Build(new ConversionSettings { Format = "mp4", VideoCodec = Codecs.Hevc }, s_hd);

        Assert.Contains("-c:v libx265", args);
        Assert.Contains("-tag:v hvc1", args);
    }

    [Fact]
    public void WebmUsesVp9AndOpus()
    {
        string args = Build(new ConversionSettings { Format = "webm" }, s_hd);

        Assert.Contains("-c:v libvpx-vp9 -crf 32 -b:v 0", args);
        Assert.Contains("-c:a libopus -b:a 128k", args);
        Assert.DoesNotContain("-movflags", args);
    }

    [Fact]
    public void CopyCodecSkipsFiltersAndEncoderOptions()
    {
        string args = Build(
            new ConversionSettings { Format = "mp4", VideoCodec = Codecs.Copy, AudioCodec = Codecs.Copy, MaxHeight = 720, FrameRate = 30 },
            s_hd);

        Assert.Contains("-c:v copy", args);
        Assert.Contains("-c:a copy", args);
        Assert.DoesNotContain("-vf", args);
        Assert.DoesNotContain("-crf", args);
        Assert.DoesNotContain("-pix_fmt", args);
    }

    [Fact]
    public void NoAudioDropsTheAudioStream()
    {
        string args = Build(new ConversionSettings { Format = "mp4", AudioCodec = Codecs.None }, s_hd);

        Assert.DoesNotContain("0:a", args);
        Assert.DoesNotContain("-c:a", args);
    }

    [Fact]
    public void CodecNotOfferedByTheFormatFallsBackToItsDefault()
    {
        string args = Build(new ConversionSettings { Format = "webm", VideoCodec = Codecs.H264 }, s_hd);

        Assert.Contains("-c:v libvpx-vp9", args);
    }

    [Fact]
    public void QualityChangesTheRateFactor()
    {
        Assert.Contains("-crf 30", Build(new ConversionSettings { Format = "mp4", Quality = Quality.Smallest }, s_hd));
        Assert.Contains("-crf 17", Build(new ConversionSettings { Format = "mp4", Quality = Quality.Best }, s_hd));
    }

    [Fact]
    public void HardwareEncoderReplacesTheSoftwareOne()
    {
        string args = Build(new ConversionSettings { Format = "mp4" }, s_hd, new HardwareEncoders("h264_nvenc", null));

        Assert.Contains("-c:v h264_nvenc", args);
        Assert.Contains("-cq 25", args);
        Assert.DoesNotContain("libx264", args);
    }

    [Fact]
    public void HardwareEncoderIsOnlyUsedForItsOwnCodec()
    {
        string args = Build(
            new ConversionSettings { Format = "mp4", VideoCodec = Codecs.Hevc }, s_hd, new HardwareEncoders("h264_nvenc", null));

        Assert.Contains("-c:v libx265", args);
    }

    [Theory]
    [InlineData("mp4", null, true)]
    [InlineData("mp4", Codecs.Hevc, true)]
    [InlineData("mp4", Codecs.Copy, false)]
    [InlineData("webm", null, false)]
    [InlineData("gif", null, false)]
    [InlineData("mp3", null, false)]
    public void HardwareAppliesOnlyToH264AndHevc(string format, string? codec, bool expected)
    {
        Assert.Equal(expected, ArgumentBuilder.CanUseHardware(new ConversionSettings { Format = format, VideoCodec = codec }));
    }

    [Fact]
    public void GifBuildsItsOwnPalette()
    {
        string args = Build(new ConversionSettings { Format = "gif" }, s_hd);

        Assert.Contains("fps=12,scale='min(iw,480)':-1:flags=lanczos,split[a][b];[a]palettegen", args);
        Assert.Contains("paletteuse", args);
        Assert.Contains("-loop 0", args);
        Assert.DoesNotContain("-c:a", args);
    }

    [Theory]
    [InlineData("mp3", "-c:a libmp3lame -q:a 4")]
    [InlineData("m4a", "-c:a aac -b:a 160k")]
    [InlineData("opus", "-c:a libopus -b:a 128k")]
    [InlineData("flac", "-c:a flac")]
    [InlineData("wav", "-c:a pcm_s16le")]
    public void AudioFormatsMapOnlyTheAudioStream(string format, string expected)
    {
        string args = Build(new ConversionSettings { Format = format }, s_hd);

        Assert.Contains("-map 0:a:0", args);
        Assert.DoesNotContain("0:v", args);
        Assert.Contains(expected, args);
    }

    [Theory]
    [InlineData("png", "-c:v png -update 1")]
    [InlineData("jpg", "-c:v mjpeg -q:v 5 -update 1")]
    [InlineData("webp", "-c:v libwebp -quality 80")]
    public void ImageFormatsWriteASingleFrame(string format, string expected)
    {
        string args = Build(new ConversionSettings { Format = format });

        Assert.Contains("-frames:v 1", args);
        Assert.Contains(expected, args);
    }

    [Fact]
    public void BestQualityWebpIsLossless()
    {
        string args = Build(new ConversionSettings { Format = "webp", Quality = Quality.Best });

        Assert.Contains("-lossless 1", args);
    }

    [Fact]
    public void TrimSeeksBeforeTheInputAndLimitsTheDuration()
    {
        List<string> args = ArgumentBuilder.Build(
            "in.mkv", "out.mp4", new ConversionSettings { Format = "mp4", TrimStartSeconds = 30, TrimEndSeconds = 105.5 }, s_hd);

        Assert.True(args.IndexOf("-ss") < args.IndexOf("-i"));
        Assert.Equal("30", args[args.IndexOf("-ss") + 1]);
        Assert.True(args.IndexOf("-t") > args.IndexOf("-i"));
        Assert.Equal("75.5", args[args.IndexOf("-t") + 1]);
    }

    [Fact]
    public void NumbersUseADecimalPointInEveryCulture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            // German writes 23,976; ffmpeg would read that as two arguments.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            string args = Build(new ConversionSettings { Format = "mp4", FrameRate = 23.976, TrimStartSeconds = 1.5 }, s_hd);

            Assert.Contains("-ss 1.5", args);
            Assert.Contains("fps=23.976", args);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(null, null, 60.0)]
    [InlineData(10.0, null, 50.0)]
    [InlineData(10.0, 40.0, 30.0)]
    [InlineData(null, 90.0, 60.0)]
    public void ExpectedDurationAccountsForTheTrim(double? start, double? end, double expected)
    {
        var settings = new ConversionSettings { TrimStartSeconds = start, TrimEndSeconds = end };

        Assert.Equal(expected, ArgumentBuilder.ExpectedDuration(settings, s_hd));
    }

    [Fact]
    public void ExpectedDurationIsUnknownWithoutProbeOrTrim()
    {
        Assert.Null(ArgumentBuilder.ExpectedDuration(new ConversionSettings(), info: null));
    }

    [Fact]
    public void UnknownFormatIsRejected()
    {
        Assert.Throws<ArgumentException>(() => Build(new ConversionSettings { Format = "exe" }));
    }
}
