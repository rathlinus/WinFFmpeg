using System.Text;

namespace WinFFmpeg.Core;

public enum JobState
{
    Pending,
    Running,
    Completed,
    Failed,
    Canceled,
}

public sealed class ConversionJob
{
    internal ConversionJob(string inputPath, string outputPath, ConversionSettings settings)
    {
        InputPath = inputPath;
        OutputPath = outputPath;
        Settings = settings;
    }

    public string InputPath { get; }

    public string OutputPath { get; internal set; }

    public ConversionSettings Settings { get; }

    public JobState State { get; internal set; }

    /// <summary>0 to 1, or null while the total length is unknown.</summary>
    public double? Progress { get; internal set; }

    /// <summary>Encoding speed relative to playback speed.</summary>
    public double? Speed { get; internal set; }

    public TimeSpan? Remaining { get; internal set; }

    /// <summary>One-line reason when the job failed.</summary>
    public string? Error { get; internal set; }

    /// <summary>The ffmpeg command line and what it printed, for the details view.</summary>
    public string? Log { get; internal set; }

    public bool IsFinished => State is JobState.Completed or JobState.Failed or JobState.Canceled;

    internal CancellationTokenSource Cancellation { get; } = new();
}

/// <summary>The jobs that ran between the queue becoming busy and becoming idle again.</summary>
public sealed record QueueSummary(IReadOnlyList<ConversionJob> Jobs)
{
    public int Completed => Jobs.Count(j => j.State == JobState.Completed);

    public int Failed => Jobs.Count(j => j.State == JobState.Failed);

    public int Canceled => Jobs.Count(j => j.State == JobState.Canceled);
}

/// <summary>
/// Converts files one after another. ffmpeg already uses every core for a single
/// encode, so running jobs in parallel would only make each of them slower.
/// Events are raised on a thread-pool thread.
/// </summary>
public sealed class JobQueue
{
    public const string FfmpegNotFoundMessage = "ffmpeg was not found. Set its location in Settings.";

    private readonly Func<AppSettings> _settings;
    private readonly object _gate = new();
    private readonly Queue<ConversionJob> _pending = new();
    private readonly List<ConversionJob> _batch = [];
    private readonly HashSet<string> _reserved = new(StringComparer.OrdinalIgnoreCase);
    private ConversionJob? _current;
    private bool _running;
    private string? _hardwareFor;
    private Task<HardwareEncoders>? _hardware;

    public JobQueue(Func<AppSettings> settings)
    {
        _settings = settings;
    }

    public event Action<ConversionJob>? JobChanged;

    public event Action<QueueSummary>? Drained;

    public bool IsBusy
    {
        get
        {
            lock (_gate)
            {
                return _running;
            }
        }
    }

    /// <summary>
    /// Queues one job per input the target format accepts and returns them.
    /// Files of other kinds are skipped.
    /// </summary>
    public IReadOnlyList<ConversionJob> Enqueue(IEnumerable<string> inputPaths, ConversionSettings settings)
    {
        OutputFormat format = FormatCatalog.Get(settings.Format);
        string? outputFolder = _settings().OutputFolder;
        var added = new List<ConversionJob>();

        lock (_gate)
        {
            foreach (string inputPath in inputPaths)
            {
                if ((MediaTypes.Classify(inputPath) & format.Accepts) == 0)
                {
                    continue;
                }

                string outputPath = OutputPathResolver.Resolve(
                    inputPath, format.Extension, outputFolder, settings.Suffix, IsTaken);
                _reserved.Add(outputPath);

                var job = new ConversionJob(inputPath, outputPath, settings);
                _pending.Enqueue(job);
                _batch.Add(job);
                added.Add(job);
            }

            if (added.Count > 0 && !_running)
            {
                _running = true;
                _ = Task.Run(WorkerAsync);
            }
        }

        return added;
    }

    public void Cancel(ConversionJob job)
    {
        bool wasPending;
        lock (_gate)
        {
            wasPending = job.State == JobState.Pending;
            if (wasPending)
            {
                job.State = JobState.Canceled;
            }
        }

        if (wasPending)
        {
            JobChanged?.Invoke(job);
        }
        else if (job.State == JobState.Running)
        {
            job.Cancellation.Cancel();
        }
    }

    public void CancelAll()
    {
        List<ConversionJob> jobs;
        lock (_gate)
        {
            jobs = [.. _pending];
            if (_current is not null)
            {
                jobs.Add(_current);
            }
        }

        foreach (ConversionJob job in jobs)
        {
            Cancel(job);
        }
    }

    private bool IsTaken(string path) => File.Exists(path) || _reserved.Contains(path);

    private async Task WorkerAsync()
    {
        while (true)
        {
            ConversionJob job;
            lock (_gate)
            {
                if (_pending.Count == 0)
                {
                    _running = false;
                    _current = null;
                    break;
                }

                job = _pending.Dequeue();
                _current = job;
            }

            if (job.State != JobState.Canceled)
            {
                try
                {
                    await RunJobAsync(job);
                }
                catch (Exception e)
                {
                    Fail(job, e.Message, e.ToString());
                }
            }

            lock (_gate)
            {
                _reserved.Remove(job.OutputPath);
            }
        }

        QueueSummary summary;
        lock (_gate)
        {
            // More work may have arrived after the loop ended; its worker reports the batch.
            if (_running || _batch.Count == 0)
            {
                return;
            }

            summary = new QueueSummary([.. _batch]);
            _batch.Clear();
        }

        Drained?.Invoke(summary);
    }

