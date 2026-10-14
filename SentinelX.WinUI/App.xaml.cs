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
            if (!smoke)
            {
                bool ollamaReady = false;
                try
                {
                    using var ollamaBootstrap = new SentinelX.Services.Base.OllamaSupervisor();
                    using var ollamaTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    ollamaReady = await ollamaBootstrap.EnsureAsync(ollamaTimeout.Token);
                }
                catch (Exception ex) { AppLog.Write(ex); }
                Environment.SetEnvironmentVariable("SENTINEL_OLLAMA_STARTUP_STATUS", ollamaReady ? "ready" : "not-ready");
                try
                {
                    string logDirectory = Path.Combine(AppPaths.Root, "Logs");
                    Directory.CreateDirectory(logDirectory);
                    File.AppendAllText(Path.Combine(logDirectory, "startup.log"), DateTimeOffset.UtcNow.ToString("O") + " Ollama-before-UI=" + (ollamaReady ? "ready" : "not-ready") + Environment.NewLine);
                }
                catch { }
                if (!ollamaReady)
                    AppLog.Write(new InvalidOperationException("Ollama nie odpowiedziała przed uruchomieniem interfejsu. Sprawdź instalację Ollama i lokalne modele; Sentinel kontynuuje uruchamianie."));
            }
            provider = ServiceLocator.Build(DispatcherQueue.GetForCurrentThread());
            Services = provider;
            if (!smoke)
            {
                ApplyWatchPrivacyMigration(provider.GetRequiredService<SentinelX.Services.Settings.ISettingsService>());
                SeedInitialMemory(provider.GetRequiredService<ConversationMemoryService>());
            }

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
            SentinelX.Services.Maintenance.WinUiFileDialog.MainWindow = shell;
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

    private static void ApplyWatchPrivacyMigration(SentinelX.Services.Settings.ISettingsService settings)
    {
        try
        {
            if (settings.Current.Startup.WatchPrivacyMigrationApplied) return;
            settings.Current.Watch.Enabled = false;
            settings.Current.Startup.WatchPrivacyMigrationApplied = true;
            settings.Save();
        }
        catch (Exception ex) { AppLog.Write(ex); }
    }

    private static void SeedInitialMemory(ConversationMemoryService memory)
    {
        try
        {
            // Stable project and interaction preferences, stored locally using the configured memory/privacy policy.
            var notes = new (string Text, string Category)[]
            {
            ("Projekt Sentinela użytkownika: Sentinel X to lokalny asystent Windows. Przy wydaniach użytkownik oczekuje prawdziwego builda EXE i APK, raportu testów oraz jasnego oddzielenia funkcji zweryfikowanych od niedokończonych.", "fakt"),
            ("Sentinel Base użytkownika: hub ESP32-WROOM-32 DevKit w Wi-Fi STA. Docelowe połączenie z komputerem używa SX4/TLS, przypięcia fingerprintu SHA-256, challenge i negocjowanych capabilities. Zwykłe HTTP nie jest protokołem SX4.", "fakt"),
            ("Preferencja AI użytkownika: pierwszeństwo ma AI lokalne/offline. Podczas CS2 i innych gier trzeba ograniczać użycie RAM, VRAM i CPU oraz nie ładować ciężkiego modelu, dopóki nie jest potrzebny.", "preferencja"),
            ("Preferencje komunikacji: odpowiadaj po polsku, prosto i zwięźle domyślnie; rozwijaj na prośbę. Wykonuj jasne polecenia bez ponownego pytania. Koryguj wyłącznie oczywiste literówki; pełnych pytań nie zamieniaj w podobne komendy.", "preferencja"),
            ("Prywatność Sentinela: nasłuch mikrofonu ma mieć widoczny status. Podgląd lub przechwycenie ekranu jest dozwolone tylko po wyraźnym poleceniu użytkownika; nie prowadź ukrytego, stałego podglądu ekranu.", "decyzja"),
            ("Preferencja interfejsu: nowoczesny minimalistyczny WinUI 3/Nexus, centralny animowany rdzeń, szklane panele, ustawienia zamiast zakładek i poprawne przywracanie fokusu po zamknięciu panelu.", "preferencja")
        };
            var existing = new HashSet<string>(memory.GetNotes().Select(n => ConversationMemoryService.Normalize(n.Text)), StringComparer.Ordinal);
            foreach (var note in notes)
            {
                string normalized = ConversationMemoryService.Normalize(note.Text);
                if (existing.Add(normalized)) memory.AddNote(note.Text, note.Category, "initial-project-profile");
            }
        }
        catch (Exception ex) { AppLog.Write(ex); }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
