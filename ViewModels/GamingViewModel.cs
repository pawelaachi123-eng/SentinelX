using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Services.Actions;
using SentinelX.Services.Desktop;
namespace SentinelX.ViewModels;
public partial class GamingViewModel(SystemViewModel system, IActionEngine engine, IDesktopService desktop) : ObservableObject
{
    public SystemViewModel System { get; } = system;
    [ObservableProperty] private string report = "Diagnostyka jest tylko do odczytu. Sentinel nie zmienia priorytetów procesów ani ustawień gry.";
    [RelayCommand] private void ToggleOverlay() => desktop.ToggleOverlay();
    [RelayCommand] private async Task DiagnoseAsync() => Report = (await engine.ExecuteAsync("diagnostyka komputera")).Text;
    [RelayCommand] private void Cancel() => engine.Cancel();
}
