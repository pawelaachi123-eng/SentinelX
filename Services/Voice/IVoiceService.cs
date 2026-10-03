using SentinelX;
using SentinelX.Models;
namespace SentinelX.Services.Voice;
public sealed record VoiceMetrics(VoiceState State, double Raw, double Enhanced, double Noise, double Snr, double Gain, bool SpeechDetected, string Transcript);
public interface IVoiceService
{
    VoiceState State { get; }
    VoiceStateInfo Lifecycle { get; }
    string Status { get; }
    bool HasLocalModels { get; }
    event Action? Changed;
    event Action<VoiceMetrics>? MetricsUpdated;
    event Action<string>? CommandRecognized;
    IReadOnlyList<string> GetMicrophones();
    Task StartAsync(int device, bool downloadModels, CancellationToken token);
    void Stop();
    void Calibrate();
    void Speak(string text);
}
