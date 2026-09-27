using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Services.AI;
using SentinelX.Services.Actions;
namespace SentinelX.ViewModels;
public partial class AiViewModel(IAiService ai, IActionEngine engine) : ObservableObject
{
    [ObservableProperty] private string managementModel = "";
    [ObservableProperty] private string managementResult = "Wpisz nazwę modelu. Pobieranie i usuwanie wymagają planu oraz dokładnego potwierdzenia.";
    [ObservableProperty] private string confirmation = "";
    [ObservableProperty] private string expectedConfirmation = "";
    partial void OnManagementModelChanged(string value)
    {
        ExpectedConfirmation = "";
        Confirmation = "";
    }
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ManageAsync(string? operation, CancellationToken token)
    {
        string command;
        if (operation == "confirm")
        {
            if (ExpectedConfirmation.Length == 0 || Confirmation != ExpectedConfirmation)
            { ManagementResult = "Najpierw pokaż plan, potem wpisz dokładnie wskazane polecenie potwierdzające."; return; }
            command = ExpectedConfirmation;
            ExpectedConfirmation = Confirmation = "";
        }
        else if (operation == "running") command = "model uruchomione";
        else if (operation == "progress") command = "model status pobierania";
        else
        {
            string? name = LocalAiService.NormalizePullModelName(ManagementModel);
            if (name == null) { ManagementResult = "Podaj poprawną nazwę modelu, np. qwen3:4b."; return; }
            string verb = operation switch { "info" => "info", "pull" => "pobierz", "delete" => "usun", _ => "" };
            if (verb.Length == 0) { ManagementResult = "Nieznana operacja."; return; }
            command = "model " + verb + ": " + name;
            ExpectedConfirmation = "";
            Confirmation = "";
        }
        string plannedModel = ManagementModel;
        bool completed = false;
        var progress = new Progress<string>(text => { if (!completed) ManagementResult = text; });
        try
        {
            ManagementResult = "Wykonywanie polecenia…";
            var result = await engine.ExecuteAsync(command, token, onDelta: text => ((IProgress<string>)progress).Report(text));
            completed = true;
            ManagementResult = result.Text;
            if ((operation is "pull" or "delete") && ManagementModel == plannedModel)
                ExpectedConfirmation = command + " potwierdzam";
        }
        catch (OperationCanceledException) { ManagementResult = "Przerwano. Ukończone kroki nie są cofane."; }
        catch (Exception ex) { ManagementResult = "Nie udało się: " + ex.Message; }
        finally { completed = true; }
    }
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
