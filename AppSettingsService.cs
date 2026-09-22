using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SentinelX;

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
            case "Ogólne": Settings.Startup = new(); Settings.Ui.CloseToTray = true; break;
            case "Developer": Settings.Developer = new(); Settings.Voice.SaveVoiceSamples = false; break;
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
