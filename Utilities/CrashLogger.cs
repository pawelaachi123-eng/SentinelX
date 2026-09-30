using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
namespace SentinelX.Utilities;
public static class CrashLogger
{
    /// <summary>
    /// Uruchamia się przed sparsowaniem <c>App.xaml</c> (inicjalizator modułu działa przed Main),
    /// więc w trybie <c>--ui-smoke</c> nawet awaria ładowania słowników zasobów zostawia
    /// <c>FAILED.txt</c> w katalogu wyników — CI już ten plik czyta i drukuje.
    /// <c>OnStartup</c> jest na to za późno: zasoby aplikacji ładują się przed nim, a bez tego
    /// krok smoke kończy się pustym logiem („Process completed with exit code 1”).
    /// Poza smoke nie zmienia niczego: handler rejestruje się tylko dla <c>--ui-smoke</c>.
    /// </summary>
    [ModuleInitializer]
    internal static void CaptureStartupCrashForUiSmoke()
    {
        string[] args = Environment.GetCommandLineArgs();
        string? requestedOutput = args.Length >= 3 && args[1] == "--ui-smoke"
            ? args[2]
            : Environment.GetEnvironmentVariable("SENTINEL_UI_SMOKE") == "1"
                ? Environment.GetEnvironmentVariable("SENTINEL_UI_SMOKE_OUTPUT")
                : null;
        if (string.IsNullOrWhiteSpace(requestedOutput)) return;
        string output = Path.GetFullPath(requestedOutput);
        try
        {
            Directory.CreateDirectory(output);
            File.AppendAllText(Path.Combine(output, "ui-smoke-progress.log"),
                $"{DateTimeOffset.UtcNow:O} managed module initializer{Environment.NewLine}");
        }
        catch { }
        AppDomain.CurrentDomain.UnhandledException += (_, error) =>
        {
            try
            {
                Directory.CreateDirectory(output);
                string detail = error.ExceptionObject?.ToString() ?? "unknown fault";
                File.WriteAllText(Path.Combine(output, "FAILED.txt"),
                    $"Sentinel failed before the smoke runner started.{Environment.NewLine}{detail}{Environment.NewLine}");
            }
            catch { }
        };
    }
    public static void Initialize(Application app)
    {
        app.DispatcherUnhandledException += (_, error) =>
        {
            AppLog.Write(error.Exception);
            error.Handled = true;
            // Unknown dispatcher faults are not safe to continue after.
            if (Environment.GetEnvironmentVariable("SENTINEL_UI_SMOKE") != "1")
                MessageBox.Show("Sentinel napotkał błąd. Szczegóły zapisano w Logs/errors.log.\n\n" + error.Exception.Message, "Sentinel X", MessageBoxButton.OK, MessageBoxImage.Error);
            app.Shutdown(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, error) => { if (error.ExceptionObject is Exception ex) AppLog.Write(ex); };
        TaskScheduler.UnobservedTaskException += (_, error) => { AppLog.Write(error.Exception); error.SetObserved(); };
    }
}
