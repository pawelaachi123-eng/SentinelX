#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8618, CS8619, CS8620, CS8625, CS8629
using System.Globalization;
using System.IO;
using System.Text;
using SentinelX;
using SentinelX.Core;
using SentinelX.Services.AI;
using SentinelX.Services.History;
using SentinelX.Services.Settings;

namespace SentinelX.Services.Phone;

/// <summary>Rozpoznawanie mowy rozmówcy (STT). Implementacje muszą uczciwie zgłaszać gotowość —
/// bez modelu Sentinel NIE dzwoni, bo nie mógłby słuchać.</summary>
public interface ICallStt
{
    bool IsReady { get; }
    string Status();
    Task<string> TranscribeAsync(float[] samples, CancellationToken token);
}

/// <summary>Mowa Sentinela do rozmówcy (TTS → PCM 16 kHz mono dla mostu).</summary>
public interface ICallTts
{
    bool IsReady { get; }
    string Status();
    float[] Render(string text);
}

/// <summary>Lokalny Whisper (ten sam model co tryb głosowy — bez chmury).</summary>
public sealed class WindowsWhisperStt : ICallStt, IDisposable
{
    private readonly VoiceModelManager models = new();
    private WhisperFallbackAsrService? whisper;
    private bool initialized;

    public bool IsReady => models.IsWhisperReady();
    public string Status() => IsReady
        ? "rozpoznawanie lokalne (Whisper Small)"
        : "brak lokalnego modelu mowy (ggml-small) — pobierz go w ustawieniach Głos";

    private async Task<bool> EnsureAsync(CancellationToken token)
    {
        if (initialized && whisper is { IsReady: true }) return true;
        whisper = new WhisperFallbackAsrService(models.WhisperModelPath);
        await whisper.InitializeAsync(token);
        initialized = whisper.IsReady;
        return initialized;
    }

    public async Task<string> TranscribeAsync(float[] samples, CancellationToken token)
    {
        if (!await EnsureAsync(token)) return "";
        return (await whisper!.TranscribeAsync(samples, token)).Trim();
    }

    public void Dispose() => whisper?.Dispose();
}

/// <summary>Windows TTS renderowany do pamięci (16 kHz mono PCM float) — mowa idzie do telefonu przez most.</summary>
public sealed class WindowsTtsRenderer : ICallTts, IDisposable
{
    private readonly System.Speech.Synthesis.SpeechSynthesizer synth = new();
    private readonly string preferredVoice;

    public WindowsTtsRenderer(string preferredVoice = "") => this.preferredVoice = preferredVoice;
    public bool IsReady => true;
    public string Status() => "mowa Windows (lokalna, darmowa) · własny głos: z próbkami dostawcy, tylko za zgodą";

