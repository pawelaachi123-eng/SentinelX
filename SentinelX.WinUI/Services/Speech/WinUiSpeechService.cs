using System.Text.RegularExpressions;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace SentinelX;

/// <summary>
/// Windows speech synthesis for the WinUI shell (Windows.Media, no SAPI).
/// Same fully-qualified name and public API as the WPF build, so the shared
/// voice path (<see cref="Services.Voice.VoiceService"/>) compiles unchanged.
/// </summary>
public sealed class SpeechOutputService : IDisposable
{
    private readonly SpeechSynthesizer? synthesizer;
    private readonly MediaPlayer? player;
    private readonly List<VoiceInformation> installed = [];
    private bool disposed;

    public IReadOnlyList<string> Voices { get; private set; } = [];
    public string PreferredVoice { get; private set; } = "";
    public string Status { get; private set; } = "Brak głosu syntezy Windows.";
    public bool Available => synthesizer != null && player != null && Voices.Count > 0;
    public bool IsSpeaking { get; private set; }
    public event Action? Completed;
    public event Action<string>? Failed;

    public SpeechOutputService()
    {
        try
        {
            installed.AddRange(SpeechSynthesizer.AllVoices);
            if (installed.Count == 0)
            {
                Status = "Windows nie ma zainstalowanych głosów syntezy.";
                return;
            }

            synthesizer = new SpeechSynthesizer();
            player = new MediaPlayer { AudioCategory = MediaPlayerAudioCategory.Speech };
            player.MediaEnded += (_, _) => Finish(true, "");
            player.MediaFailed += (_, e) => Finish(false, e.ErrorMessage);

            Voices = installed.Select(v => v.DisplayName).Distinct().ToArray();
            var polish = installed.FirstOrDefault(v => v.Language.StartsWith("pl", StringComparison.OrdinalIgnoreCase));
            PreferredVoice = (polish ?? installed[0]).DisplayName;
            Status = polish != null
                ? "Polski głos Windows dostępny. Synteza działa lokalnie."
                : "Brak polskiego głosu Windows. Dostępne głosy są na liście; polska wymowa może być niepoprawna.";
        }
        catch (Exception ex)
        {
            Status = "Synteza głosu niedostępna: " + ex.Message;
            AppLog.Write(ex);
        }
    }

    public void Speak(string text, string voice, int rate, int volume)
    {
        if (!Available || disposed)
        {
            Failed?.Invoke(Status);
            return;
        }

        // Read a short answer, not a long report, command code or filesystem evidence.
        text = Regex.Replace(text, @"```[\s\S]*?```", "Kod znajdziesz w oknie.");
        text = Regex.Replace(text, @"https?://\S+", "link w oknie");
        text = Regex.Replace(text, @"[A-Za-z]:\\\S+", "plik zapisany lokalnie");
        text = text.Replace("**", "").Replace("`", "");
        if (text.Length > 360) text = text[..360] + ". Dalsze szczegóły są w oknie.";
        if (string.IsNullOrWhiteSpace(text))
        {
            Completed?.Invoke();
            return;
        }

        _ = SpeakAsync(text, voice, rate, volume);
    }

    private async Task SpeakAsync(string text, string voice, int rate, int volume)
    {
        if (synthesizer == null || player == null || disposed) return;
        try
        {
            var match = installed.FirstOrDefault(v => v.DisplayName == voice)
                ?? installed.FirstOrDefault(v => v.DisplayName == PreferredVoice)
                ?? installed[0];
            synthesizer.Voice = match;

            player.Pause();
            player.Source = null;
            SpeechSynthesisStream stream = await synthesizer.SynthesizeTextToStreamAsync(text);
            if (disposed) return;
            player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
            player.Volume = Math.Clamp(volume, 0, 100) / 100d;
            player.PlaybackSession.PlaybackRate = Math.Clamp(1.0 + Math.Clamp(rate, -5, 5) * 0.12, 0.25, 2.0);
            IsSpeaking = true;
            player.Play();
        }
        catch (Exception ex)
        {
            Finish(false, "Nie udało się odczytać odpowiedzi: " + ex.Message);
        }
    }

    private void Finish(bool ok, string error)
    {
        IsSpeaking = false;
        try
        {
            if (ok) Completed?.Invoke();
            else Failed?.Invoke(error);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
        }
    }

    public void Stop()
    {
        try
        {
            player?.Pause();
            if (player != null) player.Source = null;
        }
        catch
        {
        }

        IsSpeaking = false;
        Completed?.Invoke();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            player?.Pause();
            player?.Dispose();
            synthesizer?.Dispose();
        }
        catch
        {
        }
    }
}
