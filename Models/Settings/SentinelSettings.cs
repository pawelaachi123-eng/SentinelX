using System.Text.Json.Serialization;

namespace SentinelX;

public sealed class SentinelSettings
{
    public UiSettings Ui { get; set; } = new();
    public VoiceSettings Voice { get; set; } = new();
    public AiSettings Ai { get; set; } = new();
    public WatchSettings Watch { get; set; } = new();
    public StartupSettings Startup { get; set; } = new();
    public DeveloperSettings Developer { get; set; } = new();
    public ResourceSettings Resources { get; set; } = new();
    public MemorySettings Memory { get; set; } = new();
    public CollaborationSettings Collaboration { get; set; } = new();

    // Backward-compatible properties for existing code and old settings.json files.
    [JsonIgnore] public bool CloseToTray { get => Ui.CloseToTray; set => Ui.CloseToTray = value; }
    [JsonIgnore] public bool MinimizeToTray { get => Ui.MinimizeToTray; set => Ui.MinimizeToTray = value; }
    [JsonIgnore] public bool StartMinimized { get => Startup.StartMinimized; set => Startup.StartMinimized = value; }
    [JsonIgnore] public bool StartWithWindows { get => Startup.StartWithWindows; set => Startup.StartWithWindows = value; }
    [JsonIgnore] public bool StartVoiceOnLaunch { get => Startup.StartVoiceOnLaunch; set => Startup.StartVoiceOnLaunch = value; }
    [JsonIgnore] public int SelectedMicrophoneDevice { get => Voice.SelectedMicrophoneDevice; set => Voice.SelectedMicrophoneDevice = value; }
    [JsonIgnore] public string SelectedMicrophoneName { get => Voice.SelectedMicrophoneName; set => Voice.SelectedMicrophoneName = value; }
    [JsonIgnore] public bool SpeakResponses { get => Voice.SpeakResponses; set => Voice.SpeakResponses = value; }
    [JsonIgnore] public string SpeechVoice { get => Voice.SpeechVoice; set => Voice.SpeechVoice = value; }
    [JsonIgnore] public int SpeechRate { get => Voice.SpeechRate; set => Voice.SpeechRate = value; }
    [JsonIgnore] public int SpeechVolume { get => Voice.SpeechVolume; set => Voice.SpeechVolume = value; }
    [JsonIgnore] public bool WatchEnabled { get => Watch.Enabled; set => Watch.Enabled = value; }
    [JsonIgnore] public int ConversationMinutes { get => Voice.ConversationTimeoutMinutes; set => Voice.ConversationTimeoutMinutes = value; }

    // Legacy JSON properties. They are write-only during deserialization.
    public bool? LegacyCloseToTray { set { if (value.HasValue) CloseToTray = value.Value; } }
    public bool? LegacyMinimizeToTray { set { if (value.HasValue) MinimizeToTray = value.Value; } }
    public bool? LegacyStartMinimized { set { if (value.HasValue) StartMinimized = value.Value; } }
    public bool? LegacyStartWithWindows { set { if (value.HasValue) StartWithWindows = value.Value; } }
    public bool? LegacyStartVoiceOnLaunch { set { if (value.HasValue) StartVoiceOnLaunch = value.Value; } }
    public int? LegacySelectedMicrophoneDevice { set { if (value.HasValue) SelectedMicrophoneDevice = value.Value; } }
    public string? LegacySelectedMicrophoneName { set { if (value != null) SelectedMicrophoneName = value; } }
    public bool? LegacySpeakResponses { set { if (value.HasValue) SpeakResponses = value.Value; } }
    public string? LegacySpeechVoice { set { if (value != null) SpeechVoice = value; } }
    public int? LegacySpeechRate { set { if (value.HasValue) SpeechRate = value.Value; } }
    public int? LegacySpeechVolume { set { if (value.HasValue) SpeechVolume = value.Value; } }
    public bool? LegacyWatchEnabled { set { if (value.HasValue) WatchEnabled = value.Value; } }
    public int? LegacyConversationMinutes { set { if (value.HasValue) ConversationMinutes = value.Value; } }

