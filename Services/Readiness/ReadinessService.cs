using SentinelX.Models;
using SentinelX.Services.AI;
using SentinelX.Services.History;
using SentinelX.Services.Settings;
using SentinelX.Services.Voice;
namespace SentinelX.Services.Readiness;

/// <summary>Read-only probes. Never captures audio, downloads models or changes settings (the engine downloads by itself, in the background).</summary>
public sealed class ReadinessService(ISettingsService settings, IHistoryService history, IVoiceService voice, IAiService ai, Services.Engine.IEngineService? engine = null) : IReadinessService
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
            return checks;
        }, token);
        var aiCheck = CheckAiAsync(token);
        var results = await local.WaitAsync(token);
        results.Add(await aiCheck);
        return results;
    }
    private async Task<ReadinessCheck> CheckAiAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        string progress = engine == null ? "" : " " + engine.Status.Message;
        try
        {
            var models = await ai.GetModelsAsync(timeout.Token).WaitAsync(timeout.Token);
            return new("ai", "Silnik AI · wbudowany", models.Count > 0 ? ReadinessState.Ready : ReadinessState.NeedsSetup,
                models.Count > 0 ? $"Działa lokalnie, bez Ollamy · modeli: {models.Count}. Nie uruchamiano generowania odpowiedzi." + (engine?.Status.State == "installing" ? progress : "")
                    : "Silnik AI instaluje się sam w tle — nic nie musisz robić." + progress, "ai", "Zobacz silnik AI");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return new("ai", "Silnik AI · wbudowany", ReadinessState.NeedsSetup, "Silnik AI nie odpowiedział w 3 sekundy. Startuje sam; narzędzia systemowe działają bez niego." + progress, "ai", "Sprawdź silnik AI"); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        { return new("ai", "Silnik AI · wbudowany", ReadinessState.NeedsSetup, "Silnik AI jeszcze nie jest gotowy (naprawi się sam). " + ex.Message + progress, "ai", "Sprawdź silnik AI"); }
    }
}
