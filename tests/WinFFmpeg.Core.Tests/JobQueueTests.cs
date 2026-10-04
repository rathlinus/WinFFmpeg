using System.Diagnostics;
using WinFFmpeg.Core;

namespace WinFFmpeg.Core.Tests;

/// <summary>Source files for the conversion tests, generated once with ffmpeg's test sources.</summary>
public sealed class MediaFixture : IDisposable
{
    public MediaFixture()
    {
        Ffmpeg = FfmpegLocator.Locate(null);
        Folder = Path.Combine(Path.GetTempPath(), $"winffmpeg-media-{Guid.NewGuid():N}");
        if (Ffmpeg is null)
        {
            return;
        }

        Directory.CreateDirectory(Folder);
        Generate("-f", "lavfi", "-i", "testsrc2=size=640x360:rate=30:duration=2",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=2", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", Short);

        // Odd height and 4:4:4 chroma: both have to be converted for a player-friendly MP4.
        Generate("-f", "lavfi", "-i", "testsrc2=size=640x361:rate=30:duration=2",
            "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv444p", Odd);

        Generate("-f", "lavfi", "-i", "testsrc2=size=1920x1080:rate=30:duration=60",
            "-c:v", "libx264", "-preset", "ultrafast", Long);
        Generate("-f", "lavfi", "-i", "sine=frequency=440:duration=2", Tone);
        Generate("-f", "lavfi", "-i", "testsrc2=size=320x240", "-frames:v", "1", "-update", "1", Picture);
        File.WriteAllText(Broken, "this is not a video");
    }

    public FfmpegPaths? Ffmpeg { get; }

    public string Folder { get; }

    public string Short => Path.Combine(Folder, "short.mkv");

    public string Odd => Path.Combine(Folder, "odd.mkv");

    public string Long => Path.Combine(Folder, "long.mkv");

    public string Tone => Path.Combine(Folder, "tone.wav");

    public string Picture => Path.Combine(Folder, "picture.png");

    public string Broken => Path.Combine(Folder, "broken.mp4");

    public void Dispose()
    {
        if (Directory.Exists(Folder))
        {
            Directory.Delete(Folder, recursive: true);
        }
    }

    private void Generate(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(Ffmpeg!.Ffmpeg) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in (string[])["-hide_banner", "-loglevel", "error", "-y", .. arguments])
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}