    private async Task RunJobAsync(ConversionJob job)
    {
        AppSettings settings = _settings();
        CancellationToken cancellationToken = job.Cancellation.Token;

        lock (_gate)
        {
            if (job.State == JobState.Canceled)
            {
                return;
            }

            job.State = JobState.Running;
        }

        JobChanged?.Invoke(job);

        FfmpegPaths? paths = FfmpegLocator.Locate(settings.FfmpegPath);
        if (paths is null)
        {
            Fail(job, FfmpegNotFoundMessage, null);
            return;
        }

        if (!File.Exists(job.InputPath))
        {
            Fail(job, "The file no longer exists.", null);
            return;
        }

        MediaInfo? info = paths.Ffprobe is null
            ? null
            : await MediaProber.ProbeAsync(paths.Ffprobe, job.InputPath, cancellationToken);

        HardwareEncoders? hardware = null;
        if (settings.UseHardwareEncoding && ArgumentBuilder.CanUseHardware(job.Settings))
        {
            hardware = await GetHardwareAsync(paths.Ffmpeg);
        }

        string partialPath = OutputPathResolver.PartialPathFor(job.OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(job.OutputPath)!);

        ProcessResult result = await EncodeAsync(job, paths.Ffmpeg, partialPath, info, hardware, cancellationToken);
        if (result.ExitCode != 0 && !result.Canceled && hardware is { Any: true })
        {
            // GPU encoders reject inputs the software encoders handle, such as 10-bit video.
            result = await EncodeAsync(job, paths.Ffmpeg, partialPath, info, hardware: null, cancellationToken);
        }

        // ffmpeg exits with 0 when asked to quit, so the cancellation flag decides.
        if (result.Canceled || cancellationToken.IsCancellationRequested)
        {
            TryDelete(partialPath);
            job.State = JobState.Canceled;
            JobChanged?.Invoke(job);
            return;
        }

        if (result.ExitCode != 0 || !File.Exists(partialPath))
        {
            TryDelete(partialPath);
            Fail(job, SummarizeError(result.StandardError), job.Log);
            return;
        }

        string finalPath = job.OutputPath;
        if (File.Exists(finalPath))
        {
            // Something else created the file while the conversion ran.
            finalPath = OutputPathResolver.Resolve(
                job.InputPath, Path.GetExtension(finalPath).TrimStart('.'),
                Path.GetDirectoryName(finalPath), job.Settings.Suffix, File.Exists);
        }

        File.Move(partialPath, finalPath);
        job.OutputPath = finalPath;
        job.Progress = 1;
        job.Remaining = null;
        job.State = JobState.Completed;
        JobChanged?.Invoke(job);
    }

    private async Task<ProcessResult> EncodeAsync(
        ConversionJob job,
        string ffmpeg,
        string partialPath,
        MediaInfo? info,
        HardwareEncoders? hardware,
        CancellationToken cancellationToken)
    {
        List<string> arguments = ArgumentBuilder.Build(job.InputPath, partialPath, job.Settings, info, hardware);
        double? expected = ArgumentBuilder.ExpectedDuration(job.Settings, info);
        var parser = new ProgressParser();

        job.Progress = null;
        job.Speed = null;
        job.Remaining = null;

        ProcessResult result = await FfmpegProcess.RunAsync(
            ffmpeg,
            arguments,
            line =>
            {
                if (parser.Feed(line) is not { } snapshot)
                {
                    return;
                }

                job.Speed = snapshot.Speed;
                if (expected is > 0 && snapshot.OutTime is { } outTime)
                {
                    job.Progress = Math.Clamp(outTime.TotalSeconds / expected.Value, 0, 1);
                    job.Remaining = snapshot.Speed is > 0
                        ? TimeSpan.FromSeconds(Math.Max(0, expected.Value - outTime.TotalSeconds) / snapshot.Speed.Value)
                        : null;
                }

                JobChanged?.Invoke(job);
            },
            cancellationToken,
            lowPriority: true);

        job.Log = FormatLog(arguments, result.StandardError);
        return result;
    }

    private Task<HardwareEncoders> GetHardwareAsync(string ffmpeg)
    {
        lock (_gate)
        {
            if (_hardware is null || _hardwareFor != ffmpeg)
            {
                _hardwareFor = ffmpeg;
                _hardware = EncoderProbe.DetectAsync(ffmpeg, CancellationToken.None);
            }

            return _hardware;
        }
    }

    private void Fail(ConversionJob job, string error, string? log)
    {
        job.State = JobState.Failed;
        job.Error = error;
        job.Log = log;
        job.Remaining = null;
        JobChanged?.Invoke(job);
    }

    // What ffmpeg said comes first: that is what explains a failure. The command
    // line follows for anyone who wants to reproduce it in a terminal.
    private static string FormatLog(IEnumerable<string> arguments, string standardError)
    {
        var log = new StringBuilder();
        if (standardError.Length > 0)
        {
            log.AppendLine(standardError).AppendLine();
        }

        log.Append("ffmpeg");
        foreach (string argument in arguments)
        {
            log.Append(' ');
            log.Append(argument.Contains(' ') ? $"\"{argument}\"" : argument);
        }

        return log.ToString();
    }

    /// <summary>Picks the line of ffmpeg's output that best explains a failure.</summary>
    internal static string SummarizeError(string standardError)
    {
        string? line = standardError
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(l => !l.StartsWith("Conversion failed", StringComparison.OrdinalIgnoreCase));
        if (line is null)
        {
            return "ffmpeg failed without an error message.";
        }

        // Drop the "[libx264 @ 000001f2c8a1b2c0] " prefix; the address means nothing to the user.
        if (line.StartsWith('[') && line.IndexOf("] ", StringComparison.Ordinal) is var end and > 0)
        {
            line = line[(end + 2)..];
        }

        return line;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover partial file is harmless; the next run overwrites it.
        }
    }
}
