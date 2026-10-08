using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SentinelX.Core;
using SentinelX.Services.Care;
using SentinelX.Services.Desktop;
using SentinelX.WinUI.Services.Link;
using SentinelX.WinUI.Services.Toasts;
using SentinelX.WinUI.UI;
using SentinelX.WinUI.Utilities;

namespace SentinelX.WinUI;

public partial class App : Application
{
    private SingleInstanceService? instance;
    private ServiceProvider? provider;
    private Window? shell;

    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>True when Windows started the app at login (registry Run entry with --autostart).</summary>
    public static bool AutostartLaunch { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            AppLog.Write(e.Exception);
            e.Handled = true; // UI hiccups must not kill the assistant.
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) AppLog.Write(ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Write(e.Exception);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            provider?.Dispose();
            instance?.Dispose();
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] cmd = Environment.GetCommandLineArgs();
        AutostartLaunch = cmd.Contains("--autostart");
        bool smoke = cmd.Length >= 3 && cmd[1] == "--ui-smoke";
        if (smoke)
        {
            Environment.SetEnvironmentVariable("SENTINEL_DATA_DIR", Path.Combine(Path.GetFullPath(cmd[2]), "data"));
            Environment.SetEnvironmentVariable("SENTINEL_UI_SMOKE", "1");
        }

        if (!smoke)
        {
            instance = new SingleInstanceService(AppPaths.Root);
            if (!instance.IsPrimary)
            {
                instance.ActivateExisting();
                Exit();
                return;
            }
        }

        try
        {
            provider = ServiceLocator.Build(DispatcherQueue.GetForCurrentThread());
            Services = provider;

            if (smoke)
            {
                int code = await WinUiSmokeTest.RunAsync(provider, Path.GetFullPath(cmd[2]));
                provider.Dispose();
                Environment.Exit(code);
                return;
            }

            var desktop = provider.GetRequiredService<IDesktopService>();
            shell = provider.GetRequiredService<ShellWindow>();
            desktop.Attach(shell);
            shell.Activate();
            try
            {
                if (shell.Content is FrameworkElement root)
                    provider.GetRequiredService<XamlRootProvider>().Root = root.XamlRoot;
            }
            catch (Exception ex)
            {
                AppLog.Write(ex);
            }

            instance?.Listen(desktop.ShowWindow);
            try
            {
                provider.GetRequiredService<CareService>().Start();
            }
            catch (Exception ex)
            {
                AppLog.Write(ex);
            }

            provider.GetRequiredService<ToastService>().Start();
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            if (smoke)
            {
                try
                {
                    Directory.CreateDirectory(cmd[2]);
                    File.WriteAllText(Path.Combine(cmd[2], "FAILED.txt"), ex.ToString());
                }
                catch
                {
                }

                Environment.Exit(1);
            }
            else
            {
                MessageBoxW(IntPtr.Zero, "Nie udało się uruchomić Sentinel:\n" + ex.Message, "Sentinel X", 0x10);
                Exit();
            }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
