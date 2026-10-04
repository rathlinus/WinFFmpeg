using FFConvert.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace FFConvert.App;

public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Relative paths are resolved against the folder the app was started in. After
        // that the app moves out of it, so it does not keep that folder locked.
        LaunchRequest request = LaunchRequest.Parse(args);
        Environment.CurrentDirectory = AppContext.BaseDirectory;

        // Every right-click conversion starts this exe. All but the first hand their
        // request to the running instance, so there is one queue and one progress window.
        using SingleInstance instance = SingleInstance.Acquire();
        if (!instance.IsPrimary && SingleInstance.TrySend(request))
        {
            return;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(parameters =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(request, instance);
        });
    }
}
