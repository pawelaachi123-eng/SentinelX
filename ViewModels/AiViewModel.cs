using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Services.AI;
namespace SentinelX.ViewModels;
public partial class AiViewModel(IAiService ai) : ObservableObject
{
    public ObservableCollection<string> Models { get; } = [];
    [ObservableProperty] private string selectedModel = "";
    [ObservableProperty] private string status = "Ollama nie była jeszcze sprawdzana. Kliknij Sprawdź połączenie.";
    [RelayCommand(IncludeCancelCommand = true)] private async Task RefreshAsync(CancellationToken token)
    {
        Status = "Łączenie z lokalną Ollama…";
        try
        {
            var models = await ai.GetModelsAsync(token); Models.Clear(); foreach (string model in models) Models.Add(model);
            if (!Models.Contains(SelectedModel)) SelectedModel = Models.FirstOrDefault() ?? "";
            Status = models.Count > 0 ? $"Połączono · {models.Count} modeli lokalnych" : "Ollama działa, ale nie ma modeli. Zainstaluj model poleceniem ollama pull qwen3:4b.";
        }
        catch (OperationCanceledException) { Status = "Sprawdzanie przerwane lub przekroczono czas połączenia."; }
        catch (Exception ex) { Status = "Ollama niedostępna. Uruchom ollama serve.\n" + ex.Message; }
    }
    [RelayCommand(IncludeCancelCommand = true)] private async Task SelectModelAsync(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(SelectedModel)) return;
        try { Status = await ai.SelectModelAsync(SelectedModel, token); }
        catch (OperationCanceledException) { Status = "Przerwano."; }
        catch (Exception ex) { Status = ex.Message; }
    }
}
