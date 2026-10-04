using System.Runtime.InteropServices;
using System.Security;
using WinFFmpeg.Core;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace WinFFmpeg.App;

internal static class Notifier
{
    /// <summary>Shows a toast for a finished batch. Clicking it opens the output folder.</summary>
    public static void ShowFinished(QueueSummary summary)
    {
        string title = summary.Failed == 0
            ? Plural(summary.Completed, "file converted", "files converted")
            : summary.Completed == 0
                ? Plural(summary.Failed, "conversion failed", "conversions failed")
                : $"{Plural(summary.Completed, "file converted", "files converted")}, {summary.Failed} failed";

        ConversionJob? last = summary.Jobs.LastOrDefault(j => j.State == JobState.Completed);
        string body = last is null
            ? "Open WinFFmpeg for details."
            : Path.GetFileName(last.OutputPath);
        string? folder = last is null ? null : Path.GetDirectoryName(last.OutputPath);

        // Protocol activation opens the folder directly, so the toast still works after the app has exited.
        string launch = folder is null
            ? ""
            : $" activationType=\"protocol\" launch=\"{SecurityElement.Escape(new Uri(folder).AbsoluteUri)}\"";
        string xml =
            $"<toast{launch}><visual><binding template=\"ToastGeneric\">"
            + $"<text>{SecurityElement.Escape(title)}</text><text>{SecurityElement.Escape(body)}</text>"
            + "</binding></visual></toast>";

        try
        {
            var document = new XmlDocument();
            document.LoadXml(xml);
            ToastNotificationManager.CreateToastNotifier().Show(new ToastNotification(document));
        }
        catch (COMException)
        {
            // Notifications are off, or the app runs without package identity.
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string Plural(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";
}