    public float[] Render(string text)
    {
        try
        {
            if (preferredVoice.Length > 0)
            {
                try { if (synth.Voice?.Name != preferredVoice) synth.SelectVoice(preferredVoice); }
                catch { /* głos nieobecny — zostaje domyślny */ }
            }
            using var wav = new MemoryStream();
            synth.SetOutputToWaveStream(wav); // SAPI zapisuje swój format w nagłówku WAV
            synth.Speak(text);
            synth.SetOutputToNull();
            return ToMono16k(wav.ToArray());
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Parsuje WAV (SAPI zwykle 22 kHz 16-bit mono) i przesymplowuje liniowo do 16 kHz mono float.</summary>
    private static float[] ToMono16k(byte[] wav)
    {
        int sampleRate = 22050, channels = 1, bits = 16;
        int dataStart = -1, dataLength = 0;
        int pos = 12;
        while (pos + 8 <= wav.Length)
        {
            string id = Encoding.ASCII.GetString(wav, pos, 4);
            int size = BitConverter.ToInt32(wav, pos + 4);
            if (size < 0 || pos + 8 + size > wav.Length) break;
            if (id == "fmt " && size >= 16)
            {
                channels = BitConverter.ToUInt16(wav, pos + 10);
                sampleRate = BitConverter.ToInt32(wav, pos + 12);
                bits = BitConverter.ToUInt16(wav, pos + 22);
            }
            else if (id == "data") { dataStart = pos + 8; dataLength = size; }
            pos += 8 + size + (size & 1);
        }
        if (dataStart < 0 || dataLength <= 0 || bits % 8 != 0 || channels < 1 || sampleRate <= 0) return [];
        int bytesPerSample = bits / 8;
        int frames = dataLength / (bytesPerSample * channels);
        var mono = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            int offset = dataStart + f * bytesPerSample * channels;
            mono[f] = bits switch
            {
                16 => BitConverter.ToInt16(wav, offset) / 32768f,
                32 => BitConverter.ToInt32(wav, offset) / 2147483648f,
                8 => (wav[offset] - 128) / 128f,
                _ => 0f,
            };
        }
        if (sampleRate == 16000) return mono;
        int outFrames = (int)((long)frames * 16000 / sampleRate);
        var result = new float[Math.Max(1, outFrames)];
        double step = (double)(frames - 1) / Math.Max(1, result.Length - 1);
        for (int i = 0; i < result.Length; i++)
        {
            double source = i * step;
            int i0 = (int)source, i1 = Math.Min(frames - 1, i0 + 1);
            result[i] = mono[i0] + (mono[i1] - mono[i0]) * (float)(source - i0);
        }
        return result;
    }

    public void Dispose() => synth.Dispose();
}

/// <summary>Przebieg jednej rozmowy (transkrypt w pamięci, zapisuje go PhoneCallStore).</summary>
public sealed class CallSession
{
    public List<PhoneTurn> Turns { get; } = [];
    public void Add(string speaker, string text)
    {
        if (text.Trim().Length == 0) return;
        Turns.Add(new PhoneTurn(speaker, text.Trim(), DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)));
    }
}

/// <summary>PHONECALL TOOL · zwykłe narzędzie głównego AI Sentinela — bez osobnej zakładki i asystenta.
/// „Sentinel, zadzwoń do restauracji X i zarezerwuj jutro stolik na 18:00 dla 4 osób” → Sentinel sam:
/// rozpoznaje telefon, znajdzie publiczny numer (przez sieć, z paskiem WiFi), zadzwoni Z TELEFONU
/// WŁAŚCICIELA (SIM/eSIM przez aplikację pomocniczą na Androidzie), poprowadzi rozmowę głosową
/// (rozmówca → STT → LLM → TTS → rozmówca), a zmianę terminu/ceny/opłaty PRZEDSTAWI właścicielowi
/// zamiast ją akceptować. „Gotowe” mówi WYŁĄCZNIE po prawdziwym połączeniu z potwierdzeniem celu.
/// Bez mostu, bez modelu mowy albo bez numeru — uczciwie odmawia i tłumaczy, czego brakuje.</summary>
public sealed class PhoneCallTool
{
    private const int MaxTurns = 10;
    private const int MaxConversationMilliseconds = 7 * 60 * 1000;
    private const int OffHookTimeoutMilliseconds = 45_000;
    /// <summary>Limit ciszy od rozmówcy (ms). Publiczny dla testów — CI nie może czekać 45 s.</summary>
    public static int SilenceTimeoutMilliseconds { get; set; } = 45_000;

    private readonly ISettingsService settings;
    private readonly WebAccessService web;
    private readonly IAiService ai;
    private readonly IPhoneBridge bridge;
    private readonly PhoneCallStore store;
    private readonly ActionHistoryService? history;
    private readonly Func<ICallStt> sttFactory;
    private readonly Func<ICallTts> ttsFactory;
    private PendingCall? pending;

    private sealed record PendingCall(PhoneCallRecord Record, PhoneTask Task, CallSession Session, string Question);

