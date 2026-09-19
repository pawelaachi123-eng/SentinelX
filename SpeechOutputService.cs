using System.Speech.Synthesis;
using System.Text.RegularExpressions;

namespace SentinelX;
public sealed class SpeechOutputService : IDisposable
{
    private SpeechSynthesizer? synthesizer;
    private Prompt? activePrompt;
    public IReadOnlyList<string> Voices { get; private set; } = [];
    public string PreferredVoice { get; private set; } = "";
    public string Status { get; private set; } = "Brak głosu syntezy Windows.";
    public bool Available => synthesizer != null && Voices.Count > 0;
    public bool IsSpeaking => activePrompt != null;
    public event Action? Completed;
    public event Action<string>? Failed;
    public SpeechOutputService()
    {
        try
        {
            synthesizer = new SpeechSynthesizer();
            var installed = synthesizer.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo).ToArray();
            Voices = installed.Select(v => v.Name).ToArray();
            PreferredVoice = installed.FirstOrDefault(v => v.Culture.TwoLetterISOLanguageName == "pl")?.Name ?? Voices.FirstOrDefault() ?? "";
            Status = installed.Any(v => v.Culture.TwoLetterISOLanguageName == "pl") ? "Polski głos Windows dostępny. Synteza działa lokalnie." : "Brak polskiego głosu Windows SAPI. Dostępne głosy są na liście; polska wymowa może być niepoprawna.";
            synthesizer.SetOutputToDefaultAudioDevice();
            synthesizer.SpeakCompleted += (_, e) =>
            {
                if (!ReferenceEquals(activePrompt, e.Prompt)) return;
                activePrompt = null;
                if (e.Error != null) Failed?.Invoke("Błąd syntezy: " + e.Error.Message); else Completed?.Invoke();
            };
        }
        catch (Exception ex) { synthesizer?.Dispose(); synthesizer = null; Status = "Synteza głosu niedostępna: " + ex.Message; }
    }
    public void Speak(string text, string voice, int rate, int volume)
    {
        if (!Available) { Failed?.Invoke(Status); return; }
        try
        {
            activePrompt = null; synthesizer!.SpeakAsyncCancelAll();
            synthesizer.SelectVoice(Voices.Contains(voice) ? voice : PreferredVoice);
            synthesizer.Rate = Math.Clamp(rate, -5, 5); synthesizer.Volume = Math.Clamp(volume, 0, 100);
            // Read a short answer, not a long report, command code or filesystem evidence.
            text = Regex.Replace(text, @"```[\s\S]*?```", "Kod znajdziesz w oknie.");
            text = Regex.Replace(text, @"https?://\S+", "link w oknie");
            text = Regex.Replace(text, @"[A-Za-z]:\\\S+", "plik zapisany lokalnie");
            text = text.Replace("**", "").Replace("`", "");
            if (text.Length > 360) text = text[..360] + ". Dalsze szczegóły są w oknie.";
            if (string.IsNullOrWhiteSpace(text)) { Completed?.Invoke(); return; }
            activePrompt = new Prompt(text); synthesizer.SpeakAsync(activePrompt);
        }
        catch (Exception ex) { activePrompt = null; Failed?.Invoke("Nie udało się odczytać odpowiedzi: " + ex.Message); }
    }
    public void Stop() { activePrompt = null; try { synthesizer?.SpeakAsyncCancelAll(); } catch { } Completed?.Invoke(); }
    public void Dispose() { Stop(); synthesizer?.Dispose(); synthesizer = null; }
}
