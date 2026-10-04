using FFConvert.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace FFConvert.App;

public partial class App : Application
{
    private static readonly TimeSpan s_closeDelay = TimeSpan.FromSeconds(1.5);

    private readonly LaunchRequest _initialRequest;
    private readonly SingleInstance _instance;
    private DispatcherQueue _dispatcher = null!;
    private MainWindow? _mainWindow;
    private ProgressWindow? _progressWindow;

    internal App(LaunchRequest initialRequest, SingleInstance instance)
    {
        _initialRequest = initialRequest;
        _instance = instance;
        Settings = SettingsStore.Load();
        Queue = new JobQueue(() => Settings);
        InitializeComponent();
    }

    public static new App Current => (App)Application.Current;

    public AppSettings Settings { get; }

    public JobQueue Queue { get; }

    internal QueueViewModel QueueView { get; private set; } = null!;

    public void SaveSettings()
    {
        try
        {
            SettingsStore.Save(Settings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Settings stay in effect for this run; there is nowhere useful to report this.
        }
    }

    /// <summary>Queues the files and shows the progress window.</summary>
    public void StartConversion(IEnumerable<string> files, ConversionSettings settings)
    {
        if (QueueView.Enqueue(files, settings) > 0)
        {
            ShowProgressWindow();
        }
        else if (_mainWindow is null && _progressWindow is null)
        {
            // Started from the context menu with nothing this preset can convert.
            Exit();
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        QueueView = new QueueViewModel(Queue, _dispatcher);
        QueueView.Finished += OnQueueFinished;

        Handle(_initialRequest);
        _instance.Listen(request => _dispatcher.TryEnqueue(() => Handle(request)));
    }

    private void Handle(LaunchRequest request)
    {
        Preset? preset = request.Command is { } id ? PresetCatalog.Find(id, Settings.UserPresets) : null;
        if (preset is not null && request.Files.Count > 0)
        {
            StartConversion(request.Files, preset.Settings);
            return;
        }

        ShowMainWindow(request.Files, compress: request.Command == ContextMenuModel.CompressId);
    }

    private void ShowMainWindow(IReadOnlyList<string> files, bool compress)
    {
        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow();
            _mainWindow.Closed += (_, _) => _mainWindow = null;
        }

        if (files.Count > 0)
        {
            _mainWindow.LoadFiles(files, compress ? PresetCatalog.CompressDefaults : null);
        }

        WindowHelper.BringToFront(_mainWindow);
    }

    private void ShowProgressWindow()
    {
        if (_progressWindow is null)
        {
            _progressWindow = new ProgressWindow(QueueView);
            _progressWindow.Closed += (_, _) =>
            {
                _progressWindow = null;
                QueueView.ClearFinished();
            };
        }

        WindowHelper.BringToFront(_progressWindow);
    }

    private async void OnQueueFinished(QueueSummary summary)
    {
        ProgressWindow? window = _progressWindow;
        bool watching = window is not null && WindowHelper.IsForeground(window);
        if (Settings.NotifyWhenDone && !watching && summary.Completed + summary.Failed > 0)
        {
            Notifier.ShowFinished(summary);
        }

        if (window is null)
        {
            return;
        }

        if (window.CloseRequested)
        {
            window.CloseNow();
            return;
        }

        // Failures stay on screen: the window is where their details can be opened.
        if (summary.Failed > 0 || !Settings.CloseWhenDone)
        {
            return;
        }

        if (summary.Completed > 0)
        {
            // Long enough to see that it finished.
            await Task.Delay(s_closeDelay);
        }

        if (!Queue.IsBusy && ReferenceEquals(window, _progressWindow))
        {
            window.CloseNow();
        }
    }
}
