using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Models;
using SentinelX.Services.Readiness;
namespace SentinelX.ViewModels;
public partial class ReadinessViewModel(IReadinessService service) : ObservableObject
{
    public ObservableCollection<ReadinessCheck> Checks { get; } = [];
    public event Action<string>? OpenSectionRequested;
    [ObservableProperty] private bool isOpen;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string summary = "Sprawdź gotowość";
    [ObservableProperty] private string lastChecked = "Jeszcze nie sprawdzano";
    [RelayCommand(IncludeCancelCommand = true)] private async Task RefreshAsync(CancellationToken token)
    {
        IsBusy = true;
        Summary = "Sprawdzanie lokalnej konfiguracji…";
        try
        {
            var results = await service.CheckAsync(token);
            token.ThrowIfCancellationRequested();
            Checks.Clear(); foreach (var check in results) Checks.Add(check);
            int ready = results.Count(x => x.State == ReadinessState.Ready);
            Summary = $"{ready}/{results.Count} sprawdzeń gotowych · narzędzia lokalne nie wymagają AI ani głosu";
            LastChecked = $"Ostatnie sprawdzenie: {DateTime.Now:HH:mm:ss}";
        }
        catch (OperationCanceledException) { Summary = "Przerwano sprawdzanie. Poprzednie wyniki mogą być nieaktualne."; }
        catch (Exception ex) { Summary = "Nie udało się sprawdzić konfiguracji: " + ex.Message; }
        finally { IsBusy = false; }
    }
    [RelayCommand] private void OpenSection(string pageKey) { IsOpen = false; OpenSectionRequested?.Invoke(pageKey); }
    [RelayCommand] private void Close() => IsOpen = false;
}
