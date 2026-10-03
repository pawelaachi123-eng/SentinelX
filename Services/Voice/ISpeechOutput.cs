namespace SentinelX.Services.Voice;

/// <summary>The Windows-local speech output boundary used by the voice session coordinator.</summary>
public interface ISpeechOutput
{
    bool IsSpeaking { get; }
    event Action? Completed;
    event Action<string>? Failed;
    void Speak(string text, string voice, int rate, int volume);
    void Stop();
}
