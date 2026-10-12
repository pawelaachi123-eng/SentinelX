using WinRT;

namespace SentinelX.WinUI;

/// <summary>
/// Entry point for the unpackaged WinUI 3 host. This project uses self-contained
/// Windows App SDK deployment, so a framework-package bootstrap dependency must
/// not be initialized here.
/// </summary>
internal static class AppBootstrap
{
    [STAThread]
    private static void Main(string[] args)
    {
        ComWrappersSupport.InitializeComWrappers();

        global::Microsoft.UI.Xaml.Application.Start(p =>
        {
            var sync = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            global::System.Threading.SynchronizationContext.SetSynchronizationContext(sync);
            _ = new App();
        });
    }
}