    public void Validate()
    {
        Ui ??= new();
        Voice ??= new();
        Ai ??= new();
        Watch ??= new();
        Startup ??= new();
        Developer ??= new();
        Resources ??= new();
        Memory ??= new();
        Collaboration ??= new();
        Memory.RetentionDays = Math.Clamp(Memory.RetentionDays, 0, 3650);
        Memory.ArchiveMonths = Math.Clamp(Memory.ArchiveMonths, 0, 120);
        Ui.Theme = ValidateChoice(Ui.Theme, ["Dark", "Deep Dark", "System"], "Dark");
        Ui.DefaultBrowserPreference = ValidateChoice(Ui.DefaultBrowserPreference, ["Brave", "Chrome", "System"], "Brave");
        Ui.AccentColor = System.Text.RegularExpressions.Regex.IsMatch(Ui.AccentColor ?? "", "^#[0-9a-fA-F]{6}$") ? Ui.AccentColor!.Trim() : "#00D4FF";
        Ui.OverlayOpacityPercent = Math.Clamp(Ui.OverlayOpacityPercent, 20, 100);
        Ui.OverlayScalePercent = Math.Clamp(Ui.OverlayScalePercent, 60, 180);
        Voice.SelectedMicrophoneDevice = Math.Clamp(Voice.SelectedMicrophoneDevice, 0, 64);
        Voice.SpeechRate = Math.Clamp(Voice.SpeechRate, -5, 5);
        Voice.SpeechVolume = Math.Clamp(Voice.SpeechVolume, 0, 100);
        Voice.ConversationTimeoutMinutes = Math.Clamp(Voice.ConversationTimeoutMinutes, 1, 60);
        Voice.WakeWordMode = ValidateChoice(Voice.WakeWordMode, ["Strict", "Balanced", "Forgiving"], "Balanced");
        Voice.SaveVoiceSamples = Developer.SaveVoiceSamples && Voice.SaveVoiceSamples;
        Voice.MicGainTargetRms = Math.Clamp(Voice.MicGainTargetRms, 0.02, 0.20);
        Voice.NoiseGateMultiplier = Math.Clamp(Voice.NoiseGateMultiplier, 1.0, 6.0);
        Voice.EndOfSpeechMilliseconds = Math.Clamp(Voice.EndOfSpeechMilliseconds, 250, 1800);
        Voice.MinimumSpeechMilliseconds = Math.Clamp(Voice.MinimumSpeechMilliseconds, 100, 1000);
        Voice.MaximumSpeechSeconds = Math.Clamp(Voice.MaximumSpeechSeconds, 5, 45);
        Voice.VadThreshold = double.IsFinite(Voice.VadThreshold) ? Math.Clamp(Voice.VadThreshold, .20, .85) : .42;
        Voice.MaximumGain = double.IsFinite(Voice.MaximumGain) ? Math.Clamp(Voice.MaximumGain, 1, 12) : 5.5;
        Resources.MonitorIntervalSeconds = Math.Clamp(Resources.MonitorIntervalSeconds, 1, 10);
        Resources.GamingMonitorIntervalSeconds = Math.Clamp(Resources.GamingMonitorIntervalSeconds, 2, 15);
        Ui.OverlayPosition = ValidateChoice(Ui.OverlayPosition, ["Lewy górny", "Prawy górny", "Lewy dolny", "Prawy dolny"], "Lewy górny");
        Ui.WindowWidth = double.IsFinite(Ui.WindowWidth) ? Math.Clamp(Ui.WindowWidth, 1000, 3840) : 1260;
        Ui.WindowHeight = double.IsFinite(Ui.WindowHeight) ? Math.Clamp(Ui.WindowHeight, 720, 2160) : 860;
        Ai.GamingModel = SanitizeModel(Ai.GamingModel, "qwen3:1.7b");
        Ai.IdleModel = SanitizeModel(Ai.IdleModel, "qwen3:4b-instruct");
        Ai.FallbackModel = SanitizeModel(Ai.FallbackModel, "gemma3:4b");
        Ai.Temperature = Math.Clamp(Ai.Temperature, 0.0, 1.5);
        Ai.MaxContextTokens = Math.Clamp(Ai.MaxContextTokens, 1024, 32768);
        Ai.MaxResponseTokens = Math.Clamp(Ai.MaxResponseTokens, 128, 4096);
        Ai.RamPressurePercent = Math.Clamp(Ai.RamPressurePercent, 50, 98);
        Ai.CpuPressurePercent = Math.Clamp(Ai.CpuPressurePercent, 50, 99);
        Ai.GpuPressurePercent = Math.Clamp(Ai.GpuPressurePercent, 30, 99);
        Watch.CpuAlertPercent = Math.Clamp(Watch.CpuAlertPercent, 50, 100);
        Watch.RamAlertPercent = Math.Clamp(Watch.RamAlertPercent, 50, 100);
        Watch.MinSecondsBeforeAlert = Math.Clamp(Watch.MinSecondsBeforeAlert, 3, 300);
        Watch.CooldownMinutes = Math.Clamp(Watch.CooldownMinutes, 1, 120);
    }

    private static string ValidateChoice(string value, string[] allowed, string fallback) =>
        allowed.FirstOrDefault(x => x.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? fallback;

    private static string SanitizeModel(string value, string fallback) =>
        LocalAiService.IsLocalModelName(value) ? value.Trim() : fallback;
}

