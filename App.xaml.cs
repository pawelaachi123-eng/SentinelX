using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SentinelX.Core;
using SentinelX.Services.Agent;
using SentinelX.Services.Care;
using SentinelX.Services.Desktop;
using SentinelX.Services.Maintenance;
namespace SentinelX;
public partial class App : Application
{
    private SingleInstanceService? instance;
    private ServiceProvider? provider;
    private AgentHost? agentHost;
    private bool cleanRun;
    public static IServiceProvider Services { get; private set; } = null!;
    /// <summary>True when Windows started the app at login (registry Run entry with --autostart).</summary>
    public static bool AutostartLaunch { get; private set; }
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Utilities.CrashLogger.Initialize(this);
        AutostartLaunch = e.Args.Contains("--autostart");
        bool selfTest = e.Args.Length == 2 && e.Args[0] is "--self-test" or "--asr-test" or "--ai-test" or "--builder-test";
        bool uiTest = e.Args.Length == 2 && e.Args[0] == "--ui-smoke";
        bool agentSmoke = e.Args.Length == 2 && e.Args[0] == "--agent-smoke";
        bool agent = e.Args.Contains("--agent");
        if (selfTest || uiTest) Environment.SetEnvironmentVariable("SENTINEL_DATA_DIR", Path.Combine(Path.GetFullPath(e.Args[1]), "data"));
        if (agentSmoke) Environment.SetEnvironmentVariable("SENTINEL_DATA_DIR", Path.Combine(Path.GetFullPath(e.Args[1]), "data"));
        if (agentSmoke)
        {
            try { int code = await AgentSmokeRunner.RunAsync(Path.GetFullPath(e.Args[1])); Shutdown(code); }
            catch (Exception ex) { try { Directory.CreateDirectory(e.Args[1]); File.WriteAllText(Path.Combine(e.Args[1], "FAILED.txt"), ex.ToString()); } catch { } Shutdown(1); }
            return;
        }
        if (agent)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { agentHost = await AgentHost.StartAsync(); }
            catch (Exception ex) { AppLog.Write(ex); Shutdown(2); }
            return;
        }
        if (!selfTest && !uiTest && !agentSmoke && !agent)
        {
            try { RunHealth.BeginRun(AppPaths.Root, "ui"); } catch (Exception ex) { AppLog.Write(ex); }
            try
            {
                var recovery = RecoveryService.Evaluate();
                if (RecoveryService.Needed(recovery))
                {
                    var recoveryWindow = new Views.RecoveryWindow(recovery);
                    if (recoveryWindow.ShowDialog() != true) { Shutdown(0); return; }
                }
            }
            catch (Exception ex) { AppLog.Write(ex); }
        }
        if (uiTest) Environment.SetEnvironmentVariable("SENTINEL_UI_SMOKE", "1");
        if (!selfTest && !uiTest)
        {
            instance = new SingleInstanceService(AppPaths.Root);
            if (!instance.IsPrimary) { instance.ActivateExisting(); Shutdown(); return; }
            try{
                var updater=new VerifiedUpdater(Path.Combine(AppPaths.Root,"Updates"),MaintenanceService.ValidPackage);
                var journal=updater.ReadJournal();
                if(journal.Active!=null){
                    string current=Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
                    var active=current.Equals(journal.Active.Directory.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)?updater.BeginLaunch():journal.Active;
                    if(active!=null&&VerifiedUpdater.VerifyVersion(active)&&!current.Equals(active.Directory.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)){
                        instance.Dispose();instance=null;
                        var launch=new System.Diagnostics.ProcessStartInfo(Path.Combine(active.Directory,"SentinelX.exe")){UseShellExecute=false,WorkingDirectory=active.Directory};
                        if(AutostartLaunch)launch.ArgumentList.Add("--autostart");
                        System.Diagnostics.Process.Start(launch);Shutdown();return;
                    }
                }
            }catch(Exception updateError){AppLog.Write(updateError);}

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
            var shell = provider.GetRequiredService<Views.MainWindow>();
            MainWindow = shell; shell.Show(); cleanRun = true;
            instance?.Listen(provider.GetRequiredService<IDesktopService>().ShowWindow);
            // The caretaker starts the phone link and the AI engine and keeps them running (it does nothing in the UI smoke test).
            if (!uiTest)
            {
                try { provider.GetRequiredService<CareService>().Start(); provider.GetRequiredService<Services.Base.IBaseControl>().Start(); }
                catch (Exception careError) { AppLog.Write(careError); }
                try
                {
                    // Controlled autostart heals itself: when the user enabled the background
                    // Agent (e.g. after an update stopped it), the UI starts it again.
                    if (provider.GetRequiredService<Services.Settings.ISettingsService>().Current.Startup.AgentAutostart
                        && !AgentClient.IsAgentRunning(AppPaths.Root) && Environment.ProcessPath is string exe)
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "--agent") { UseShellExecute = false });
                }
                catch (Exception agentError) { AppLog.Write(agentError); }
            }
            if(!uiTest){_ = Task.Run(async()=>{await Task.Delay(60000);try{string report=await provider.GetRequiredService<MaintenanceService>().ConfirmHealthyAsync(CancellationToken.None);if(report.StartsWith("Wersja niezdrowa",StringComparison.Ordinal))AppLog.Write(new InvalidOperationException("UpdateHealth: "+report));}catch(Exception error){AppLog.Write(error);}});}
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
    protected override void OnExit(ExitEventArgs e)
    {
        if (agentHost != null) try { agentHost.StopAsync().GetAwaiter().GetResult(); } catch { }
        if (cleanRun && !Utilities.CrashLogger.Crashed) try { RunHealth.EndRun(AppPaths.Root, "ui"); } catch { }
        provider?.Dispose(); instance?.Dispose(); base.OnExit(e);
    }
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if (agentHost != null) try { agentHost.StopAsync().GetAwaiter().GetResult(); } catch (Exception ex) { AppLog.Write(ex); }
        else if (provider != null) provider.GetRequiredService<IDesktopService>().Exit();
        else (MainWindow as MainWindow)?.CloseTestWindow();
        base.OnSessionEnding(e);
    }
}
