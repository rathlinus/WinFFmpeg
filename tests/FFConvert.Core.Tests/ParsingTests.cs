using FFConvert.Core;

namespace FFConvert.Core.Tests;

public class ProgressParserTests
{
    [Fact]
    public void SnapshotIsProducedAtTheEndOfEachBlock()
    {
        var parser = new ProgressParser();

        Assert.Null(parser.Feed("frame=120"));
        Assert.Null(parser.Feed("out_time_us=4000000"));
        Assert.Null(parser.Feed("total_size=1048576"));
        Assert.Null(parser.Feed("speed=2.5x"));
        ProgressSnapshot? snapshot = parser.Feed("progress=continue");

        Assert.NotNull(snapshot);
        Assert.Equal(TimeSpan.FromSeconds(4), snapshot.Value.OutTime);
        Assert.Equal(2.5, snapshot.Value.Speed);
        Assert.Equal(1048576, snapshot.Value.TotalSize);
        Assert.False(snapshot.Value.Finished);
    }

    [Fact]
    public void LastBlockIsMarkedFinished()
    {
        var parser = new ProgressParser();
        parser.Feed("out_time_us=1000000");

        Assert.True(parser.Feed("progress=end")!.Value.Finished);
    }

    [Fact]
    public void OldBuildsReportMicrosecondsUnderTheMillisecondName()
    {
        var parser = new ProgressParser();
        parser.Feed("out_time_ms=2500000");

        Assert.Equal(TimeSpan.FromSeconds(2.5), parser.Feed("progress=continue")!.Value.OutTime);
    }

    [Theory]
    [InlineData("out_time_us=N/A")]
    [InlineData("speed=N/A")]
    [InlineData("out_time_us=-9223372036854775808")]
    [InlineData("not a progress line")]
    [InlineData("")]
    public void UnusableValuesAreIgnored(string line)
    {
        var parser = new ProgressParser();
        parser.Feed(line);
        ProgressSnapshot snapshot = parser.Feed("progress=continue")!.Value;

        Assert.Null(snapshot.OutTime);
        Assert.Null(snapshot.Speed);
    }

    [Fact]
    public void SpeedWithPaddingIsRead()
    {
        var parser = new ProgressParser();
        parser.Feed("speed=   1.07x");

        Assert.Equal(1.07, parser.Feed("progress=continue")!.Value.Speed);
    }
}

public class FfprobeParserTests
{
    private const string Video = """
        {
          "streams": [
            { "index": 0, "codec_name": "h264", "codec_type": "video", "width": 1920, "height": 1080,
              "r_frame_rate": "30000/1001", "avg_frame_rate": "30000/1001", "disposition": { "attached_pic": 0 } },
            { "index": 1, "codec_name": "aac", "codec_type": "audio", "disposition": { "attached_pic": 0 } }
          ],
          "format": { "duration": "12.345000" }
        }
        """;

    private const string AudioWithCover = """
        {
          "streams": [
            { "index": 0, "codec_name": "mp3", "codec_type": "audio", "duration": "200.5" },
            { "index": 1, "codec_name": "mjpeg", "codec_type": "video", "width": 600, "height": 600,
              "avg_frame_rate": "0/0", "disposition": { "attached_pic": 1 } }
          ],
          "format": { }
        }
        """;

    [Fact]
    public void ReadsVideoProperties()
    {
        MediaInfo info = FfprobeParser.Parse(Video);

        Assert.True(info.HasVideo);
        Assert.True(info.HasAudio);
        Assert.Equal(1920, info.Width);
        Assert.Equal(1080, info.Height);
        Assert.Equal("h264", info.VideoCodec);
        Assert.Equal("aac", info.AudioCodec);
        Assert.Equal(12.345, info.DurationSeconds);
        Assert.Equal(29.97, info.FrameRate!.Value, precision: 2);
    }

    [Fact]
    public void CoverArtIsNotAVideoStream()
    {
        MediaInfo info = FfprobeParser.Parse(AudioWithCover);

        Assert.False(info.HasVideo);
        Assert.True(info.HasAudio);
        Assert.Null(info.Width);
    }

    [Fact]
    public void DurationFallsBackToTheStream()
    {
        Assert.Equal(200.5, FfprobeParser.Parse(AudioWithCover).DurationSeconds);
    }

    [Fact]
    public void EmptyOutputGivesAnEmptyResult()
    {
        MediaInfo info = FfprobeParser.Parse("{}");

        Assert.False(info.HasVideo);
        Assert.False(info.HasAudio);
        Assert.Null(info.DurationSeconds);
    }
}

public class TimeTextTests
{
    [Theory]
    [InlineData("90", 90.0)]
    [InlineData("1:30", 90.0)]
    [InlineData("0:01:30", 90.0)]
    [InlineData("1:02:03.5", 3723.5)]
    [InlineData("12,5", 12.5)]
    [InlineData(" 7 ", 7.0)]
    public void ParsesTimes(string text, double expected)
    {
        Assert.True(TimeText.TryParse(text, out double? seconds));
        Assert.Equal(expected, seconds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyMeansNoTime(string? text)
    {
        Assert.True(TimeText.TryParse(text, out double? seconds));
        Assert.Null(seconds);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1:75")]
    [InlineData("1:2:3:4")]
    [InlineData("-5")]
    [InlineData("1.5:30")]
    public void RejectsWhatIsNotATime(string text)
    {
        Assert.False(TimeText.TryParse(text, out _));
    }

    [Theory]
    [InlineData(32, "0:32")]
    [InlineData(605, "10:05")]
    [InlineData(3725, "1:02:05")]
    public void FormatsLikeAClock(int seconds, string expected)
    {
        Assert.Equal(expected, TimeText.Format(TimeSpan.FromSeconds(seconds)));
    }
}
