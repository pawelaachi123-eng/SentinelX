using System.IO;
using System.Text.Json;

namespace SentinelX;

internal static class UpgradeRegressionRunner
{
    internal static async Task<IReadOnlyList<string>> RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<string>();
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException("UPGRADE TEST: " + label); checks.Add(label); }
        Check(AppLaunchPlan.TryParseCommand("Proszę odpal mi Discorda i YouTube.", out var target) && AppLaunchPlan.Parse(target).Targets.Count == 2, "Naturalna komenda z dwoma celami");
        Check(AppLaunchPlan.Parse("Discorda oraz Steam i YouTube").Targets.Count == 3, "Trzy znane aplikacje w jednym planie");
        Check(AppLaunchPlan.Parse("Discord i discorda").Targets.Count == 1, "Alias nie uruchamia aplikacji dwa razy");
        Check(AppLaunchPlan.Parse("Ratchet i Clank").Targets.Single() == "Ratchet i Clank", "Spójnik w nazwie gry nie jest listą aplikacji");
        Check(!AppLaunchPlan.Parse("Discord i nieznana-aplikacja").IsValid, "Niejednoznaczny plan zatrzymany przed pierwszym działaniem");
        Check(!AppLauncherService.IsSafeWebUrl("javascript:alert(1)") && !AppLauncherService.IsSafeWebUrl("https://user:pass@host.test"), "Odrzucone niebezpieczne URI i osadzone dane logowania");
        Check(AppLauncherService.BuildSearchUrl("CS2 & smoke?", true).EndsWith("CS2%20%26%20smoke%3F"), "Wyszukiwanie poprawnie koduje parametry");
        var tasks = new ActionTaskRegistry();
        using (var task = tasks.Begin("test1", "plan", 2, CancellationToken.None))
        {
            task.SetStep("pierwszy"); task.CompleteStep();
            Check(tasks.GetTasks().Single().CompletedSteps == 1, "Postęp zadania odpowiada ukończonym krokom");
            Check(tasks.CancelAll() == 1 && task.Token.IsCancellationRequested, "Emergency Stop anuluje token aktywnego planu");
        }
        Check(tasks.GetTasks().Single().Status == "CANCELLED", "Anulowane zadanie trafia do historii sesji");
        string historyRoot = Path.Combine(directory, "recovery");
        Directory.CreateDirectory(Path.Combine(historyRoot, "History"));
        File.WriteAllText(Path.Combine(historyRoot, "History", "actions.jsonl"), "{\"actionId\":\"old\",\"sessionId\":\"old-session\",\"status\":\"RUNNING\",\"timestamp\":\"2026-01-01T00:00:00\"}\n");
        var history = new ActionHistoryService(historyRoot);
        Check(history.GetRecentEntries().Single().Status == "INTERRUPTED", "Restart oznacza przerwaną akcję bez ponownego wykonania");
        string settingsDir = Path.Combine(directory, "settings");
        var store = new AppSettingsService(settingsDir); store.Save();
        var field = SettingsCatalog.Create(store).Single(x => x.Label == "Próg VAD");
        Check(field.Write("NaN") != null && field.Write("9") != null, "Ustawienia odrzucają NaN i wartości poza zakresem");
        Check(field.Write("0,35") == null, "Ustawienia przyjmują polski separator dziesiętny"); store.Save();
        Check(new AppSettingsService(settingsDir).Settings.Voice.VadThreshold == .35, "Zmiana VAD odtworzona po ponownym odczycie");
        Check(File.Exists(store.SettingsPath + ".backup"), "Poprzedni zapis ustawień ma kopię zapasową");
        string import = Path.Combine(directory, "bad-settings.json"); File.WriteAllText(import, "[]");
        Check(!store.Import(import) && store.Settings.Voice.VadThreshold == .35, "Błędny import nie zastępuje działających ustawień");
        File.WriteAllText(import, "{\"Voice\":{\"VadThreshold\":0.5},\"Ui\":{\"AccentColor\":\"invalid\"}}");
        Check(store.Import(import) && store.Settings.Voice.VadThreshold == .5 && store.Settings.Ui.AccentColor.StartsWith('#'), "Import waliduje sekcje i kolor");
        File.WriteAllText(import, "{\"voice\":{\"VadThreshold\":0.6}}");
        Check(store.Import(import) && store.Settings.Voice.VadThreshold == .6, "Import zachowuje sekcje z małymi literami");
        var options = VoiceRuntimeOptions.FromSettings(new VoiceSettings { VadThreshold = double.NaN, MaximumGain = 100 });
        var inbox = new VoiceCommandInbox();
        for (int i = 0; i < 4; i++) Check(inbox.TryEnqueue("polecenie " + i), "Kolejkowanie wypowiedzi " + i);
        Check(!inbox.TryEnqueue("nadmiar") && inbox.TryDequeue(out string first) && first == "polecenie 0", "Kolejka głosu jest ograniczona i zachowuje kolejność");
        inbox.Clear(); Check(inbox.Count == 0, "STOP czyści oczekujące polecenia głosowe");
        Check(options.VadThreshold == .42f && options.MaximumGain == 12, "Voice dostaje skończoną i ograniczoną konfigurację");
        Check(!VoiceTranscriptFilter.ContainsWakeWord("Centenel", "Strict") && VoiceTranscriptFilter.ContainsWakeWord("Centenel", "Balanced"), "Tryb wybudzenia faktycznie zmienia filtr");
        string logDirectory = Path.Combine(directory, "voice-log");
        var log = new VoiceUtteranceLogService(logDirectory, 1024, 2);
        for (int i = 0; i < 12; i++) log.TryWrite(new() { Decision = "test-rejected", Engine = "test", Transcript = null });
        await log.DisposeAsync();
        string logText = File.ReadAllText(log.LogPath);
        Check(logText.Contains("test-rejected") && logText.Contains("\"transcriptRedacted\":true"), "Log zapisuje decyzję i oznacza ukrytą transkrypcję");
        Check(Directory.GetFiles(logDirectory).Length <= 3 && File.Exists(log.LogPath + ".1"), "Log Voice jest rotowany i ograniczony rozmiarem");
        string instanceId = "test-" + Guid.NewGuid();
        using (var primary = new SingleInstanceService(instanceId))
        using (var secondary = new SingleInstanceService(instanceId))
            Check(primary.IsPrimary && !secondary.IsPrimary, "Tylko jedna instancja dla danego katalogu danych");
        var files = new FileWorkspaceService(Path.Combine(directory, "files"), Path.Combine(directory, "desktop"));
        Check((await files.ProcessAsync("stwórz plik notatka.txt na pulpicie", default))!.StartsWith("Gotowe", StringComparison.Ordinal), "Tworzenie pliku w wskazanym pulpicie testowym");
        Check((await files.ProcessAsync("wpisz do niego pierwsza", default))!.StartsWith("Gotowe", StringComparison.Ordinal), "Edycja ostatnio utworzonego pliku");
        await files.ProcessAsync("dopisz do niego druga", default);
        Check(File.ReadAllText(files.LastFile!).Contains("druga") && Directory.GetFiles(Path.GetDirectoryName(files.LastFile!)!, "*.bak").Length == 2, "Dopisywanie zachowuje treść i kopie poprzednich wersji");
        await files.ProcessAsync("zmien 2 linie na poprawiona", default);
        Check(File.ReadAllText(files.LastFile!).Contains("poprawiona"), "Edycja wskazanej linii pliku");
        Check((await files.ProcessAsync("stwórz plik ../escape.txt na pulpicie", default))!.StartsWith("Nie udało się", StringComparison.Ordinal), "Polecenie plikowe nie opuszcza katalogu docelowego");
        Check((await files.ProcessAsync("stwórz plik notatka.txt na pulpicie", default))!.StartsWith("Nie udało się", StringComparison.Ordinal), "Tworzenie nie nadpisuje istniejącego pliku");
        return checks;
    }
}
