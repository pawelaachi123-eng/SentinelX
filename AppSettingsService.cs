using System.IO;
using System.Text.Json;
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
        Ui.Theme = ValidateChoice(Ui.Theme, ["Dark", "Deep Dark", "System"], "Dark");
        Ui.DefaultBrowserPreference = ValidateChoice(Ui.DefaultBrowserPreference, ["Brave", "Chrome", "System"], "Brave");
        Ui.AccentColor = System.Text.RegularExpressions.Regex.IsMatch(Ui.AccentColor ?? "", "^#[0-9a-fA-F]{6}$") ? Ui.AccentColor!.Trim() : "#66F2C2";
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

public sealed class UiSettings
{
    public bool CloseToTray { get; set; } = true;
    public bool MinimizeToTray { get; set; }
    public string Theme { get; set; } = "Dark";
    public string DefaultBrowserPreference { get; set; } = "Brave";
    public string AccentColor { get; set; } = "#66F2C2";
    public int OverlayOpacityPercent { get; set; } = 85;
    public int OverlayScalePercent { get; set; } = 100;
    public string OverlayPosition { get; set; } = "Lewy górny";
    public double WindowWidth { get; set; } = 1260;
    public double WindowHeight { get; set; } = 860;
    public string SelectedPage { get; set; } = "Chat";
}

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

public sealed class ResourceSettings
{
    public int MonitorIntervalSeconds { get; set; } = 1;
    public int GamingMonitorIntervalSeconds { get; set; } = 3;
}

public sealed class AiSettings
{
    public string GamingModel { get; set; } = "qwen3:1.7b";
    public string IdleModel { get; set; } = "qwen3:4b-instruct";
    public string FallbackModel { get; set; } = "gemma3:4b";
    public double Temperature { get; set; } = 0.22;
    public int MaxContextTokens { get; set; } = 4096;
    public int MaxResponseTokens { get; set; } = 700;
    public int RamPressurePercent { get; set; } = 78;
    public int CpuPressurePercent { get; set; } = 85;
    public int GpuPressurePercent { get; set; } = 70;
}

public sealed class WatchSettings
{
    public bool Enabled { get; set; }
    public int CpuAlertPercent { get; set; } = 90;
    public int RamAlertPercent { get; set; } = 90;
    public int MinSecondsBeforeAlert { get; set; } = 15;
    public int CooldownMinutes { get; set; } = 5;
}

public sealed class StartupSettings
{
    public bool StartMinimized { get; set; }
    public bool StartWithWindows { get; set; }
    public bool StartVoiceOnLaunch { get; set; }
}

public sealed class DeveloperSettings
{
    public bool DeveloperMode { get; set; }
    public bool SaveVoiceSamples { get; set; }
}

public sealed class AppSettingsService
{
    private readonly string path;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public SentinelSettings Settings { get; private set; } = new();
    public string? LastError { get; private set; }
    public string SettingsPath => path;
    public event Action? Changed;

