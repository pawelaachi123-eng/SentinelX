using SentinelX.Models;
using SentinelX.Services.AI;
using SentinelX.Services.History;
using SentinelX.Services.Settings;
using SentinelX.Services.Voice;
namespace SentinelX.Services.Readiness;

/// <summary>Read-only probes. Never captures audio, downloads models, launches Ollama or changes settings.</summary>
public sealed class ReadinessService(ISettingsService settings, IHistoryService history, IVoiceService voice, IAiService ai,
    SentinelX.ConversationMemoryService? memory = null, SentinelX.TaskService? tasks = null) : IReadinessService
{
    public async Task<IReadOnlyList<ReadinessCheck>> CheckAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var local = Task.Run(() =>
        {
            var checks = new List<ReadinessCheck>();
            string? error = settings.LastError ?? history.StorageError;
            checks.Add(new("storage", "Ustawienia i historia", error == null ? ReadinessState.Ready : ReadinessState.Unavailable,
                error ?? "Wczytano konfigurację. Zapis jest sprawdzany przy każdej zmianie. Dane pozostają w %LOCALAPPDATA%\\SentinelX.", "settings", "Otwórz ustawienia"));
            if (memory != null)
            {
                string? memoryError = memory.LastStorageError;
                checks.Add(new("memory", "Pamięć rozmów", memoryError == null ? ReadinessState.Ready : ReadinessState.Unavailable,
                    memoryError ?? "Magazyn lokalny działa; zakres zapisu i użycia przez AI zależy od ustawień prywatności.", "memory", "Sprawdź Pamięć i prywatność"));
            }
            if (tasks != null)
            {
                string? taskError = tasks.LastStorageError;
                checks.Add(new("tasks", "Zadania i przypomnienia", taskError == null ? ReadinessState.Ready : ReadinessState.Unavailable,
                    taskError ?? "Lokalny magazyn zadań odpowiada; przypomnienia powiadamiają tylko przy uruchomionej aplikacji.", "tasks", "Otwórz Zadania"));
            }
            token.ThrowIfCancellationRequested();
            try
            {
                int count = voice.GetMicrophones().Count;
                checks.Add(new("microphone", "Mikrofon", count > 0 ? ReadinessState.Ready : ReadinessState.NeedsSetup,
                    count > 0 ? $"Wykryto urządzenia: {count}. Sygnał i dostęp sprawdzisz po włączeniu głosu." : "Nie wykryto mikrofonu. Polecenia wpisywane nadal działają.", "voice", "Wybierz mikrofon"));
            }
            catch (Exception ex) { checks.Add(new("microphone", "Mikrofon", ReadinessState.Unavailable, ex.Message, "voice", "Sprawdź urządzenia")); }
            token.ThrowIfCancellationRequested();
            try
            {
                bool ready = voice.HasLocalModels;
                checks.Add(new("asr", "Rozpoznawanie mowy", ready ? ReadinessState.Ready : ReadinessState.NeedsSetup,
                    ready ? "Lokalne pliki ASR i VAD są dostępne. Ich uruchomienie zostanie sprawdzone po włączeniu głosu." : "Brak kompletnego zestawu lokalnych modeli. Nic nie jest pobierane bez Twojej decyzji.", "voice", ready ? "Przetestuj głos" : "Skonfiguruj modele"));
            }
            catch (Exception ex) { checks.Add(new("asr", "Rozpoznawanie mowy", ReadinessState.Unavailable, ex.Message, "voice", "Sprawdź modele")); }
            return checks;
        }, token);
        var ollama = CheckAiAsync(token);
        var results = await local.WaitAsync(token);
        results.Add(await ollama);
        return results;
    }
    private async Task<ReadinessCheck> CheckAiAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var models = await ai.GetModelsAsync(timeout.Token).WaitAsync(timeout.Token);
            return new("ollama", "Ollama · tylko lokalnie", models.Count > 0 ? ReadinessState.Ready : ReadinessState.NeedsSetup,
                models.Count > 0 ? $"Usługa odpowiada · modeli: {models.Count}. Nie uruchamiano generowania odpowiedzi." : "Ollama działa, ale nie ma lokalnych modeli. Zainstaluj np. qwen3:4b.", "ai", "Wybierz model");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return new("ollama", "Ollama · tylko lokalnie", ReadinessState.NeedsSetup, "Brak odpowiedzi w 3 sekundy. Uruchom Ollama i sprawdź ponownie. Narzędzia systemowe działają bez niej.", "ai", "Sprawdź połączenie"); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        { return new("ollama", "Ollama · tylko lokalnie", ReadinessState.NeedsSetup, "Nie połączono z lokalną usługą. Uruchom Ollama. " + ex.Message, "ai", "Skonfiguruj AI"); }
    }
}
