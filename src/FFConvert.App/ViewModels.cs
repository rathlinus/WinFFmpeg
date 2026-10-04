using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using FFConvert.Core;
using Microsoft.UI.Dispatching;

namespace FFConvert.App;

/// <summary>One row of the progress window.</summary>
public sealed class JobViewModel : INotifyPropertyChanged
{
    private const string PendingGlyph = "";
    private const string RunningGlyph = "";
    private const string CompletedGlyph = "";
    private const string FailedGlyph = "";
    private const string CanceledGlyph = "";

    // An empty name tells bindings that every property may have changed.
    private static readonly PropertyChangedEventArgs s_everything = new(string.Empty);

    public JobViewModel(ConversionJob job)
    {
        Job = job;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ConversionJob Job { get; }

    public string FileName => Path.GetFileName(Job.InputPath);

    public string Glyph => Job.State switch
    {
        JobState.Pending => PendingGlyph,
        JobState.Running => RunningGlyph,
        JobState.Completed => CompletedGlyph,
        JobState.Failed => FailedGlyph,
        _ => CanceledGlyph,
    };

    public string StatusText => Job.State switch
    {
        JobState.Pending => "Waiting",
        JobState.Running => RunningText(),
        JobState.Completed => $"Saved as {Path.GetFileName(Job.OutputPath)}",
        JobState.Failed => Job.Error ?? "Failed",
        _ => "Canceled",
    };

    public double ProgressValue => (Job.Progress ?? 0) * 100;

    public bool IsRunning => Job.State == JobState.Running;

    public bool IsIndeterminate => IsRunning && Job.Progress is null;

    public bool CanCancel => !Job.IsFinished;

    public bool CanReveal => Job.State == JobState.Completed;

    public bool IsFailed => Job.State == JobState.Failed;

    public bool IsNotFailed => !IsFailed;

    public bool HasLog => IsFailed && !string.IsNullOrEmpty(Job.Log);

    public void Refresh() => PropertyChanged?.Invoke(this, s_everything);

    private string RunningText()
    {
        if (Job.Progress is not { } progress)
        {
            return "Converting…";
        }

        var parts = new List<string> { progress.ToString("P0", CultureInfo.CurrentCulture) };
        if (Job.Speed is > 0)
        {
            parts.Add($"{Job.Speed.Value.ToString("0.0", CultureInfo.CurrentCulture)}× speed");
        }

        if (Job.Remaining is { } remaining)
        {
            parts.Add($"{TimeText.Format(remaining)} left");
        }

        return string.Join("  ·  ", parts);
    }
}

/// <summary>Mirrors the <see cref="JobQueue"/> onto the UI thread for the progress window.</summary>
public sealed class QueueViewModel : INotifyPropertyChanged
{
    private static readonly PropertyChangedEventArgs s_everything = new(string.Empty);

    private readonly JobQueue _queue;
    private readonly DispatcherQueue _dispatcher;
    private readonly Dictionary<ConversionJob, JobViewModel> _byJob = [];

    public QueueViewModel(JobQueue queue, DispatcherQueue dispatcher)
    {
        _queue = queue;
        _dispatcher = dispatcher;
        _queue.JobChanged += job => _dispatcher.TryEnqueue(() => OnJobChanged(job));
        _queue.Drained += summary => _dispatcher.TryEnqueue(() =>
        {
            Refresh();
            Finished?.Invoke(summary);
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised on the UI thread when the queue runs out of work.</summary>
    public event Action<QueueSummary>? Finished;

    public ObservableCollection<JobViewModel> Jobs { get; } = [];

    public bool IsBusy => Jobs.Any(j => !j.Job.IsFinished);

    public bool IsIdle => !IsBusy;

    public string Header
    {
        get
        {
            int total = Jobs.Count;
            int finished = Jobs.Count(j => j.Job.IsFinished);
            if (finished < total)
            {
                return total == 1 ? "Converting 1 file" : $"Converting {finished + 1} of {total} files";
            }

            int completed = Jobs.Count(j => j.Job.State == JobState.Completed);
            int failed = Jobs.Count(j => j.IsFailed);
            return (completed, failed) switch
            {
                (0, 0) => "Canceled",
                (_, 0) => completed == 1 ? "1 file converted" : $"{completed} files converted",
                (0, _) => failed == 1 ? "Conversion failed" : $"{failed} conversions failed",
                _ => $"{completed} converted, {failed} failed",
            };
        }
    }

    /// <summary>0 to 100 across all jobs, counting the running one by its own progress.</summary>
    public double OverallProgress
    {
        get
        {
            if (Jobs.Count == 0)
            {
                return 0;
            }

            double done = Jobs.Sum(j => j.Job.IsFinished ? 1 : j.Job.Progress ?? 0);
            return done / Jobs.Count * 100;
        }
    }

    public bool HasSeveralJobs => Jobs.Count > 1;

    /// <summary>Folder of the most recent output, for the "Open folder" button.</summary>
    public string? LastOutputPath =>
        Jobs.LastOrDefault(j => j.Job.State == JobState.Completed)?.Job.OutputPath;

    public bool HasOutput => LastOutputPath is not null;

    /// <summary>Queues the files and returns how many jobs that made.</summary>
    public int Enqueue(IEnumerable<string> files, ConversionSettings settings)
    {
        // A new batch after everything finished starts from a clean list.
        if (IsIdle)
        {
            ClearFinished();
        }

        IReadOnlyList<ConversionJob> jobs = _queue.Enqueue(files, settings);
        foreach (ConversionJob job in jobs)
        {
            var viewModel = new JobViewModel(job);
            _byJob[job] = viewModel;
            Jobs.Add(viewModel);
        }

        Refresh();
        return jobs.Count;
    }

    public void Cancel(JobViewModel job) => _queue.Cancel(job.Job);

    public void CancelAll() => _queue.CancelAll();

    public void ClearFinished()
    {
        for (int i = Jobs.Count - 1; i >= 0; i--)
        {
            if (Jobs[i].Job.IsFinished)
            {
                _byJob.Remove(Jobs[i].Job);
                Jobs.RemoveAt(i);
            }
        }

        Refresh();
    }

    private void OnJobChanged(ConversionJob job)
    {
        if (_byJob.TryGetValue(job, out JobViewModel? viewModel))
        {
            viewModel.Refresh();
            Refresh();
        }
    }

    private void Refresh() => PropertyChanged?.Invoke(this, s_everything);
}
