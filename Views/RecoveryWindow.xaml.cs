using System.Diagnostics;
using System.IO;
using System.Windows;
using SentinelX.Core;
using SentinelX.Services.Agent;
using SentinelX.Services.Base;
using SentinelX.Services.Link;
using SentinelX.Services.Maintenance;

namespace SentinelX.Views;

public partial class RecoveryWindow : Window
{
    private readonly RecoveryState state;
    private readonly MaintenanceService maintenance;

    public RecoveryWindow(RecoveryState state)
    {
        this.state = state;
        var ollama = new OllamaSupervisor();
        maintenance = new MaintenanceService(
            new WindowsBaseService(new HeadlessDesktopService(), new SystemMonitor(), ollama, new AlertFeed()), ollama);
        InitializeComponent();
        SummaryText.Text = state.UiFailures > 0
            ? $"Poprzednie uruchomienie nie zakończyło się czysto (nieczyste starty z rzędu: {state.UiFailures})."
            : "Wykryto problem ze stanem aplikacji.";
        var parts = new List<string>
        {
            $"Nieczyste starty UI: {state.UiFailures}, Agenta: {state.AgentFailures}.",
            $"Licznik startów aktualizacji: {state.LaunchFailures}" + (state.ActiveVersion != null ? $" (aktywna: {state.ActiveVersion})." : "."),
            "Ustawienia: " + (state.SettingsError ?? "sprawne.") + ".",
            "Parowanie Base: " + (state.IdentityState switch
            {
                "paired" => "sprawne.",
                "absent" => "brak (Base nie jest sparowana).",
                "corrupt" => "USZKODZONE — zarchiwizuj je przyciskiem poniżej i sparuj ponownie.",
                _ => "nieznane."
            })
        };
        StateText.Text = string.Join(" ", parts);
        ErrorText.Text = state.LastError != null ? "Ostatni błąd: " + state.LastError : "Brak wpisu w errors.log.";
    }

    private void OnContinue(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void Run(Func<string> action)
    {
        ResultText.Text = "Pracuję…";
        try
        {
            ResultText.Text = await Task.Run(action);
        }
        catch (Exception ex)
        {
            ResultText.Text = "Nie powiodło się: " + ex.Message;
        }
    }

    private void OnRollback(object sender, RoutedEventArgs e) => Run(RecoveryService.RollbackUpdate);
    private void OnRepairDirs(object sender, RoutedEventArgs e) => Run(() => maintenance.RepairDirectoriesAsync().GetAwaiter().GetResult());
    private void OnRepairAutostart(object sender, RoutedEventArgs e) => Run(maintenance.RepairAutostart);
    private void OnResetSettings(object sender, RoutedEventArgs e) => Run(RecoveryService.ResetSettings);
    private void OnBackupIdentity(object sender, RoutedEventArgs e) => Run(RecoveryService.BackupCorruptIdentity);

    private void OnOpenLogs(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogsDirectory);
            Process.Start(new ProcessStartInfo(AppPaths.LogsDirectory) { UseShellExecute = true });
            ResultText.Text = "Otwarto katalog logów.";
        }
        catch (Exception ex)
        {
            ResultText.Text = "Nie powiodło się: " + ex.Message;
        }
    }
}
