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
    internal static void RecordUiSmokeProgress(string output, string stage)
    {
        if (output.Length == 0) return;
        Directory.CreateDirectory(output);
        File.AppendAllText(Path.Combine(output, "ui-smoke-progress.log"),
            $"{DateTimeOffset.UtcNow:O} {stage}{Environment.NewLine}");
    }
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Utilities.CrashLogger.Initialize(this);
        bool selfTest = e.Args.Length == 2 && e.Args[0] is "--self-test" or "--asr-test" or "--ai-test" or "--builder-test";
        bool uiTestArgument = e.Args.Length == 2 && e.Args[0] == "--ui-smoke";
        string uiOutputArgument = uiTestArgument ? e.Args[1] : Environment.GetEnvironmentVariable("SENTINEL_UI_SMOKE_OUTPUT") ?? "";
        bool uiTest = uiTestArgument || (Environment.GetEnvironmentVariable("SENTINEL_UI_SMOKE") == "1" && !string.IsNullOrWhiteSpace(uiOutputArgument));
        string uiOutput = uiTest ? Path.GetFullPath(uiOutputArgument) : "";
        string testOutput = uiTest ? uiOutput : selfTest ? Path.GetFullPath(e.Args[1]) : "";
        if (selfTest || uiTest) Environment.SetEnvironmentVariable("SENTINEL_DATA_DIR", Path.Combine(testOutput, "data"));
        RecordUiSmokeProgress(uiOutput, "entered App.OnStartup");
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
            RecordUiSmokeProgress(uiOutput, "building service provider");
            provider = ServiceLocator.Build(Dispatcher, uiTest ? stage => RecordUiSmokeProgress(uiOutput, stage) : null); Services = provider;
            RecordUiSmokeProgress(uiOutput, "service provider ready");
            if (uiTest)
            {
                RecordUiSmokeProgress(uiOutput, "watchers skipped in UI smoke");
            }
            else
            {
                RecordUiSmokeProgress(uiOutput, "starting watchers");
                provider.GetRequiredService<WatcherService>().Start();
                RecordUiSmokeProgress(uiOutput, "watchers started");
            }
            RecordUiSmokeProgress(uiOutput, "resolving main window");
            if (uiTest)
            {
                Type[] startupDependencies =
                [
                    typeof(Services.Actions.IActionEngine),
                    typeof(Services.Desktop.IDesktopService),
                    typeof(Core.IUiDispatcher),
                    typeof(ViewModels.CommandCenterViewModel),
                    typeof(ViewModels.SystemViewModel),
                    typeof(ViewModels.GamingViewModel),
                    typeof(ViewModels.VoiceViewModel),
                    typeof(ViewModels.AiViewModel),
                    typeof(ViewModels.ActionsViewModel),
                    typeof(ViewModels.HistoryViewModel),
                    typeof(ViewModels.SettingsViewModel),
                    typeof(ViewModels.CommandPaletteViewModel),
                    typeof(ViewModels.ReadinessViewModel),
                    typeof(ViewModels.MemoryViewModel),
                    typeof(ViewModels.ProjectViewModel),
                    typeof(ViewModels.TaskViewModel),
                    typeof(ViewModels.DiagnosticViewModel)
                ];
                foreach (Type dependency in startupDependencies)
                {
                    RecordUiSmokeProgress(uiOutput, "resolving " + dependency.Name);
                    provider.GetRequiredService(dependency);
                    RecordUiSmokeProgress(uiOutput, "resolved " + dependency.Name);
                }
            }
            var shell = provider.GetRequiredService<Views.MainWindow>();
            RecordUiSmokeProgress(uiOutput, "main window constructed");
            MainWindow = shell;
            shell.Show();
            RecordUiSmokeProgress(uiOutput, "main window shown");
            instance?.Listen(provider.GetRequiredService<IDesktopService>().ShowWindow);
            if (uiTest)
            {
                RecordUiSmokeProgress(uiOutput, "starting UI smoke runner");
                await UiSmokeTestRunner.RunAsync(provider, shell, uiOutput);
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
