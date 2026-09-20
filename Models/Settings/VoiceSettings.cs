using System.Text.Json.Serialization;

namespace SentinelX;

public sealed class VoiceSettings
{
    public int SelectedMicrophoneDevice { get; set; }
    public string SelectedMicrophoneName { get; set; } = "";
    public bool SpeakResponses { get; set; } = true;
    public string SpeechVoice { get; set; } = "";
    public int SpeechRate { get; set; }
    public int SpeechVolume { get; set; } = 80;
    public int ConversationTimeoutMinutes { get; set; } = 2;
    public string WakeWordMode { get; set; } = "Balanced";
    public double MicGainTargetRms { get; set; } = 0.075;
    public double NoiseGateMultiplier { get; set; } = 1.9;
    public bool SaveVoiceSamples { get; set; }
    public int EndOfSpeechMilliseconds { get; set; } = 650;
    public int MinimumSpeechMilliseconds { get; set; } = 200;
    public int MaximumSpeechSeconds { get; set; } = 20;
    public double VadThreshold { get; set; } = .42;
    public double MaximumGain { get; set; } = 5.5;
    public bool UtteranceLoggingEnabled { get; set; } = true;
    public bool IncludeTranscriptInLogs { get; set; }
}

