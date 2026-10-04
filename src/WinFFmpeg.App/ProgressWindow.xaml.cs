using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace WinFFmpeg.App;

/// <summary>The compact window that shows the queue, like Explorer's copy dialog.</summary>
public sealed partial class ProgressWindow : Window
{
    // Device-independent pixels: title bar, header and footer, then one job row.
    private const int BaseHeight = 190;
    private const int RowHeight = 72;
    private const int MinHeight = 270;
    private const int MaxInitialHeight = 560;

    private bool _closing;
    private bool _dialogOpen;

    public ProgressWindow(QueueViewModel queue)
    {
        Queue = queue;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        // Tall enough for the rows it opens with, like Explorer's copy dialog; longer lists scroll.
        int height = Math.Clamp(BaseHeight + RowHeight * queue.Jobs.Count, MinHeight, MaxInitialHeight);
        WindowHelper.Initialize(this, width: 480, height: height, minWidth: 400, minHeight: MinHeight);
        AppWindow.Closing += OnClosing;
    }

    public QueueViewModel Queue { get; }

    /// <summary>The user chose to stop; the window closes as soon as ffmpeg has let go of its files.</summary>
    public bool CloseRequested { get; private set; }

    public void CloseNow()
    {
        _closing = true;
        Close();
    }

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closing || Queue.IsIdle)
        {
            return;
        }

        // Closing now would end the app with ffmpeg mid-file, so the queue is stopped first.
        args.Cancel = true;
        if (CloseRequested || _dialogOpen)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Stop converting?",
            Content = "Files that are not finished yet are discarded.",
            PrimaryButtonText = "Stop",
            CloseButtonText = "Keep converting",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await ShowAsync(dialog) != ContentDialogResult.Primary)
        {
            return;
        }

        if (Queue.IsIdle)
        {
            CloseNow();
            return;
        }

        CloseRequested = true;
        Queue.CancelAll();
    }

    private void OnCancelJobClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is JobViewModel job)
        {
            Queue.Cancel(job);
        }
    }

    private void OnRevealClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is JobViewModel job)
        {
            WindowHelper.RevealInExplorer(job.Job.OutputPath);
        }
    }

    private async void OnDetailsClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not JobViewModel job || job.Job.Log is not { } log || _dialogOpen)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = job.FileName,
            Content = new ScrollViewer
            {
                MaxHeight = 280,
                Content = new TextBlock
                {
                    Text = log,
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    FontSize = 12,
                    IsTextSelectionEnabled = true,
                    TextWrapping = TextWrapping.Wrap,
                },
            },
            PrimaryButtonText = "Copy",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await ShowAsync(dialog) == ContentDialogResult.Primary)
        {
            var data = new DataPackage();
            data.SetText(log);
            Clipboard.SetContent(data);
        }
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        if (Queue.LastOutputPath is { } path)
        {
            WindowHelper.RevealInExplorer(path);
        }
    }

    private void OnCancelAllClick(object sender, RoutedEventArgs e) => Queue.CancelAll();

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseNow();

    // Only one ContentDialog can be open per window.
    private async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        _dialogOpen = true;
        try
        {
            return await dialog.ShowAsync();
        }
        finally
        {
            _dialogOpen = false;
        }
    }
}