    public PhoneCallTool(ISettingsService settings, WebAccessService web, IAiService ai, IPhoneBridge bridge,
        PhoneCallStore store, ActionHistoryService? history = null,
        Func<ICallStt>? sttFactory = null, Func<ICallTts>? ttsFactory = null)
    {
        this.settings = settings;
        this.web = web;
        this.ai = ai;
        this.bridge = bridge;
        this.store = store;
        this.history = history;
        this.sttFactory = sttFactory ?? (() => new WindowsWhisperStt());
        this.ttsFactory = ttsFactory ?? (() => new WindowsTtsRenderer(settings.Current.Voice.SpeechVoice));
    }

    /// <summary>Czy polecenie wygląda na sprawę telefoniczną? Router używa tego, by NIE „naprawiać”
    /// takich zdań przez katalog („zadzwoń do…” nie jest literówką znanego polecenia).</summary>
    public static bool IsPhoneIntent(string normalized)
    {
        if (normalized.Length == 0) return false;
        return normalized.Contains("zadzwon", StringComparison.Ordinal)
            || normalized.StartsWith("telefon ", StringComparison.Ordinal)
            || normalized is "telefon status" or "status telefonu"
            || normalized.Contains("rozmowy historia", StringComparison.Ordinal)
            || normalized.StartsWith("rozmowy otworz", StringComparison.Ordinal)
            || normalized is "historia rozmow" or "historia polaczen" or "rozmowy telefoniczne" or "historia rozmow telefonicznych"
            || normalized is "most telefoniczny" or "wlacz most telefoniczny" or "wlacz most"
                or "wylacz most telefoniczny" or "wylacz most" or "polacz telefon";
    }

    // ————————————————————————————————— ROUTING —————————————————————————————————

    /// <summary>Zwraca odpowiedź albo null, gdy polecenie nie dotyczy telefonu (wtedy router idzie dalej).</summary>
    public async Task<string?> TryHandleAsync(string rawCommand, string normalized, CancellationToken token)
    {
        if (pending != null) return HandleDecision(normalized);
        if (normalized is "rozmowy historia" or "historia rozmow" or "historia polaczen" or "rozmowy telefoniczne" or "historia rozmow telefonicznych")
            return RenderHistory();
        if (normalized is "rozmowy otworz") return OpenTranscript("");
        if (normalized.StartsWith("rozmowy otworz ", StringComparison.Ordinal))
            return OpenTranscript(normalized["rozmowy otworz ".Length..].Trim());
        if (normalized is "most telefoniczny" or "status telefonu" or "polacz telefon" or "telefon status")
            return bridge.Status() + "\n" + HintForBridge();
        if (normalized is "wlacz most telefoniczny" or "wlacz most")
            return StartBridge();
        if (normalized is "wylacz most telefoniczny" or "wylacz most")
        {
            bridge.Stop();
            return "Most telefoniczny wyłączony — Sentinel nie odbierze poleceń wymagających telefonu do czasu włączenia.";
        }
        if (normalized.Contains("sms", StringComparison.Ordinal) && (normalized.Contains("zadzwon", StringComparison.Ordinal) || normalized.Contains("telefon", StringComparison.Ordinal) || normalized.Contains("wyslij", StringComparison.Ordinal)))
            return "SMS-ów na razie nie wysyłam — obsługuję tylko rozmowy głosowe z Twojego telefonu (SIM/eSIM).";

        bool phoneVerb = normalized.Contains("zadzwon", StringComparison.Ordinal)
            || normalized.StartsWith("telefon ", StringComparison.Ordinal)
            || normalized.StartsWith("zadzwonie", StringComparison.Ordinal)
            || (normalized.Contains(" telefon", StringComparison.Ordinal) && normalized.Contains("zamow", StringComparison.Ordinal));
        if (!phoneVerb) return null;
        return await StartCallAsync(rawCommand, normalized, token);
    }

    // ————————————————————————————————— DECYZJE WŁAŚCICIELA —————————————————————————————————

