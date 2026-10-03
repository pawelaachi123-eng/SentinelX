using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Services.Actions;
using SentinelX.Services.Desktop;
namespace SentinelX.ViewModels;
public partial class GamingViewModel(SystemViewModel system, IActionEngine engine, IDesktopService desktop) : ObservableObject
{
    public SystemViewModel System { get; } = system;
    [ObservableProperty] private string report = "Diagnostyka jest tylko do odczytu. Sentinel nie zmienia priorytetów procesów ani ustawień gry.";
    [ObservableProperty] private bool isDiagnosing;
    [RelayCommand] private void ToggleOverlay() => desktop.ToggleOverlay();
    [RelayCommand] private async Task DiagnoseAsync()
    {
        if (IsDiagnosing) return;
        if (engine.IsStopped) { Report = "STOP awaryjny jest aktywny. Wznów Sentinel przed uruchomieniem diagnostyki."; return; }
        IsDiagnosing = true;
        try { Report = (await engine.ExecuteAsync("diagnostyka komputera")).Text; }
        catch (Exception ex) { Report = "Nie udało się wykonać diagnostyki. " + ex.Message; AppLog.Write("Gaming", "Error", "Diagnostic action failed.", ex); }
        finally { IsDiagnosing = false; }
    }
    [RelayCommand] private void Cancel() => engine.Cancel();
}
