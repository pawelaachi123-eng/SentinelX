using System.Runtime.InteropServices;
using Microsoft.Windows.ApplicationModel.DynamicDependency;
using WinRT;

namespace SentinelX.WinUI;

/// <summary>
/// Entry point of the unpackaged WinUI 3 build. Takes the Windows App SDK runtime
/// dependency explicitly (so the portable EXE and the Inno Setup installer work
/// without a separately installed runtime) and then starts the XAML application.
/// </summary>
internal static class AppBootstrap
{
    // Windows App SDK 1.5 → major 1, minor 5.
    private const uint MajorMinorVersion = 0x00010005;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            Bootstrap.Initialize(MajorMinorVersion);
        }
        catch (Exception ex)
        {
            MessageBoxW(IntPtr.Zero,
                "Nie można uruchomić środowiska Windows App SDK 1.5, którego potrzebuje Sentinel X.\n\n" +
                ex.Message,
                "Sentinel X", 0x10 /* MB_ICONERROR */);
            Environment.Exit(1);
            return;
        }

        ComWrappersSupport.InitializeComWrappers();

        global::Microsoft.UI.Xaml.Application.Start(p =>
        {
            var sync = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            global::System.Threading.SynchronizationContext.SetSynchronizationContext(sync);
            _ = new App();
        });

        Bootstrap.Shutdown();
    }
}