    public AppSettingsService(string? directory = null)
    {
        path = Path.Combine(directory ?? Path.Combine(AppPaths.Root, "Settings"), "settings.json");
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                Settings = JsonSerializer.Deserialize<SentinelSettings>(MigrateLegacyJson(json), JsonOptions) ?? new();
            }
            Settings.Validate();
            LastError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or InvalidDataException)
        {
            Settings = new();
            Settings.Validate();
            LastError = "Nie udało się odczytać ustawień: " + ex.Message;
        }
    }

    public void Save()
    {
        try
        {
            Settings.Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Settings, JsonOptions));
            if (File.Exists(path)) File.Copy(path, path + ".backup", true);
            File.Move(temp, path, true);
            LastError = null;
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastError = "Nie udało się zapisać ustawień: " + ex.Message;
        }
    }

    public void Export(string destination) => File.WriteAllText(destination, JsonSerializer.Serialize(Settings, JsonOptions));

    public bool Import(string source)
    {
        SentinelSettings previous = Settings;
        try
        {
            if (new FileInfo(source).Length > 1024 * 1024) throw new InvalidDataException("Plik ustawień przekracza 1 MB.");
            string json = File.ReadAllText(source);
            using var parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Ustawienia muszą być obiektem JSON.");
            var candidate = JsonSerializer.Deserialize<SentinelSettings>(MigrateLegacyJson(json), JsonOptions) ?? throw new InvalidDataException("Puste ustawienia.");
            candidate.Validate();
            Settings = candidate;
            Save();
            if (LastError != null) { Settings = previous; return false; }
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { Settings = previous; LastError = "Import odrzucony: " + ex.Message; return false; }
    }

    public void ResetSection(string section)
    {
        switch (section)
        {
            case "Wygląd": Settings.Ui = new(); break;
            case "Głos": Settings.Voice = new(); break;
            case "AI": Settings.Ai = new(); break;
            case "Watch": Settings.Watch = new(); break;
            case "Zasoby": Settings.Resources = new(); break;
        }
        Save();
    }

    public string Summary() =>
        $"""
        USTAWIENIA SENTINEL
        Plik: {path}

        UI: motyw {Settings.Ui.Theme}, accent {Settings.Ui.AccentColor}, tray {(Settings.Ui.CloseToTray ? "ON" : "OFF")}
        Voice: timeout {Settings.Voice.ConversationTimeoutMinutes} min, TTS {Settings.Voice.SpeechVolume}%, rate {Settings.Voice.SpeechRate}, wake {Settings.Voice.WakeWordMode}
        AI: gra/obciążenie {Settings.Ai.GamingModel}, luz {Settings.Ai.IdleModel}, fallback {Settings.Ai.FallbackModel}
        Watch: {(Settings.Watch.Enabled ? "ON" : "OFF")}, CPU {Settings.Watch.CpuAlertPercent}%, RAM {Settings.Watch.RamAlertPercent}%
        Developer: {(Settings.Developer.DeveloperMode ? "ON" : "OFF")}
        """;

    private static string MigrateLegacyJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Ustawienia muszą być obiektem JSON.");
        if (document.RootElement.EnumerateObject().Any(x => new[] { "Voice", "Ui", "Ai", "Watch", "Startup", "Developer", "Resources" }.Contains(x.Name, StringComparer.OrdinalIgnoreCase)))
            return json;

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("Ui");
            writer.WriteStartObject();
            CopyBool(document.RootElement, writer, "CloseToTray", true);
            CopyBool(document.RootElement, writer, "MinimizeToTray", false);
            writer.WriteEndObject();

            writer.WritePropertyName("Startup");
            writer.WriteStartObject();
            CopyBool(document.RootElement, writer, "StartMinimized", false);
            CopyBool(document.RootElement, writer, "StartWithWindows", false);
            CopyBool(document.RootElement, writer, "StartVoiceOnLaunch", false);
            writer.WriteEndObject();

            writer.WritePropertyName("Voice");
            writer.WriteStartObject();
            CopyInt(document.RootElement, writer, "SelectedMicrophoneDevice", 0);
            CopyString(document.RootElement, writer, "SelectedMicrophoneName", "");
            CopyBool(document.RootElement, writer, "SpeakResponses", true);
            CopyString(document.RootElement, writer, "SpeechVoice", "");
            CopyInt(document.RootElement, writer, "SpeechRate", 0);
            CopyInt(document.RootElement, writer, "SpeechVolume", 80);
            CopyInt(document.RootElement, writer, "ConversationMinutes", 2, "ConversationTimeoutMinutes");
            writer.WriteEndObject();

            writer.WritePropertyName("Watch");
            writer.WriteStartObject();
            CopyBool(document.RootElement, writer, "WatchEnabled", false, "Enabled");
            writer.WriteEndObject();

            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void CopyBool(JsonElement root, Utf8JsonWriter writer, string name, bool fallback, string? outName = null) =>
        writer.WriteBoolean(outName ?? name, root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback);

    private static void CopyInt(JsonElement root, Utf8JsonWriter writer, string name, int fallback, string? outName = null) =>
        writer.WriteNumber(outName ?? name, root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : fallback);

    private static void CopyString(JsonElement root, Utf8JsonWriter writer, string name, string fallback, string? outName = null) =>
        writer.WriteString(outName ?? name, root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback);
}
