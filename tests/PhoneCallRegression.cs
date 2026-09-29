using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentinelX;
using SentinelX.Core;
using SentinelX.Core.Verification;
using SentinelX.Services.AI;
using SentinelX.Services.Phone;
using SentinelX.Services.Settings;

namespace SentinelX.Tests;

/// <summary>0.99 · PHONECALL TOOL (46. zestaw) — zwykłe narzędzie głównego AI do PRAWDZIWYCH rozmów
/// z karty SIM (przez most Android). Testy idą na atrapach mostu/STT/TTS/AI, więc są szybkie i pewne,
/// ale pilnują wszystkiego, czego nie wolno udawać:
/// parsowanie zadania · uczciwe odmowy (brak mostu / brak modelu mowy / brak numeru) · prawdziwy dowód
/// OFFHOOK · czujnik ZMIANY MATERIALNEJ z pytaniem do właściciela · ponowne połączenie z kontekstem
/// po „tak” · „nie” bez fałszywego sukcesu · transkrypty i historia · reguła sądu CALL_ · rada naprawy.</summary>
internal static class PhoneCallRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TEST FAILED: " + message);
    }

    // ————— atrapy: most, słuch, mowa, model —————

    private sealed class FakeBridge : IPhoneBridge
    {
        public bool IsConnected { get; set; } = true;
        public string ConnectedDevice => "Pixel testowy";
        public PhoneCallState CallState { get; set; } = PhoneCallState.Idle;
        public int CallDurationSeconds => CallState == PhoneCallState.OffHook ? 12 : 0;
        public List<string> DialedNumbers { get; } = [];
        public int TtsCount;
        private readonly Queue<float[]> scripted = new();
        public event Action<PhoneCallState>? CallStateChanged;
        public event Action<float[]>? AudioUp;
        public event Action<string>? Log;
        public void ScriptUtterance() => scripted.Enqueue(BuildWave());
        public string Status() => IsConnected ? "Most: Pixel testowy · połączony." : "Most telefoniczny: wyłączony.";
        public void Start(int port, string token) { }
        public void Stop() => IsConnected = false;
        public Task DialAsync(string number, string callId)
        {
            DialedNumbers.Add(number);
            CallState = PhoneCallState.OffHook; // prawdziwy most dostaje to z TelephonyManager
            return Task.CompletedTask;
        }
        public void SendTtsAudio(float[] samples)
        {
            TtsCount++;
            if (scripted.Count > 0 && AudioUp != null) AudioUp.Invoke(scripted.Dequeue());
        }
        public void EndCall() => CallState = PhoneCallState.Ended;
    }

    private static float[] BuildWave()
    {
        // 0,4 s ciszy + 0,6 s tonu (ponad próg) + 0,9 s ciszy → pełna „wypowiedź” dla energy-VAD
        int rate = 16000;
        var samples = new List<float>((int)(rate * 1.9));
        samples.AddRange(Enumerable.Repeat(0f, rate * 2 / 5));
        for (int i = 0; i < rate * 3 / 5; i++) samples.Add(0.3f * (float)Math.Sin(2 * Math.PI * 440 * i / rate));
        samples.AddRange(Enumerable.Repeat(0f, rate * 9 / 10));
        return samples.ToArray();
    }

    private sealed class FakeStt : ICallStt
    {
        private readonly Queue<string> replies;
        public FakeStt(params string[] replies) => this.replies = new Queue<string>(replies);
        public bool IsReady => true;
        public string Status() => "atrapa słuchu";
        public Task<string> TranscribeAsync(float[] samples, CancellationToken token) =>
            Task.FromResult(replies.Count > 0 ? replies.Dequeue() : "");
    }

    private sealed class FakeTts : ICallTts
    {
        public bool IsReady => true;
        public string Status() => "atrapa mowy";
        public float[] Render(string text) => new float[160];
    }

    private sealed class FakeAi : IAiService
    {
        public Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken token = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string> SelectModelAsync(string model, CancellationToken token = default) => Task.FromResult(model);
        public Task<string> AskAsync(string input, string context, CancellationToken token) =>
            Task.FromResult("Dziękuję bardzo, rezerwacja dla czterech osób na jutro.");
        public string RoutingReason => "atrapa modelu";
        public void Cancel() { }
    }

    private sealed class FakeSettings : ISettingsService
    {
        public SentinelSettings Current { get; } = new();
        public string? LastError => null;
        public event Action? Changed { add { } remove { } }
        public void Save() { }
        public void ResetSection(string section) { }
    }

    public static Task RunAsync(string directory)
    {
        int savedSilence = PhoneCallTool.SilenceTimeoutMilliseconds;
        PhoneCallTool.SilenceTimeoutMilliseconds = 300;
        try
        {
            RunChecked(directory);
        }
        finally
        {
            PhoneCallTool.SilenceTimeoutMilliseconds = savedSilence;
        }
        return Task.CompletedTask;
    }

    private static void RunChecked(string directory)
    {
        Directory.CreateDirectory(directory);

        // ————— 1. parsowanie zadania z mowy właściciela —————
        var task = CallPolicy.Parse(
            "Sentinel, zadzwoń do restauracji Fiesta i zarezerwuj jutro stolik na 18:00 dla 4 osób",
            "sentinel zadzwon do restauracji fiesta i zarezerwuj jutro stolik na 18:00 dla 4 osob",
            callbackNumber: "786843433", ownerName: "Pan Piotr");
        Check(task.PlaceName == "Fiesta", "parsowanie: nazwa miejsca = " + task.PlaceName);
        Check(task.WhenText.Contains("jutro") && task.WhenText.Contains("18:00"), "parsowanie: termin = " + task.WhenText);
        Check(task.PartySize == 4, "parsowanie: liczba osób = " + task.PartySize);
        Check(task.CallbackNumber == "786843433", "parsowanie: numer kontaktowy właściciela podany z ustawień");
        Check(CallPolicy.Parse("zadzwon na 987654321", "zadzwon na 987654321", "").HasNumber
            && CallPolicy.Parse("zadzwon na 987654321", "zadzwon na 987654321", "").Number == "+48987654321",
            "parsowanie: numer z polecenia → +48987654321");
        Check(CallPolicy.Parse("zadzwon do pizzerii na 123456789 i zarezerwuj stolik",
            "zadzwon do pizzerii na 123456789 i zarezerwuj stolik", "").PlaceName == "pizzerii",
            "parsowanie: numer NIE zanieczyszcza nazwy miejsca („" + CallPolicy.Parse("zadzwon do pizzerii na 123456789 i zarezerwuj stolik",
            "zadzwon do pizzerii na 123456789 i zarezerwuj stolik", "").PlaceName + "”)");

        // ————— 2. czujnik ZMIANY MATERIALNEJ — nieakceptuję niczego sam —————
        var change = CallPolicy.DetectMaterialChange("Niestety 18:00 mamy zajęte, możemy dać 17:00.", task);
        Check(change != null && change.Contains("17:00"), "zmiana materialna: proponowana godzina 17:00 trafia do właściciela");
        Check(CallPolicy.DetectMaterialChange("Opłata rezerwacyjna 20 zł.", task) != null,
            "zmiana materialna: opłata/cena też wymaga zgody właściciela");
        Check(CallPolicy.DetectMaterialChange("Stolik zarezerwowany, do zobaczenia.", task) == null,
            "potwierdzenie celu NIE jest zmianą materialną");
        Check(CallPolicy.IsConfirmation("Stolik zarezerwowany, do zobaczenia."), "czujnik potwierdzenia: rezerwacja przyjęta");

        // ————— 3. podsumowanie jak u człowieka —————
        string summary = CallPolicy.SummaryFor(task);
        Check(summary.Contains("Gotowe.") && summary.Contains("18:00") && summary.Contains("4 os."),
            "podsumowanie: „" + summary + "”");

        // ————— 4. energy-VAD składa wypowiedź z ciszy, mowy i ciszy —————
        var buffer = new UtteranceBuffer();
        float[]? utterance = null;
        var wave = BuildWave();
        for (int offset = 0; offset < wave.Length && utterance == null; offset += 1600)
            utterance = buffer.Feed(wave[offset..Math.Min(wave.Length, offset + 1600)]);
        Check(utterance != null && utterance.Length > 8000, "VAD: wypowiedź złożona z " + (utterance?.Length ?? 0) + " próbek");

        // ————— 5. uczciwa odmowa: brak mostu = brak telefonu —————
        string callDir = Path.Combine(directory, "calls");
        var bridge = new FakeBridge { IsConnected = false };
        var tool = NewTool(callDir, bridge, new FakeStt(), scriptedUtterances: false);
        string refused = tool.TryHandleAsync("zadzwoń do restauracji Fiesta na 123456789",
            "zadzwon do restauracji fiesta na 123456789", CancellationToken.None).GetAwaiter().GetResult()!;
        Check(refused.Contains("Nie dzwonię"), "brak mostu: uczciwa odmowa, zero udawania");
        Check(NewStore(callDir).List().Count == 0, "brak mostu: żadnej rozmowy nie zapisano jako zakończoną");

        // ————— 6. uczciwa odmowa: brak numeru i brak sieci (wifi off) —————
        var bridgeOk = new FakeBridge { IsConnected = true };
        var toolNoNumber = NewTool(Path.Combine(directory, "calls2"), bridgeOk, new FakeStt(), scriptedUtterances: false);
        string needNumber = toolNoNumber.TryHandleAsync("zadzwoń do restauracji Fiesta i zarezerwuj stolik",
            "zadzwon do restauracji fiesta i zarezerwuj stolik", CancellationToken.None).GetAwaiter().GetResult()!;
        Check(needNumber.Contains("wifi on"), "brak numeru bez sieci: uczciwa prośba o numer albo „wifi on”");

        // ————— 7. pełna uczciwa ścieżka: PRAWDZIWE połączenie (atrapa mostu) + potwierdzenie = „Gotowe” —————
        string callDir3 = Path.Combine(directory, "calls3");
        var liveBridge = new FakeBridge { IsConnected = true };
        liveBridge.ScriptUtterance();
        var liveTool = NewTool(callDir3, liveBridge, new FakeStt("Stolik zarezerwowany, do zobaczenia jutro."), scriptedUtterances: true);
        string done = liveTool.TryHandleAsync("Sentinel, zadzwoń do restauracji Fiesta na 123456789 i zarezerwuj jutro stolik na 18:00 dla 4 osób",
            "zadzwon do restauracji fiesta na 123456789 i zarezerwuj jutro stolik na 18:00 dla 4 osob", CancellationToken.None).GetAwaiter().GetResult()!;
        Check(done.Contains("✅") && done.Contains("Gotowe."), "sukces tylko po prawdziwym połączeniu: „" + done.Split('\n')[0] + "”");
        Check(liveBridge.DialedNumbers.Single() == "+48123456789", "most dostał numer do wybrania (+48 z 9 cyfr)");
        var liveRecord = NewStore(callDir3).List().Single();
        Check(liveRecord.Status == "ZREALIZOWANE", "historia: status ZREALIZOWANE, nie udajemy niczego innego");
        Check(liveRecord.Evidence.Contains("OFFHOOK") && liveRecord.Evidence.Contains("123456789"),
            "dowód: OFFHOOK + numer (" + liveRecord.Evidence + ")");
        Check(File.Exists(liveRecord.TranscriptPath), "transkrypt zapisany na dysku");
        Check(File.ReadAllText(liveRecord.TranscriptPath).Contains("Rozmówca") && File.ReadAllText(liveRecord.TranscriptPath).Contains("Sentinel"),
            "transkrypt ma obie strony rozmowy i jest tekstowy (bez audio)");

        // ————— 8. zmiana materialna W TRAKCIE rozmowy → pytanie → „tak” → ponowne połączenie z kontekstem —————
        string callDir4 = Path.Combine(directory, "calls4");
        var redialBridge = new FakeBridge { IsConnected = true };
        redialBridge.ScriptUtterance();
        redialBridge.ScriptUtterance();
        redialBridge.ScriptUtterance();
        var redialTool = NewTool(callDir4, redialBridge,
            new FakeStt("18:00 mamy zajęte, możemy dać 17:00.", "Potwierdzam, stolik zarezerwowany na 17:00."),
            scriptedUtterances: true);
        string paused = redialTool.TryHandleAsync("zadzwoń do restauracji Fiesta na 123456789 i zarezerwuj jutro stolik na 18:00 dla 4 osób",
            "zadzwon do restauracji fiesta na 123456789 i zarezerwuj jutro stolik na 18:00 dla 4 osob", CancellationToken.None).GetAwaiter().GetResult()!;
        Check(paused.Contains("17:00") && paused.Contains("tak"), "właściciel decyduje: „" + paused.Split('\n')[0] + "”");
        var pendingRecord = NewStore(callDir4).List().Single();
        Check(pendingRecord.Status == "CZEKA NA CIEBIE", "status przed decyzją: CZEKA NA CIEBIE (nie „gotowe”!)");
        string approved = redialTool.TryHandleAsync("tak", "tak", CancellationToken.None).GetAwaiter().GetResult()!;
        Check(approved.Contains("Gotowe.") && approved.Contains("✅"), "po „tak”: ponowne połączenie i uczciwe „Gotowe”");
        Check(redialBridge.DialedNumbers.Count == 2, "Sentinel zadzwonił PONOWNIE po decyzji właściciela (" + redialBridge.DialedNumbers.Count + "×)");
        var finalRecord = NewStore(callDir4).List().Single();
        Check(finalRecord.Status == "ZREALIZOWANE" && finalRecord.Evidence.Contains("OFFHOOK"),
            "ponowna rozmowa też ma prawdziwy dowód OFFHOOK");
        Check(finalRecord.Turns.Any(t => t.Speaker == "Rozmówca" && t.Text.Contains("17:00")),
            "kontekst rozmowy zachowany (17:00 w transkrypcie)");

        // ————— 9. „nie” = bez zgody, bez fałszywego sukcesu —————
        string callDir5 = Path.Combine(directory, "calls5");
        var rejectBridge = new FakeBridge { IsConnected = true };
        rejectBridge.ScriptUtterance();
        var rejectTool = NewTool(callDir5, rejectBridge,
            new FakeStt("18:00 mamy zajęte, możemy dać 17:00."), scriptedUtterances: true);
        rejectTool.TryHandleAsync("zadzwoń do restauracji Fiesta na 123456789 i zarezerwuj jutro stolik na 18:00",
            "zadzwon do restauracji fiesta na 123456789 i zarezerwuj jutro stolik na 18:00", CancellationToken.None).GetAwaiter().GetResult();
        string rejected = rejectTool.TryHandleAsync("nie", "nie", CancellationToken.None).GetAwaiter().GetResult()!;
        Check(rejected.Contains("nie nazywam tego sukcesem"), "po „nie”: zero fałszywego sukcesu");
        Check(NewStore(callDir5).List().Single().Status == "ODRZUCONE", "historia: status ODRZUCONE");
        Check(rejectBridge.DialedNumbers.Count == 1, "po „nie” NIE akceptuję zmiany drugą rundą dzwonienia");

        // ————— 10. milczenie rozmówcy = uczciwa porażka —————
        string callDir6 = Path.Combine(directory, "calls6");
        var silentBridge = new FakeBridge { IsConnected = true };
        var silentTool = NewTool(callDir6, silentBridge, new FakeStt(), scriptedUtterances: false);
        string silent = silentTool.TryHandleAsync("zadzwoń do restauracji Fiesta na 123456789 i zarezerwuj stolik na 18:00",
            "zadzwon do restauracji fiesta na 123456789 i zarezerwuj stolik na 18:00", CancellationToken.None).GetAwaiter().GetResult()!;
        Check(silent.Contains("✗") && silent.Contains("Nie udało się"), "cisza rozmówcy: uczciwa porażka, nie sukces");
        Check(silent.Contains("→"), "porażka ma wykonywalną radę naprawy");
        Check(NewStore(callDir6).List().Single().Status == "NIEUDANE", "historia: status NIEUDANE");

        // ————— 11. reguła sądu 9: CALL_ VERIFIED bez OFFHOOK/numeru jest obalana —————
        var blindEntry = new ActionHistoryEntry
        {
            ActionId = "SX-CALL-1", ActionType = "CALL_TASK", Status = "VERIFIED",
            Message = "Zadzwoniłem.", Evidence = "rozmowa przebiegła pomyślnie.", Timestamp = DateTime.Now,
        };
        var (blindPassed, blindFindings) = VerificationCenter.Evaluate("zadzwoniłem", "CALL_TASK", [blindEntry]);
        Check(!blindPassed && blindFindings[0].Contains("OFFHOOK"), "sąd: CALL_ VERIFIED bez OFFHOOK/numeru zostaje obalony");
        var realEntry = new ActionHistoryEntry
        {
            ActionId = "SX-CALL-2", ActionType = "CALL_TASK", Status = "VERIFIED",
            Message = "Zadzwoniłem i potwierdzono.", Timestamp = DateTime.Now,
            Evidence = "DIAL 123456789 → OFFHOOK po 2 s (stan zgłoszony przez telefon) · zwroty rozmówcy: 2 · potwierdzenie: „zarezerwowane” · rozmowa 95 s",
        };
        var (realPassed, _) = VerificationCenter.Evaluate("zadzwoniłem", "CALL_TASK", [realEntry]);
        Check(realPassed, "sąd: prawdziwy dowód OFFHOOK + numer przechodzi");

        // ————— 12. rada naprawy dla rodziny CALL_ —————
        string advice = RecoveryAdvisor.Advise("CALL_TASK", "most rozłączony");
        Check(advice.Contains("most", StringComparison.OrdinalIgnoreCase) && advice.Contains("telefonie", StringComparison.OrdinalIgnoreCase),
            "rada naprawy CALL_: konkretna (most/aplikacja pomocnicza/głośnik)");

        // ————— 13. historia rozmów przez polecenie —————
        string history = NewTool(callDir3, new FakeBridge(), new FakeStt(), scriptedUtterances: false)
            .TryHandleAsync("historia rozmów", "historia rozmow", CancellationToken.None).GetAwaiter().GetResult()!;
        Check(history.Contains("Historia rozmów") && history.Contains("ZREALIZOWANE"), "„historia rozmów” pokazuje rozmowy i statusy");

        string report = "PASS\nparsowanie (miejsce/termin/osoby/numer), zmiana materialna → decyzja właściciela,\n" +
            "uczciwe odmowy (brak mostu / brak numeru bez sieci), prawdziwy dowód OFFHOOK,\n" +
            "ponowne połączenie z kontekstem po „tak”, „nie” bez fałszywego sukcesu,\n" +
            "cisza = porażka z radą, sąd CALL_ (OFFHOOK + numer), rada naprawy, historia i transkrypty\n";
        File.WriteAllText(Path.Combine(directory, "phone-call.txt"), report);
    }

    private static PhoneCallStore NewStore(string directory) => new(directory);

    private static PhoneCallTool NewTool(string callDirectory, FakeBridge bridge, FakeStt stt, bool scriptedUtterances) =>
        new(new FakeSettings(), new WebAccessService(), new FakeAi(), bridge, NewStore(callDirectory),
            history: null,
            sttFactory: () => stt,
            ttsFactory: () => new FakeTts());
}
