using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;
using SentinelX.Services.AI;
using SentinelX.Services.Engine;
namespace SentinelX.ViewModels;
public partial class AiViewModel : ObservableObject, IDisposable
{
    private readonly IAiService ai;
    private readonly IEngineService engine;
    private readonly IUiDispatcher dispatcher;
    public ObservableCollection<string> Models { get; } = [];
    [ObservableProperty] private string selectedModel = "";
    [ObservableProperty] private string status = "Wybierz model, jeśli chcesz wymusić konkretny. Domyślnie Sentinel dobiera go sam.";
    [ObservableProperty] private string engineLabel = "Silnik AI startuje";
    [ObservableProperty] private string engineMessage = "";
    [ObservableProperty] private double engineProgressPercent;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private bool isSelectingModel;
    [ObservableProperty] private bool isRepairing;
    public bool IsBusy => IsRefreshing || IsSelectingModel || IsRepairing;
    partial void OnIsRefreshingChanged(bool value) => OnPropertyChanged(nameof(IsBusy));
    partial void OnIsSelectingModelChanged(bool value) => OnPropertyChanged(nameof(IsBusy));
    partial void OnIsRepairingChanged(bool value) => OnPropertyChanged(nameof(IsBusy));
    public AiViewModel(IAiService ai, IEngineService engine, IUiDispatcher dispatcher)
    {
        this.ai = ai; this.engine = engine; this.dispatcher = dispatcher;
        engine.Changed += OnEngineChanged;
        ApplyEngine();
    }
    private void OnEngineChanged() => dispatcher.Post(ApplyEngine);
    private void ApplyEngine()
    {
        var current = engine.Status;
        EngineLabel = current.State switch
        {
            "ready" => "Silnik AI: gotowy",
            "installing" => "Silnik AI: instaluje się sam",
            "paused" => "Silnik AI: czeka na koniec gry",
            "error" => "Silnik AI: czeka na internet (ponowi sam)",
            _ => "Silnik AI: startuje"
        };
        EngineMessage = current.Message;
        EngineProgressPercent = Math.Clamp(current.Progress * 100, 0, 100);
    }
    [RelayCommand(IncludeCancelCommand = true)] private async Task RefreshAsync(CancellationToken token)
    {
        IsRefreshing = true;
        Status = "Sprawdzam modele silnika AI…";
        try
        {
            var models = await ai.GetModelsAsync(token); Models.Clear(); foreach (string model in models) Models.Add(model);
            if (!Models.Contains(SelectedModel)) SelectedModel = Models.FirstOrDefault() ?? "";
            Status = models.Count > 0 ? $"Dostępne lokalnie: {models.Count} modeli" : "Modele jeszcze się pobierają — silnik robi to sam w tle. Polecenia systemowe działają już teraz.";
        }
        catch (OperationCanceledException) { Status = "Sprawdzanie przerwane lub przekroczono czas."; }
        catch (Exception ex) { Status = "Silnik AI jeszcze nie jest gotowy (naprawi się sam). " + ex.Message; }
        finally { IsRefreshing = false; }
    }
    [RelayCommand(IncludeCancelCommand = true)] private async Task SelectModelAsync(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(SelectedModel)) return;
        IsSelectingModel = true;
        try { Status = await ai.SelectModelAsync(SelectedModel, token); }
        catch (OperationCanceledException) { Status = "Przerwano wybór modelu."; }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsSelectingModel = false; }
    }
    [RelayCommand(IncludeCancelCommand = true)] private async Task RepairAsync(CancellationToken token)
    {
        IsRepairing = true;
        try { Status = await engine.RepairAsync(token); }
        catch (OperationCanceledException) { Status = "Przerwano naprawę silnika AI."; }
        catch (Exception ex) { Status = "Nie udało się uruchomić naprawy: " + ex.Message; }
        finally { IsRepairing = false; }
    }
    public void Dispose() => engine.Changed -= OnEngineChanged;
}
