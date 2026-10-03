using System.Windows;
namespace SentinelX.Utilities;
public static class CrashLogger
{
    public static void Initialize(Application app)
    {
        app.DispatcherUnhandledException += (_, error) =>
        {
            AppLog.Write(error.Exception);
            error.Handled = true;
            // Unknown dispatcher faults are not safe to continue after.
            if (Environment.GetEnvironmentVariable("SENTINEL_UI_SMOKE") != "1")
                MessageBox.Show("Sentinel napotkał nieobsłużony błąd. Szczegóły zapisano w Logs/sentinel.jsonl.", "Sentinel X", MessageBoxButton.OK, MessageBoxImage.Error);
            app.Shutdown(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, error) => { if (error.ExceptionObject is Exception ex) AppLog.Write(ex); };
        TaskScheduler.UnobservedTaskException += (_, error) => { AppLog.Write(error.Exception); error.SetObserved(); };
    }
}
