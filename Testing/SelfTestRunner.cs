using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SentinelX;
internal static class SelfTestRunner
{
    internal static async Task RunAsync(MainWindow window, string directory)
    {
        Directory.CreateDirectory(directory);
        var results = new List<string>();
        void Check(bool passed, string name) { if (!passed) throw new InvalidOperationException("TEST FAILED: " + name); results.Add(name); }
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(1000);
        results.AddRange(await UpgradeRegressionRunner.RunAsync(Path.Combine(directory, "upgrade")));
        await AiReliabilityTestRunner.RunAsync(Path.Combine(directory, "ai-transport"));
        Check((await window.ExecuteAsync("ustaw próg VAD na 0.35")).Contains("0.35"), "Komenda live VAD przez UI");
        Check((await window.ExecuteAsync("ustaw opacity overlay na 60%")).Contains("60"), "Komenda live ustawień nakładki");
        Check((await window.ExecuteAsync("sprawdź siebie")).Contains("SELF DIAGNOSTIC"), "Raport zdrowia aplikacji przez UI");
        Check((await window.ExecuteAsync("..tasks")).Contains("zadań"), "Rejestr zadań przez komendę czatu");
        Check((await window.ExecuteAsync("ile używam RAM")).Contains("GB"), "Bieżący RAM przez interfejs");
        Check((await window.ExecuteAsync("..help")).Contains("SZYBKIE KOMENDY"), "Komenda ..help działa lokalnie");
        Check((await window.ExecuteAsync("2+2")).Contains("4"), "Prosta matematyka działa bez AI");
        Check((await window.ExecuteAsync("dlaczego kruki odlatują z Polski na zimę")).Contains("zwykle nie odlatują"), "Proste sprostowanie wiedzy działa bez AI");
        Check((await window.ExecuteAsync("ustaw timeout rozmowy na 20 min")).Contains("20 min"), "Timeout rozmowy zmieniany komendą live");
        Check((await window.ExecuteAsync("ustaw głośność odpowiedzi na 70%")).Contains("70%"), "Głośność TTS zmieniana komendą live");
        Check((await window.ExecuteAsync("ustaw watch cpu 85")).Contains("85%"), "Próg Watch CPU zmieniany komendą live");
        Check((await window.ExecuteAsync("..settings")).Contains("USTAWIENIA SENTINEL"), "Sekcyjny settings summary działa");
        Check((await window.ExecuteAsync("mam na imię Testowy")).Contains("Testowy"), "Zapamiętanie imienia");
        Check((await window.ExecuteAsync("jak mam na imię")).Contains("Testowy"), "Odczyt imienia");
        await window.ExecuteAsync("zapamiętaj: preferuję kolor morski");
        await window.ExecuteAsync("nowa rozmowa");
        Check((await window.ExecuteAsync("co pamiętasz")).Contains("morski"), "Wspomnienia trwają między sesjami");
        var reloaded = new ConversationMemoryService();
        Check(reloaded.UserName == "Testowy", "Pamięć odtwarzana z dysku");
        string fixture = "test-" + Guid.NewGuid().ToString("N") + ".txt";
        Check((await window.ExecuteAsync($"utwórz plik {fixture}: Zażółć gęślą jaźń")).Contains("sprawdzono"), "Tworzenie i weryfikacja pliku UTF-8");
        Check((await window.ExecuteAsync($"utwórz plik {fixture}: inny tekst")).Contains("już istnieje"), "Ochrona istniejących plików");
        Check((await LocalFileService.CreateAsync("../outside.txt", "x")).Contains("zwykłą nazwę"), "Ochrona przed wyjściem ze wskazanego katalogu");
        Check((await LocalFileService.CreateAsync("CON.txt", "x")).Contains("zwykłą nazwę"), "Nazwy urządzeń Windows są odrzucane");
        await window.ExecuteAsync("zamknij notatnik");
        Check((await window.ExecuteAsync("potwierdź", true)).Contains("w oknie"), "Głos nie potwierdza zamknięcia aplikacji");
        await window.ExecuteAsync("anuluj");
        Check((await window.ExecuteAsync("potwierdź")).Contains("Brak akcji"), "Anulowana akcja nie jest wykonywana");
        await window.ExecuteAsync("awaryjny stop");
        Check((await window.ExecuteAsync("ile używam RAM")).Contains("blokuje"), "STOP blokuje nowe zadania");
        await window.ExecuteAsync("wznów Sentinel");
        Check((await window.ExecuteAsync("ile używam RAM")).Contains("GB"), "Wznowienie po STOP");
        var handler = new FakeOllama();
        using var ai = new LocalAiService(new GamingModeService(), handler, Path.Combine(directory, "mock-ai"));
        using var monitor = new SystemMonitor();
        var router = new CommandRouter(monitor, new SystemInfoService(), ai, reloaded);
        Check((await router.ProcessAsync("Czy moje obecne użycie CPU i RAM wygląda dobrze do grania?")) == "ODPOWIEDŹ MODELU", "Złożone pytanie CPU/RAM trafia do AI");
        Check((await router.ProcessAsync("Czy palenie zwiększa ryzyko raka?")) == "ODPOWIEDŹ MODELU", "Pytanie ogólne nie wpada do komendy RAM");
        Check(handler.ChatCount == 2, "Dokładnie dwa zapytania do testowego modelu");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        bool cancelled = false;
        try { await router.ProcessAsync("pytanie", canceled.Token); } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "Anulowanie przekazywane do routera");
        Check(!LocalAiService.IsLocalModelName("qwen3:cloud"), "Model chmurowy odrzucony");
        Check(LocalAiService.CleanAnswer("<think>ukryte</think>Wynik") == "Wynik", "Usuwanie znaczników rozumowania");
        string[] modelSet = ["qwen3:1.7b", "qwen3:4b-instruct", "gemma3:4b", "gemma3:1b"];
        Check(LocalAiService.SelectModel(modelSet, true, "") == "qwen3:1.7b", "Obciążenie preferuje qwen3:1.7b");
        Check(LocalAiService.SelectModel(modelSet, false, "") == "qwen3:4b-instruct", "Luz systemu preferuje qwen3:4b-instruct");
        Check(LocalAiService.SelectModels(modelSet, false, "qwen3:1.7b").Take(2).SequenceEqual(["qwen3:1.7b", "qwen3:4b-instruct"]), "Model ręczny ma fallback auto");
        string legacyDir = Path.Combine(directory, "legacy-settings");
        Directory.CreateDirectory(legacyDir);
        File.WriteAllText(Path.Combine(legacyDir, "settings.json"), "{\"ConversationMinutes\":99,\"SpeechVolume\":150,\"WatchEnabled\":true}");
        var migratedSettings = new AppSettingsService(legacyDir);
        Check(migratedSettings.Settings.Voice.ConversationTimeoutMinutes == 60 && migratedSettings.Settings.Voice.SpeechVolume == 100 && migratedSettings.Settings.Watch.Enabled, "Stary settings.json migruje i waliduje zakresy");
        var enhancer = new AudioEnhancementService(); enhancer.Reset();
        float[] silence = new float[512];
        for (int i = 0; i < 70; i++) enhancer.ProcessSamples(silence);
        Check(!enhancer.IsCalibrating, "Kalibracja kończy się po 2 sekundach próbek");
        Check(enhancer.ProcessSamples(silence).EnhancedRms == 0, "Cisza pozostaje ciszą");
        Check(AudioEnhancementService.RmsToMeter(0) == 0, "Miernik ciszy wynosi zero");
        Check(Math.Abs(AudioEnhancementService.RmsToDbfs(0.01f) + 40) < 0.01, "Miernik dBFS ma prawidłową skalę");
        var random = new Random(778); float[] noise = Enumerable.Range(0, 512).Select(_ => (float)((random.NextDouble() - 0.5) * 0.002)).ToArray();
        enhancer.Reset(); for (int i = 0; i < 80; i++) enhancer.ProcessSamples(noise);
        Check(enhancer.ProcessSamples(noise).Gain < 2, "Szum po kalibracji nie jest wzmacniany 20×");
        float[] quietSpeech = Enumerable.Range(0, 1600).Select(i => MathF.Sin(i * 0.08f) * 0.006f).ToArray();
        Check(enhancer.ProcessSamples(quietSpeech).Gain > 1, "Cicha mowa dostaje bezpieczne wzmocnienie");
        Check(!VoiceTranscriptFilter.ContainsWakeWord("sentinelowy"), "Wybudzenie wymaga całego słowa");
        Check(VoiceTranscriptFilter.ContainsWakeWord("Sentinel, pokaż RAM"), "Polskie polecenie z wybudzeniem");
        Check(VoiceTranscriptFilter.ContainsWakeWord("Centenel pokaż RAM"), "Typowy błąd ASR nadal budzi Sentinel");
        Check(VoiceTranscriptFilter.IsSuspicious("Dziękuję za oglądanie.", 16000), "Typowa halucynacja ASR odrzucana");
        using (var voice = new VoiceRecognitionService())
        {
            bool missing = false; try { await voice.InitializeAsync(); } catch (InvalidOperationException ex) { missing = ex.Message.Contains("modelu"); }
            Check(missing && !voice.IsReady && !voice.IsListening, "Brak modeli nie uruchamia mikrofonu ani pobierania");
        }
        string inspect = await window.ExecuteAsync("analizuj plik " + Path.Combine(AppPaths.Root, "CreatedFiles", fixture));
        Check(inspect.Contains("SHA-256") && inspect.Contains("VERIFIED"), "Analiza istniejącego pliku z dowodem");
        using var json = JsonDocument.Parse("{\"x\":null,\"y\":false}");
        Check(PcDiagnosticService.BooleanStatus(json.RootElement, "x") == "NIEZNANY" && PcDiagnosticService.BooleanStatus(json.RootElement, "y") == "WYŁĄCZONA", "Brak odczytu nie jest mylony z wyłączonym zabezpieczeniem");
        foreach (string page in new[] { "Chat", "System", "Tools", "Voice", "Gaming", "Memory", "Settings", "AI", "Actions", "Programs" })
        {
            window.SelectTestPage(page); await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
            Capture(window, Path.Combine(directory, page + ".png"));
            Point composer = window.CommandInput.TranslatePoint(new Point(), window);
            Check(composer.Y + window.CommandInput.ActualHeight <= window.ActualHeight, "Pole polecenia mieści się: " + page);
        }
        window.Width = 980; window.Height = 700; window.SelectTestPage("Voice");
        await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
        Capture(window, Path.Combine(directory, "minimum-size.png"));
        Check(window.CommandInput.ActualWidth > 300, "Minimalna szerokość okna pozostaje użyteczna");
        File.WriteAllText(Path.Combine(directory, "results.json"), JsonSerializer.Serialize(new { passed = results.Count, checks = results, note = "Testy izolowane: bez nagrywania mikrofonu, bez zmiany ustawień Windows, bez rzeczywistego zapytania do modelu AI." }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static void Capture(Window window, string path)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
    private sealed class FakeOllama : HttpMessageHandler
    {
        public int ChatCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            bool chat = request.RequestUri!.AbsolutePath == "/api/chat"; if (chat) ChatCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(chat ? "{\"message\":{\"content\":\"ODPOWIEDŹ MODELU\"}}" : "{\"models\":[{\"name\":\"qwen3:1.7b\"}]}", Encoding.UTF8, "application/json") });
        }
    }
}