/// <summary>
/// Runs real conversions. They need ffmpeg on the machine and do nothing when it is missing.
/// </summary>
public sealed class JobQueueTests : IClassFixture<MediaFixture>, IDisposable
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromMinutes(2);

    private readonly MediaFixture _media;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"winffmpeg-jobs-{Guid.NewGuid():N}");
    private readonly AppSettings _settings = new();

    public JobQueueTests(MediaFixture media)
    {
        _media = media;
        Directory.CreateDirectory(_folder);
    }

    private bool HasFfmpeg => _media.Ffmpeg is not null;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    /// <summary>Copies a source into this test's folder, so outputs of different tests never collide.</summary>
    private string Input(string source, string? name = null)
    {
        string path = Path.Combine(_folder, name ?? Path.GetFileName(source));
        File.Copy(source, path);
        return path;
    }

    private async Task<ConversionJob> ConvertAsync(string input, ConversionSettings settings)
    {
        var queue = new JobQueue(() => _settings);
        var drained = new TaskCompletionSource<QueueSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Drained += drained.SetResult;

        ConversionJob job = Assert.Single(queue.Enqueue([input], settings));
        await drained.Task.WaitAsync(s_timeout);
        return job;
    }

    private async Task<MediaInfo> ProbeAsync(string path)
    {
        MediaInfo? info = await MediaProber.ProbeAsync(_media.Ffmpeg!.Ffprobe!, path, CancellationToken.None);
        return Assert.IsType<MediaInfo>(info);
    }

    private string[] FilesInFolder() => [.. Directory.GetFiles(_folder).Select(Path.GetFileName).Order()!];

    [Fact]
    public async Task ConvertsVideoToMp4()
    {
        if (!HasFfmpeg) return;
        string input = Input(_media.Short);

        ConversionJob job = await ConvertAsync(input, new() { Format = "mp4" });

        Assert.True(job.State == JobState.Completed, job.Log);
        Assert.Equal(Path.Combine(_folder, "short.mp4"), job.OutputPath);
        Assert.Equal(1, job.Progress);
        Assert.Equal(["short.mkv", "short.mp4"], FilesInFolder());

        MediaInfo info = await ProbeAsync(job.OutputPath);
        Assert.Equal("h264", info.VideoCodec);
        Assert.Equal("aac", info.AudioCodec);
        Assert.Equal(2, info.DurationSeconds!.Value, precision: 0);
    }

    [Fact]
    public async Task OddSizedVideoIsMadeEven()
    {
        if (!HasFfmpeg) return;
        string input = Input(_media.Odd);

        ConversionJob job = await ConvertAsync(input, new() { Format = "mp4" });

        Assert.True(job.State == JobState.Completed, job.Log);
        MediaInfo info = await ProbeAsync(job.OutputPath);
        Assert.Equal(640, info.Width);
        Assert.Equal(360, info.Height);
    }

    [Fact]
    public async Task ScalesDownToTheMaximumHeight()
    {
        if (!HasFfmpeg) return;
        string input = Input(_media.Short);

        ConversionJob job = await ConvertAsync(input, new() { Format = "mp4", MaxHeight = 180, Suffix = " (small)" });

        Assert.True(job.State == JobState.Completed, job.Log);
        Assert.Equal(Path.Combine(_folder, "short (small).mp4"), job.OutputPath);
        MediaInfo info = await ProbeAsync(job.OutputPath);
        Assert.Equal(320, info.Width);
        Assert.Equal(180, info.Height);
    }

    [Theory]
    [InlineData("mp4", Codecs.Hevc, "hevc")]
    [InlineData("webm", null, "vp9")]
    [InlineData("mkv", Codecs.Copy, "h264")]
    [InlineData("gif", null, "gif")]
    public async Task ConvertsToEachVideoFormat(string format, string? codec, string expectedCodec)
    {
        if (!HasFfmpeg) return;
        string input = Input(_media.Short, "source.mov");

        ConversionJob job = await ConvertAsync(input, new() { Format = format, VideoCodec = codec, Quality = Quality.Small });

        Assert.True(job.State == JobState.Completed, job.Log);
        Assert.Equal(expectedCodec, (await ProbeAsync(job.OutputPath)).VideoCodec);
    }

    [Theory]
    [InlineData("mp3", "mp3")]
    [InlineData("m4a", "aac")]
    [InlineData("opus", "opus")]
    [InlineData("flac", "flac")]
    public async Task ExtractsAudioFromVideo(string format, string expectedCodec)
    {
        if (!HasFfmpeg) return;
        string input = Input(_media.Short);

        ConversionJob job = await ConvertAsync(input, new() { Format = format });

        Assert.True(job.State == JobState.Completed, job.Log);
        MediaInfo info = await ProbeAsync(job.OutputPath);
        Assert.False(info.HasVideo);
        Assert.Equal(expectedCodec, info.AudioCodec);
    }

    [Theory]
    [InlineData("jpg", "mjpeg")]
    [InlineData("webp", "webp")]
    public async Task ConvertsImages(string format, string expectedCodec)
    {
        if (!HasFfmpeg) return;
        string input = Input(_media.Picture);

        ConversionJob job = await ConvertAsync(input, new() { Format = format });

        Assert.True(job.State == JobState.Completed, job.Log);
        MediaInfo info = await ProbeAsync(job.OutputPath);
        Assert.Equal(expectedCodec, info.VideoCodec);
        Assert.Equal(320, info.Width);
    }

    [Fact]
    public async Task HandlesPercentSignsAndUnicodeInFileNames()
    {
        if (!HasFfmpeg) return;
        string input = Input(_media.Picture, "100% süß %03d.png");

        ConversionJob job = await ConvertAsync(input, new() { Format = "jpg" });

        Assert.True(job.State == JobState.Completed, job.Log);
        Assert.True(File.Exists(Path.Combine(_folder, "100% süß %03d.jpg")));
    }

    [Fact]
    public async Task TrimKeepsOnlyTheChosenPart()
    {
        if (!HasFfmpeg) return;
        string input = Input(_media.Tone);

        ConversionJob job = await ConvertAsync(input, new() { Format = "flac", TrimStartSeconds = 0.5, TrimEndSeconds = 1.5 });

        Assert.True(job.State == JobState.Completed, job.Log);
        Assert.Equal(1, (await ProbeAsync(job.OutputPath)).DurationSeconds!.Value, precision: 1);
    }

    [Fact]
    public async Task NeverOverwritesAnExistingFile()
    {
        if (!HasFfmpeg) return;
        string input = Input(_media.Short, "clip.mp4");

        ConversionJob job = await ConvertAsync(input, new() { Format = "mp4" });

        Assert.True(job.State == JobState.Completed, job.Log);
        Assert.Equal(Path.Combine(_folder, "clip (1).mp4"), job.OutputPath);
        Assert.Equal(new FileInfo(_media.Short).Length, new FileInfo(input).Length);
        Assert.Equal(["clip (1).mp4", "clip.mp4"], FilesInFolder());
    }

    [Fact]
    public async Task InputsWithTheSameNameGetDifferentOutputs()
    {
        if (!HasFfmpeg) return;
        string first = Input(_media.Short, "clip.mkv");
        string second = Input(_media.Short, "clip.mov");
        var queue = new JobQueue(() => _settings);
        var drained = new TaskCompletionSource<QueueSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Drained += drained.SetResult;

        IReadOnlyList<ConversionJob> jobs = queue.Enqueue([first, second], new() { Format = "mp4" });
        QueueSummary summary = await drained.Task.WaitAsync(s_timeout);

        Assert.Equal(2, summary.Completed);
        Assert.Equal(Path.Combine(_folder, "clip.mp4"), jobs[0].OutputPath);
        Assert.Equal(Path.Combine(_folder, "clip (1).mp4"), jobs[1].OutputPath);
        Assert.All(jobs, j => Assert.True(File.Exists(j.OutputPath)));
    }

    [Fact]
    public async Task WritesToTheConfiguredOutputFolder()
    {
        if (!HasFfmpeg) return;
        string input = Input(_media.Tone);
        _settings.OutputFolder = Path.Combine(_folder, "converted", "audio");

        ConversionJob job = await ConvertAsync(input, new() { Format = "mp3" });

        Assert.True(job.State == JobState.Completed, job.Log);
        Assert.True(File.Exists(Path.Combine(_settings.OutputFolder, "tone.mp3")));
    }

    [Fact]
    public async Task FailureIsExplainedAndLeavesNothingBehind()
    {
        if (!HasFfmpeg) return;
        string input = Input(_media.Broken);

        ConversionJob job = await ConvertAsync(input, new() { Format = "mkv" });

        Assert.Equal(JobState.Failed, job.State);
        Assert.False(string.IsNullOrWhiteSpace(job.Error));
        Assert.Contains("ffmpeg -hide_banner", job.Log);
        Assert.Equal(["broken.mp4"], FilesInFolder());
    }

    [Fact]
    public async Task ReportsProgressWhileConverting()
    {
        if (!HasFfmpeg) return;
        string input = Input(_media.Long);
        var queue = new JobQueue(() => _settings);
        var drained = new TaskCompletionSource<QueueSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new List<double>();
        queue.Drained += drained.SetResult;
        queue.JobChanged += j =>
        {
            if (j.Progress is { } progress)
            {
                lock (seen)
                {
                    seen.Add(progress);
                }
            }
        };

        ConversionJob job = Assert.Single(queue.Enqueue([input], new() { Format = "mp4", MaxHeight = 720 }));
        await drained.Task.WaitAsync(s_timeout);

        Assert.True(job.State == JobState.Completed, job.Log);
        Assert.Contains(seen, p => p is > 0 and < 1);
        Assert.Equal(seen.Order(), seen);
        Assert.Equal(1, seen[^1]);
    }

    [Fact]
    public async Task CancellingStopsFfmpegAndDeletesThePartialFile()
    {
        if (!HasFfmpeg) return;
        string input = Input(_media.Long);
        var queue = new JobQueue(() => _settings);
        var drained = new TaskCompletionSource<QueueSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Drained += drained.SetResult;
        queue.JobChanged += j =>
        {
            if (j.Progress is > 0)
            {
                started.TrySetResult();
            }
        };

        // Slow on purpose, so the conversion is certainly still running when it is cancelled.
        ConversionJob job = Assert.Single(queue.Enqueue(
            [input], new() { Format = "mp4", VideoCodec = Codecs.Hevc, Quality = Quality.Best }));
        await started.Task.WaitAsync(s_timeout);
        Assert.True(File.Exists(Path.Combine(_folder, "long.partial.mp4")));

        queue.Cancel(job);
        QueueSummary summary = await drained.Task.WaitAsync(s_timeout);

        Assert.Equal(JobState.Canceled, job.State);
        Assert.Equal(1, summary.Canceled);
        Assert.Equal(["long.mkv"], FilesInFolder());
    }

    [Fact]
    public async Task CancellingAPendingJobNeverStartsIt()
    {
        if (!HasFfmpeg) return;
        string first = Input(_media.Short, "first.mkv");
        string second = Input(_media.Short, "second.mkv");
        var queue = new JobQueue(() => _settings);
        var drained = new TaskCompletionSource<QueueSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Drained += drained.SetResult;

        IReadOnlyList<ConversionJob> jobs = queue.Enqueue([first, second], new() { Format = "mp4" });
        queue.Cancel(jobs[1]);
        QueueSummary summary = await drained.Task.WaitAsync(s_timeout);

        Assert.Equal(JobState.Completed, jobs[0].State);
        Assert.Equal(JobState.Canceled, jobs[1].State);
        Assert.Equal(1, summary.Completed);
        Assert.False(File.Exists(Path.Combine(_folder, "second.mp4")));
    }

    [Fact]
    public void FilesTheFormatCannotTakeAreSkipped()
    {
        var queue = new JobQueue(() => _settings);

        IReadOnlyList<ConversionJob> jobs = queue.Enqueue(
            [Path.Combine(_folder, "notes.txt"), Path.Combine(_folder, "photo.png")], new() { Format = "mp4" });

        Assert.Empty(jobs);
        Assert.False(queue.IsBusy);
    }

    [Theory]
    [InlineData("[libx264 @ 000001f2c8a1b2c0] height not divisible by 2 (640x361)\nConversion failed!", "height not divisible by 2 (640x361)")]
    [InlineData("in.mp4: Invalid data found when processing input", "in.mp4: Invalid data found when processing input")]
    [InlineData("", "ffmpeg failed without an error message.")]
    public void ErrorSummaryIsTheLineThatExplainsTheFailure(string standardError, string expected)
    {
        Assert.Equal(expected, JobQueue.SummarizeError(standardError));
    }
}
