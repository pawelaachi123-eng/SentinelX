namespace SentinelX;

/// <summary>Validated immutable snapshot; readers never observe half-applied slider edits.</summary>
public sealed record VoiceRuntimeOptions
{
    public int EndOfSpeechMilliseconds { get; init; } = 650;
    public int MinimumSpeechMilliseconds { get; init; } = 200;
    public int MaximumSpeechSeconds { get; init; } = 20;
    public float VadThreshold { get; init; } = 0.42f;
    public float GainTargetRms { get; init; } = 0.075f;
    public float NoiseGateMultiplier { get; init; } = 1.9f;
    public float MaximumGain { get; init; } = 5.5f;
    public string WakeWordMode { get; init; } = "Balanced";
    public bool UtteranceLoggingEnabled { get; init; } = true;
    public bool IncludeTranscriptInLogs { get; init; }

    public static VoiceRuntimeOptions FromSettings(VoiceSettings? settings)
    {
        settings ??= new();
        return new()
        {
            EndOfSpeechMilliseconds = Math.Clamp(settings.EndOfSpeechMilliseconds, 250, 1800),
            MinimumSpeechMilliseconds = Math.Clamp(settings.MinimumSpeechMilliseconds, 100, 1000),
            MaximumSpeechSeconds = Math.Clamp(settings.MaximumSpeechSeconds, 5, 45),
            VadThreshold = Finite(settings.VadThreshold, 0.42f, 0.20f, 0.85f),
            GainTargetRms = Finite(settings.MicGainTargetRms, 0.075f, 0.02f, 0.20f),
            NoiseGateMultiplier = Finite(settings.NoiseGateMultiplier, 1.9f, 1f, 6f),
            MaximumGain = Finite(settings.MaximumGain, 5.5f, 1f, 12f),
            WakeWordMode = settings.WakeWordMode is "Strict" or "Forgiving" ? settings.WakeWordMode : "Balanced",
            UtteranceLoggingEnabled = settings.UtteranceLoggingEnabled,
            IncludeTranscriptInLogs = settings.IncludeTranscriptInLogs
        };
    }

    private static float Finite(double value, float fallback, float minimum, float maximum) =>
        double.IsFinite(value) ? (float)Math.Clamp(value, minimum, maximum) : fallback;
}
