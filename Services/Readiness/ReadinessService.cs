using SentinelX.Models;
using SentinelX.Services.AI;
using SentinelX.Services.History;
using SentinelX.Services.Settings;
using SentinelX.Services.Voice;
namespace SentinelX.Services.Readiness;

/// <summary>Read-only probes. Never captures audio, downloads models, launches Ollama or changes settings.</summary>
public sealed class ReadinessService(ISettingsService settings, IHistoryService history, IVoiceService voice, IAiService ai,
    SentinelX.Services.Actions.StepRunner? steps = null) : IReadinessService
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
            // 0.96 · warstwa JARVIS: sprawdzamy SAMĄ konfigurację — bez łączenia się z Open-Meteo,
            // bez czytania stanu domu i bez generowania czegokolwiek przez model.
            var jarvis = settings.Current.Jarvis;
            checks.Add(new("jarvis-network", "Pogoda · Open-Meteo", jarvis.WeatherEnabled ? ReadinessState.Ready : ReadinessState.NeedsSetup,
                jarvis.WeatherEnabled
                    ? $"Włączone · miasto domyślne: {jarvis.DefaultCity} · zapis ważny {jarvis.WeatherCacheMinutes} min. To jedyne polecenie, które wychodzi do internetu (bez konta i bez klucza API)."
                    : "Wyłączone — Sentinel nie łączy się z internetem w ogóle. „pogoda” odpowie wtedy, że jest wyłączone, zamiast zgadywać temperaturę.",
                "settings", jarvis.WeatherEnabled ? "Sprawdź pogodę" : "Włącz w ustawieniach"));
            token.ThrowIfCancellationRequested();
            // Agent: „gotowy” tylko gdy włączony I podłączony do kolejki. Dom: „gotowy” tylko gdy
            // włączony i ma adres — bez adresu nie ma czego sprawdzać, więc to dalej „do konfiguracji”.
            bool agentReady = !jarvis.AgentEnabled || steps is { IsReady: true };
            bool homeReady = !jarvis.HomeEnabled || jarvis.HomeBaseUrl.Trim().Length > 0;
            bool anythingOn = jarvis.AgentEnabled || jarvis.HomeEnabled;
            checks.Add(new("jarvis-agent", "Agent, dom i sekwencje",
                anythingOn && agentReady && homeReady ? ReadinessState.Ready : ReadinessState.NeedsSetup,
                (jarvis.AgentEnabled
                    ? agentReady ? $"Tryb agenta włączony · limit {Math.Clamp(jarvis.AgentMaxSteps, 1, 8)} kroków · narzędzia wyłącznie do odczytu."
                        : "Tryb agenta włączony, ale kolejka wykonań nie jest podłączona — działa w oknie Sentinela."
                    : "Tryb agenta wyłączony (model nie wywołuje żadnych narzędzi). ") +
                (jarvis.HomeEnabled ? $"Home Assistant: {jarvis.HomeBaseUrl} · token ze zmiennej SENTINEL_HA_TOKEN."
                    : "Sterowanie domem wyłączone — trzeba je włączyć świadomie, bo dotyczy fizycznych urządzeń."),
                "settings", "Otwórz ustawienia"));
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
