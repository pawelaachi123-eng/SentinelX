using System.Speech.Synthesis;
using System.Text.RegularExpressions;
using SentinelX.Services.Voice;

namespace SentinelX;

public sealed class SpeechOutputService : ISpeechOutput, IDisposable
{
    private readonly object gate = new();
    private SpeechSynthesizer? synthesizer;
    private Prompt? activePrompt;
    private bool disposed;

    public IReadOnlyList<string> Voices { get; private set; } = [];
    public string PreferredVoice { get; private set; } = "";
    public string Status { get; private set; } = "Brak głosu syntezy Windows.";
    public bool Available { get { lock (gate) return synthesizer != null && Voices.Count > 0 && !disposed; } }
    public bool IsSpeaking { get { lock (gate) return activePrompt != null; } }
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
            Status = installed.Any(v => v.Culture.TwoLetterISOLanguageName == "pl")
                ? "Polski głos Windows dostępny. Synteza działa lokalnie."
                : "Brak polskiego głosu Windows SAPI. Dostępne głosy są na liście; polska wymowa może być niepoprawna.";
            synthesizer.SetOutputToDefaultAudioDevice();
            synthesizer.SpeakCompleted += SpeakCompleted;
        }
        catch (Exception ex)
        {
            try { synthesizer?.Dispose(); }
            catch (Exception cleanupError) { AppLog.Write("Voice", "Warning", "Failed to release a partially initialized speech synthesizer.", cleanupError); }
            synthesizer = null;
            Status = "Synteza głosu niedostępna: " + ex.Message;
            AppLog.Write("Voice", "Warning", "Speech synthesis is unavailable; voice input remains available.", ex);
        }
    }

    public void Speak(string text, string voice, int rate, int volume)
    {
        Prompt? prompt = null;
        string? failure = null;
        bool empty = false;
        lock (gate)
        {
            if (disposed) { failure = "Synteza głosu została zamknięta."; }
            else if (synthesizer == null || Voices.Count == 0) { failure = Status; }
            else
            {
                try
                {
                    activePrompt = null;
                    synthesizer.SpeakAsyncCancelAll();
                    synthesizer.SelectVoice(Voices.Contains(voice) ? voice : PreferredVoice);
                    synthesizer.Rate = Math.Clamp(rate, -5, 5);
                    synthesizer.Volume = Math.Clamp(volume, 0, 100);
                    // Read a short answer, not a long report, command code or filesystem evidence.
                    text = Regex.Replace(text ?? "", @"```[\s\S]*?```", "Kod znajdziesz w oknie.");
                    text = Regex.Replace(text, @"https?://\S+", "link w oknie");
                    text = Regex.Replace(text, @"[A-Za-z]:\\\S+", "plik zapisany lokalnie");
                    text = text.Replace("**", "").Replace("`", "");
                    if (text.Length > 360) text = text[..360] + ". Dalsze szczegóły są w oknie.";
                    if (string.IsNullOrWhiteSpace(text)) empty = true;
                    else
                    {
                        prompt = new Prompt(text);
                        activePrompt = prompt;
                        synthesizer.SpeakAsync(prompt);
                    }
                }
                catch (Exception ex)
                {
                    activePrompt = null;
                    failure = "Nie udało się odczytać odpowiedzi: " + ex.Message;
                }
            }
        }

        if (failure != null) Publish(Failed, failure);
        else if (empty) Publish(Completed);
    }

    private void SpeakCompleted(object? sender, SpeakCompletedEventArgs e)
    {
        lock (gate)
        {
            if (!ReferenceEquals(activePrompt, e.Prompt)) return;
            activePrompt = null;
        }
        if (e.Error != null) Publish(Failed, "Błąd syntezy: " + e.Error.Message);
        else Publish(Completed);
    }

    public void Stop()
    {
        lock (gate)
        {
            if (disposed) return;
            activePrompt = null;
            try { synthesizer?.SpeakAsyncCancelAll(); }
            catch (Exception ex) { AppLog.Write("Voice", "Warning", "Speech cancellation failed.", ex); }
        }
        // Completion is also the cancellation signal consumed by the microphone suppression guard.
        Publish(Completed);
    }

    private static void Publish(Action? observers)
    {
        foreach (Action observer in observers?.GetInvocationList() ?? [])
        {
            try { observer(); }
            catch (Exception ex) { AppLog.Write("Voice", "Warning", "A speech-completion observer failed.", ex); }
        }
    }

    private static void Publish(Action<string>? observers, string message)
    {
        foreach (Action<string> observer in observers?.GetInvocationList() ?? [])
        {
            try { observer(message); }
            catch (Exception ex) { AppLog.Write("Voice", "Warning", "A speech-failure observer failed.", ex); }
        }
    }

    public void Dispose()
    {
        SpeechSynthesizer? current;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            activePrompt = null;
            current = synthesizer;
            synthesizer = null;
            if (current != null) current.SpeakCompleted -= SpeakCompleted;
        }
        try { current?.SpeakAsyncCancelAll(); }
        catch (Exception ex) { AppLog.Write("Voice", "Warning", "Speech cancellation during disposal failed.", ex); }
        try { current?.Dispose(); }
        catch (Exception ex) { AppLog.Write("Voice", "Warning", "Speech synthesizer disposal failed.", ex); }
        Publish(Completed);
    }
}