    private string HandleDecision(string normalized)
    {
        var current = pending!;
        string text = normalized.Trim().TrimEnd('.', '!', '?', ',');
        bool yes = text is "tak" or "tak to" or "potwierdz" or "ok" or "zgadzam" or "rezerwuj" or "przyjmij"
            || text.StartsWith("tak,", StringComparison.Ordinal) || text.StartsWith("tak ", StringComparison.Ordinal);
        bool no = text is "nie" or "odrzucam" or "nie rezerwuj" or "zostan" or "zostaje"
            || text.StartsWith("nie,", StringComparison.Ordinal) || text.StartsWith("nie ", StringComparison.Ordinal);
        if (!yes && !no)
            return "Czekam na decyzję w sprawie: „" + current.Question + "” Odpowiedz krótko: tak albo nie.";
        pending = null;
        if (no)
        {
            string summary = "Nie doszło do zgody — nie zaakceptowałem zmiany zaproponowanej przez rozmówcę.";
            store.ApplyDecision(current.Record.Id, "ODRZUCONE", summary, current.Question, current.Session.Turns, current.Record.Evidence);
            return "Zostawiam bez zgody: „" + current.Question + "” Rozmowa nie zakończyła się rezerwacją — nie nazywam tego sukcesem.";
        }
        return Task.Run(() => ResumeAsync(current, token: CancellationToken.None)).GetAwaiter().GetResult();
    }

    /// <summary>Ponowne połączenie z zachowanym kontekstem: „dzwonię ponownie, 17:00 będzie w porządku”.</summary>
    private async Task<string> ResumeAsync(PendingCall current, CancellationToken token)
    {
        string acceptance = ExtractAcceptance(current.Question);
        current.Session.Add("Właściciel", "Tak — przyjmuję zaproponowaną zmianę: " + acceptance);
        string response = await RunCallAsync(current.Task, current.Session, acceptance, current.Record.Id, token);
        return response;
    }

    private static string ExtractAcceptance(string question)
    {
        var match = System.Text.RegularExpressions.Regex.Match(question, @"\d{1,2}:\d{2}");
        return match.Success ? match.Value : "propozycję rozmówcy";
    }

    // ————————————————————————————————— HISTORIA —————————————————————————————————

    private string RenderHistory()
    {
        var calls = store.List();
        if (calls.Count == 0)
            return "Nie ma jeszcze żadnych rozmów telefonicznych. Poproś: „zadzwoń do …” — transkrypty pojawią się tutaj.";
        var lines = calls.AsEnumerable().Reverse().Take(20).Select(call =>
            $"{call.Id} [{call.Status}] {(call.TargetName.Length > 0 ? call.TargetName : call.Number)} — {(call.Summary.Length > 0 ? call.Summary : call.Goal)}"
            + "\n   otworz transkrypt: „rozmowy otworz " + call.Id + "”");
        return "Historia rozmów (najnowsze pierwsze):\n" + string.Join("\n", lines);
    }

