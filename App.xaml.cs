using System.IO;
using System.Windows;
namespace SentinelX;
public partial class App : Application
{
    private SingleInstanceService? instance;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, error) => { AppLog.Write(error.Exception); error.Handled = true; MessageBox.Show("Sentinel napotkał błąd i zostanie zamknięty. Szczegóły są w lokalnym pliku Logs/errors.log.\n\n" + error.Exception.Message, "Sentinel X", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); };
        AppDomain.CurrentDomain.UnhandledException += (_, error) => { if (error.ExceptionObject is Exception ex) AppLog.Write(ex); };
        TaskScheduler.UnobservedTaskException += (_, error) => { AppLog.Write(error.Exception); error.SetObserved(); };
        bool selfTest = e.Args.Length == 2 && e.Args[0] is "--self-test" or "--asr-test" or "--ai-test" or "--builder-test";
        if (selfTest) Environment.SetEnvironmentVariable("SENTINEL_DATA_DIR", Path.Combine(Path.GetFullPath(e.Args[1]), "data"));
        if (!selfTest)
        {
            instance = new SingleInstanceService(AppPaths.Root);
            if (!instance.IsPrimary) { instance.ActivateExisting(); Shutdown(); return; }
        }
        try
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
                window.CloseTestWindow();
                Shutdown(0);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            if (selfTest) { Directory.CreateDirectory(e.Args[1]); File.WriteAllText(Path.Combine(e.Args[1], "FAILED.txt"), ex.ToString()); Shutdown(1); }
            else { MessageBox.Show("Nie udało się uruchomić Sentinel:\n" + ex.Message, "Sentinel X"); Shutdown(1); }
        }
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e) { (MainWindow as MainWindow)?.CloseTestWindow(); base.OnSessionEnding(e); }
}
