namespace SentinelX.Services.Voice;

/// <summary>The local microphone/ASR boundary consumed by the voice session coordinator.</summary>
public interface IVoiceCapture
{
    bool HasLocalModels { get; }
    bool IsListening { get; }
    bool IsWakeOnlyMode { get; }
    bool RecognitionSuppressed { get; set; }
    float CurrentRawAudioLevel { get; }
    float CurrentEnhancedRms { get; }
    float CurrentNoiseFloor { get; }
    float CurrentSnrDb { get; }
    float CurrentGain { get; }
    bool SpeechDetected { get; }
    string LastTranscript { get; }

    event Action<string>? SpeechRecognized;
    event Action<bool>? RecognitionProcessingChanged;
    event Action<string>? StatusChanged;
    event Action<string>? ErrorOccurred;

    IReadOnlyList<string> GetMicrophones();
    Task InitializeAsync(bool allowModelDownloads, CancellationToken cancellationToken = default);
    void SetWakeOnlyMode(bool enabled);
    void StartListening(int deviceNumber = 0);
    void StopListening();
    void ApplySettings();
    void CalibrateNoise();
}