    private string OpenTranscript(string id)
    {
        if (id.Length == 0) return "Podaj identyfikator, np. „rozmowy otworz ROZ-20260101-120000”. Lista: „rozmowy historia”.";
        var record = store.Find(id) ?? store.List().LastOrDefault(x => x.Id.EndsWith(id, StringComparison.OrdinalIgnoreCase));
        if (record == null) return "Nie znalazłem rozmowy „" + id + "”. Lista: „rozmowy historia”.";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = record.TranscriptPath,
                UseShellExecute = true,
            });
            return "Otwieram transkrypt " + record.Id + " (" + record.Status + "). Audio nie jest zapisywane — tylko tekst.";
        }
        catch (Exception ex)
        {
            return "Nie udało się otworzyć pliku (" + ex.GetType().Name + "). Transkrypt leży tutaj:\n" + record.TranscriptPath;
        }
    }

    // ————————————————————————————————— MOST —————————————————————————————————

    private string StartBridge()
    {
        var phone = settings.Current.Phone;
        if (phone.BridgeToken.Length == 0)
        {
            phone.BridgeToken = Guid.NewGuid().ToString("N")[..12];
            settings.Save();
        }
        bridge.Start(phone.BridgePort, phone.BridgeToken);
        return "Most telefoniczny włączony (port " + phone.BridgePort + ").\n"
            + "1. Uruchom aplikację pomocniczą na telefonie (folder companion/android w repo).\n"
            + "2. Wpisz adres tego komputera w sieci WiFi, port " + phone.BridgePort + " i token:\n   " + phone.BridgeToken + "\n"
            + "3. Po sparowaniu Sentinel zadzwoni Z TWOJEJ karty SIM. Status: " + bridge.Status();
    }

    private static string HintForBridge() =>
        "Aplikacja pomocnicza (companion/android): wpisz adres komputera, port i token, połącz — i Sentinel dzwoni z Twojej karty SIM.";

    // ————————————————————————————————— ZADZWON —————————————————————————————————

    private async Task<string> StartCallAsync(string rawCommand, string normalized, CancellationToken token)
    {
        var phoneSettings = settings.Current.Phone;
        var task = CallPolicy.Parse(rawCommand, normalized, phoneSettings.CallbackNumber, phoneSettings.OwnerName);
        if (task.MissingEssentials)
            return "Powiedz, do kogo zadzwonić i czego doprowadzić do końca, np. „zadzwoń do restauracji Fiesta i zarezerwuj jutro stolik na 18:00 dla 4 osób”.";
        if (task.PlaceName.Length > 0 && !task.HasNumber)
        {
            string? found = await FindPublicNumberAsync(task, token);
            if (found == null)
                return web.Enabled
                    ? "Nie znalazłem wiarygodnego publicznego numeru dla „" + task.PlaceName + "”. Podaj numer wprost (np. „zadzwoń na 123456789 do …”) — nie będę zgadywał."
                    : "Nie mogę znaleźć publicznego numeru „" + task.PlaceName + "”, bo dostęp do sieci jest wyłączony. Podaj numer wprost albo włącz sieć poleceniem „wifi on”.";
            task = task with { Number = found };
        }

        // UCZCIWE BRAMKI: bez mostu i bez słuchu nie ma telefonu — nigdy „na niby”.
        if (!bridge.IsConnected)
            return "Nie dzwonię — brak połączenia z aplikacją pomocniczą na Twoim telefonie.\n" + HintForBridge()
                + "\nStatus mostu: " + bridge.Status();
        var stt = sttFactory();
        if (!stt.IsReady)
            return "Nie dzwonię — nie umiałbym SŁUCHAĆ rozmówcy: " + stt.Status()
                + ". Bez tego rozmowa byłaby udawaniem, a tego nie robię.";
        var tts = ttsFactory();
        if (!tts.IsReady)
            return "Nie dzwonię — brak syntezatora mowy: " + tts.Status();

        string callId = store.NewCallId();
        var session = new CallSession();
        return await RunCallAsync(task, session, acceptance: null, callId, token);
    }

    private async Task<string?> FindPublicNumberAsync(PhoneTask task, CancellationToken token)
    {
        foreach (string query in new[] { task.PlaceName + " telefon kontaktowy", task.PlaceName + " numer telefonu restauracja" })
        {
            try
            {
                string response = await web.SearchAsync(query, token, 6);
                string? number = CallPolicy.FindPhoneNumber(response);
                if (number != null) return number;
            }
            catch (Exception ex) { AppLog.Write(ex); }
        }
        return null;
    }

    /// <summary>Jedna próba doprowadzenia sprawy do końca. „Gotowe” — tylko po OFFHOOK + potwierdzeniu celu.</summary>
    private async Task<string> RunCallAsync(PhoneTask task, CallSession session, string? acceptance, string callId, CancellationToken token)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        string evidence = "";
        string status = "NIEUDANE";
        string summary = "";
        string question = "";
        try
        {
            // 1. PRAWDZIWE wybieranie z karty SIM telefonu.
            await bridge.DialAsync(task.Number, callId);
            var dialDeadline = DateTime.UtcNow.AddMilliseconds(OffHookTimeoutMilliseconds);
            while (bridge.CallState is PhoneCallState.Idle or PhoneCallState.Dialing && DateTime.UtcNow < dialDeadline)
                await Task.Delay(200, token);
            if (bridge.CallState != PhoneCallState.OffHook)
            {
                evidence = "DIAL " + task.Number + " → telefon zgłosił " + bridge.CallState + " bez odebrania (czekano "
                    + OffHookTimeoutMilliseconds / 1000 + " s). Połączenie NIE zostało zawiązane.";
                return await FailAsync(task, session, callId, clock, status, "", "",
                    "połączenie nie doszło do skutku — telefon zgłosił " + bridge.CallState);
            }
            int offHookAfter = bridge.CallDurationSeconds;
            evidence = "DIAL " + task.Number + " → OFFHOOK po " + Math.Max(0, offHookAfter) + " s (stan zgłoszony przez telefon) · ";

            // 2. Prowadzenie rozmowy: rozmówca → STT → LLM → TTS → rozmówca.
            var stt = sttFactory();
            var tts = ttsFactory();
            var upQueue = new System.Collections.Concurrent.ConcurrentQueue<float[]>();
            void OnAudio(float[] chunk) => upQueue.Enqueue(chunk);
            bridge.AudioUp += OnAudio;
            string? materialQuestion = null;
            bool confirmed = false;
            int farTurns = 0;
            string confirmationTurn = "";
            try
            {
                var utterances = new UtteranceBuffer();
                var conversationDeadline = DateTime.UtcNow.AddMilliseconds(MaxConversationMilliseconds);
                for (int turn = 0; turn < MaxTurns && DateTime.UtcNow < conversationDeadline; turn++)
                {
                    string opening = acceptance == null
                        ? BuildOpeningLine(task)
                        : "Dobry dzień, dzwonię ponownie w tej samej sprawie. " + acceptance + " będzie w porządku, proszę o potwierdzenie.";
                    if (turn == 0) SpeakAndSend(tts, bridge, opening, session);

                    var farAudio = await ReadUtteranceAsync(upQueue, utterances, token);
                    if (farAudio == null)
                    {
                        SpeakAndSend(tts, bridge, "Czy mnie pan słyszy?", session);
                        farAudio = await ReadUtteranceAsync(upQueue, utterances, token);
                        if (farAudio == null)
                        {
                            evidence += "brak głosu rozmówcy przez " + SilenceTimeoutMilliseconds / 1000 + " s";
                            return await FailAsync(task, session, callId, clock, status, "", "",
                                "rozmówca milczał — połączenie było, ale celu nie potwierdzono");
                        }
                    }
                    string farText = (await stt.TranscribeAsync(farAudio, token)).Trim();
                    if (farText.Length == 0) continue;
                    farTurns++;
                    session.Add("Rozmówca", farText);

                    // CZUJNIK ZMIANY MATERIALNEJ — deterministyczny, przed modelem.
                    materialQuestion = CallPolicy.DetectMaterialChange(farText, task, acceptance);
                    if (materialQuestion != null)
                    {
                        SpeakAndSend(tts, bridge, CallPolicy.PauseLine(), session);
                        evidence += "zmiana materialna po " + farTurns + " zwrotach rozmówcy · rozmowa " + bridge.CallDurationSeconds + " s";
                        question = materialQuestion;
                        status = "UNVERIFIED";
                        summary = "";
                        var record = Persist(task, session, callId, clock, "CZEKA NA CIEBIE", "", question, evidence);
                        pending = new PendingCall(record, task, session, question);
                        return "📞 " + question + "\nOdpowiedz „tak” — zadzwonię ponownie z zaakceptowaną zmianą, albo „nie” — zostawię bez zgody.\nTranskrypt: " + record.TranscriptPath;
                    }
                    if (CallPolicy.IsConfirmation(farText))
                    {
                        confirmed = true;
                        confirmationTurn = farText;
                        SpeakAndSend(tts, bridge, "Dziękuję bardzo, do usłyszenia. Dobry dzień.", session);
                        break;
                    }

                    string reply = await ai.AskAsync(BuildPrompt(task, session.Turns, acceptance), "", token);
                    reply = PolishReply(reply);
                    if (reply.Length == 0) reply = "Dziękuję, chwileczkę.";
                    session.Add("Sentinel", reply);
                    SpeakAndSend(tts, bridge, reply, session);
                }
            }
            finally
            {
                bridge.AudioUp -= OnAudio;
                bridge.EndCall();
                await Task.Delay(600, CancellationToken.None);
            }

            if (!confirmed)
            {
                evidence += "zwroty rozmówcy: " + farTurns + " · brak potwierdzenia celu · rozmowa " + bridge.CallDurationSeconds + " s";
                return await FailAsync(task, session, callId, clock, status, "", "",
                    "rozmowa się odbyła, ale rozmówca NIE potwierdził realizacji celu");
            }
            evidence += "zwroty rozmówcy: " + farTurns + " · potwierdzenie: „" + TrimEvidence(confirmationTurn) + "” · rozmowa " + bridge.CallDurationSeconds + " s";
            summary = CallPolicy.SummaryFor(task);
            status = "VERIFIED";
            var done = Persist(task, session, callId, clock, "ZREALIZOWANE", summary, "", evidence);
            RecordEntry("CALL_TASK", "VERIFIED", "Połączenie wykonane i potwierdzone.", evidence, "", done);
            return "✅ " + summary + "\nDowód: " + evidence + "\nTranskrypt: " + done.TranscriptPath;
        }
        catch (OperationCanceledException)
        {
            bridge.EndCall();
            evidence += "przerwane przez właściciela po " + clock.ElapsedMilliseconds / 1000 + " s";
            Persist(task, session, callId, clock, "PRZERWANE", "", "", evidence);
            RecordEntry("CALL_TASK", "UNVERIFIED", "Przerwane przez właściciela.", evidence, "", null);
            return "Przerwałem rozmowę i zakończyłem połączenie. Stan zapisałem w „rozmowy historia”.";
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            bridge.EndCall();
            evidence += "wyjątek: " + ex.GetType().Name;
            return await FailAsync(task, session, callId, clock, status, "", "", "błąd wewnętrzny: " + ex.Message);
        }
    }

    private async Task<string> FailAsync(PhoneTask task, CallSession session, string callId,
        System.Diagnostics.Stopwatch clock, string status, string summary, string question, string reason)
    {
        string advice = RecoveryAdvisor.Advise("CALL_TASK", reason);
        var record = Persist(task, session, callId, clock, "NIEUDANE", summary, question, "NIEUDANE · " + reason);
        RecordEntry("CALL_TASK", "FAILED", "Nie udało się doprowadzić sprawy do końca: " + reason, record.Evidence, advice, record);
        return "✗ Nie udało się: " + reason + ".\n→ " + advice + "\nTranskrypt: " + record.TranscriptPath;
    }

    private PhoneCallRecord Persist(PhoneTask task, CallSession session, string callId,
        System.Diagnostics.Stopwatch clock, string status, string summary, string question, string evidence)
    {
        var record = new PhoneCallRecord(callId, DateTime.Now, task.PlaceName, task.Number, task.RawCommand,
            status, summary, (int)clock.ElapsedMilliseconds / 1000, session.Turns.ToList(), question, evidence, "");
        return store.Save(record);
    }

    private void RecordEntry(string type, string entryStatus, string message, string evidence, string advice, PhoneCallRecord? record)
    {
        try
        {
            ActionEvidenceCapture.Record(new ActionHistoryEntry
            {
                ActionId = record?.Id ?? store.NewCallId(),
                ActionType = type,
                Status = entryStatus,
                Command = record?.Goal ?? "rozmowa telefoniczna",
                Message = message,
                Evidence = evidence,
                RecoveryAdvice = advice,
                DurationMilliseconds = record is null ? 0 : record.DurationSeconds * 1000L,
            });
        }
        catch (Exception ex) { AppLog.Write(ex); }
    }

    // ————————————————————————————————— POMOCNICZE —————————————————————————————————

    private static async Task<float[]?> ReadUtteranceAsync(
        System.Collections.Concurrent.ConcurrentQueue<float[]> queue, UtteranceBuffer buffer, CancellationToken token)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(SilenceTimeoutMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            while (queue.TryDequeue(out var chunk))
            {
                var utterance = buffer.Feed(chunk);
                if (utterance != null) return utterance;
            }
            await Task.Delay(50, token);
        }
        return buffer.Flush();
    }

    private void SpeakAndSend(ICallTts tts, IPhoneBridge bridge, string text, CallSession session)
    {
        var audio = tts.Render(text);
        if (audio.Length > 0) bridge.SendTtsAudio(audio);
        session.Add("Sentinel", text);
    }

    private string BuildOpeningLine(PhoneTask task) =>
        "Dobry dzień! " + (task.WhenText.Length > 0 || task.PartySize > 0
            ? "Chciałbym " + (task.SubjectText.Length > 0 ? "zamówić " + task.SubjectText : "zapytać o rezerwację")
                + (task.WhenText.Length > 0 ? " na " + task.WhenText : "")
                + (task.PartySize > 0 ? " dla " + task.PartySize + " osób" : "") + "."
            : "Dzwonię w małej sprawie.");

    private static string BuildPrompt(PhoneTask task, IReadOnlyList<PhoneTurn> turns, string? acceptance)
    {
        var text = new StringBuilder();
        text.AppendLine("Prowadzisz PRAWDZIWĄ rozmowę telefoniczną w imieniu właściciela. Mów po polsku.");
        text.AppendLine("CEL: " + task.RawCommand);
        text.AppendLine("DANE WŁAŚCICIELA (używaj WYŁĄCZNIE tych — nie wymyślaj): " + task.KnownDataLine);
        if (acceptance != null) text.AppendLine("WŁAŚCICIEL ZAAKCEPTOWAŁ ZMIANĘ: " + acceptance + " — potwierdź ją wprost.");
        text.AppendLine("ZASADY: krótko (1–2 zdania), naturalnie i uprzejmie jak człowiek, nie chatbotowo; nie wymyślaj faktów; NIE zgadzaj się na żadną zmianę terminu, daty, ceny, opłaty ani warunków — wtedy powiedz „sprawdzę i oddzwonię”; gdy cel osiągnięty, podziękuj i pożegnaj się.");
        text.AppendLine("ROZMOWA DO TEJ PORY:");
        foreach (var turn in turns) text.AppendLine(turn.Speaker + ": " + turn.Text);
        text.AppendLine("Wypowiedz TYLKO swoje słowa — bez opisów scenicznych i cudzysłowów.");
        return text.ToString();
    }

    private static string PolishReply(string reply)
    {
        string text = (reply ?? "").Trim().Trim('"', '„', '”');
        text = text.Replace("*", "");
        var cut = text.IndexOf('\n');
        if (cut > 0) text = text[..cut].Trim();
        if (text.Length > 320) text = text[..320].Trim();
        if (text.Length > 0 && !char.IsPunctuation(text[^1])) text += ".";
        return text;
    }

    private static string TrimEvidence(string text)
    {
        string clean = (text ?? "").Replace("\n", " ").Trim();
        return clean.Length <= 80 ? clean : clean[..80] + "…";
    }
}
