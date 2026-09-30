using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SentinelX.Core;
using SentinelX.Services.Desktop;
namespace SentinelX;
public partial class App : Application
{
    private SingleInstanceService? instance;
    private ServiceProvider? provider;
    public static IServiceProvider Services { get; private set; } = null!;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Utilities.CrashLogger.Initialize(this);
        bool selfTest = e.Args.Length == 2 && e.Args[0] is "--self-test" or "--asr-test" or "--ai-test" or "--builder-test";
        bool uiTest = e.Args.Length == 2 && e.Args[0] == "--ui-smoke";
        if (selfTest || uiTest) Environment.SetEnvironmentVariable("SENTINEL_DATA_DIR", Path.Combine(Path.GetFullPath(e.Args[1]), "data"));
        if (uiTest) Environment.SetEnvironmentVariable("SENTINEL_UI_SMOKE", "1");
        if (!selfTest && !uiTest)
        {
            instance = new SingleInstanceService(AppPaths.Root);
            if (!instance.IsPrimary) { instance.ActivateExisting(); Shutdown(); return; }
        }
        try
        {
            // Full compatibility UI and its original regression runners remain available.
            if (selfTest || e.Args.Contains("--legacy"))
            {
                var window = new MainWindow(selfTest); MainWindow = window; window.Show();
                instance?.Listen(window.ActivateExistingWindow);
                if (selfTest)
                {
                    string output = Path.GetFullPath(e.Args[1]);
                    if (e.Args[0] == "--builder-test") await ProgramBuilderTestRunner.RunAsync(output);
                    else if (e.Args[0] == "--asr-test") await AsrTestRunner.RunAsync(output);
                    else if (e.Args[0] == "--ai-test") await AiTestRunner.RunAsync(output);
                    else await SelfTestRunner.RunAsync(window, output);
                    window.CloseTestWindow(); Shutdown(0);
                }
                return;
            }
            provider = ServiceLocator.Build(Dispatcher); Services = provider;
            provider.GetRequiredService<WatcherService>().Start();
            var shell = provider.GetRequiredService<Views.MainWindow>();
            MainWindow = shell; shell.Show();
            instance?.Listen(provider.GetRequiredService<IDesktopService>().ShowWindow);
            if (uiTest)
            {
                await UiSmokeTestRunner.RunAsync(provider, shell, Path.GetFullPath(e.Args[1]));
                provider.GetRequiredService<IDesktopService>().Exit();
            }
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            if (selfTest || uiTest) { Directory.CreateDirectory(e.Args[1]); File.WriteAllText(Path.Combine(e.Args[1], "FAILED.txt"), ex.ToString()); Shutdown(1); }
            else { MessageBox.Show("Nie udało się uruchomić Sentinel:\n" + ex.Message, "Sentinel X"); Shutdown(1); }
        }
    }
    protected override void OnExit(ExitEventArgs e) { provider?.Dispose(); instance?.Dispose(); base.OnExit(e); }
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if (provider != null) provider.GetRequiredService<IDesktopService>().Exit();
        else (MainWindow as MainWindow)?.CloseTestWindow();
        base.OnSessionEnding(e);
    }
}
