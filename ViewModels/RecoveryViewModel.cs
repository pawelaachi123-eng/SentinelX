using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Services.Maintenance;
namespace SentinelX.ViewModels;
public partial class RecoveryViewModel(RecoveryState state, MaintenanceService maintenance) : ObservableObject
{
    public event Action<bool?>? RequestClose;
    [ObservableProperty] private string result = "Wybierz akcję. Nic nie dzieje się samo — także parowanie Base zostaje nietknięte, dopóki jawnie je zarchiwizujesz.";
    public string Summary => state.UiFailures > 0
        ? $"Poprzednie uruchomienie nie zakończyło się czysto (nieczyste starty z rzędu: {state.UiFailures})."
        : "Wykryto problem ze stanem aplikacji.";
    public string Details => string.Join(" ",
        $"Nieczyste starty UI: {state.UiFailures}, Agenta: {state.AgentFailures}.",
        $"Licznik startów aktualizacji: {state.LaunchFailures}" + (state.ActiveVersion != null ? $" (aktywna: {state.ActiveVersion})." : "."),
        "Ustawienia: " + (state.SettingsError ?? "sprawne.") + ".",
        "Parowanie Base: " + (state.IdentityState switch
        {
            "paired" => "sprawne.",
            "absent" => "brak (Base nie jest sparowana).",
            "corrupt" => "USZKODZONE — zarchiwizuj je przyciskiem poniżej i sparuj ponownie.",
            _ => "nieznane."
        }));
    public string LastErrorText => state.LastError != null ? "Ostatni błąd: " + state.LastError : "Brak wpisu w errors.log.";
    private async Task Run(Func<string> action)
    {
        Result = "Pracuję…";
        try
        {
            Result = await Task.Run(action);
        }
        catch (Exception ex)
        {
            Result = "Nie powiodło się: " + ex.Message;
        }
    }
    [RelayCommand] private void Continue() => RequestClose?.Invoke(true);
    [RelayCommand] private void Close() => RequestClose?.Invoke(false);
    [RelayCommand] private Task RollbackAsync() => Run(RecoveryService.RollbackUpdate);
    [RelayCommand] private Task RepairDirsAsync() => Run(() => maintenance.RepairDirectoriesAsync().GetAwaiter().GetResult());
    [RelayCommand] private Task RepairAutostartAsync() => Run(maintenance.RepairAutostart);
    [RelayCommand] private Task ResetSettingsAsync() => Run(RecoveryService.ResetSettings);
    [RelayCommand] private Task BackupIdentityAsync() => Run(RecoveryService.BackupCorruptIdentity);
    [RelayCommand] private Task OpenLogsAsync() => Run(RecoveryService.OpenLogs);
}
