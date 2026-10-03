using System.IO;
using System.Text;
using System.Text.Json;

namespace SentinelX;

public sealed class AppSettingsService
{
    private const long MaximumSettingsBytes = 1024 * 1024;
    private readonly string path;
    private readonly object gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public SentinelSettings Settings { get; private set; } = new();
    public string? LastError { get; private set; }
    public string SettingsPath => path;
    public event Action? Changed;

    public AppSettingsService(string? directory = null)
    {
        path = Path.GetFullPath(Path.Combine(directory ?? Path.Combine(AppPaths.Root, "Settings"), "settings.json"));
        Load();
    }

    public void Load()
    {
        lock (gate)
        {
            LastError = null;
            if (!File.Exists(path))
            {
                if (TryRecoverBackup("Główny plik ustawień nie istnieje.")) return;
                Settings = CreateDefaults();
                if (File.Exists(path + ".backup") && LastError == null)
                    LastError = "Nie udało się przywrócić ustawień z kopii zapasowej; załadowano wartości domyślne.";
                return;
            }

            try
            {
                Settings = ReadSettingsFile(path);
                LastError = null;
            }
            catch (Exception ex) when (IsSettingsError(ex))
            {
                string primaryError = ex.Message;
                string? quarantine = PreserveCorruptPrimary();
                if (TryRecoverBackup("Nie udało się odczytać głównego pliku ustawień: " + primaryError, quarantine)) return;
                string? backupError = LastError;
                Settings = CreateDefaults();
                LastError = "Nie udało się odczytać ustawień; załadowano bezpieczne wartości domyślne. " + primaryError +
                    (quarantine == null ? " Uszkodzonego pliku nie udało się zachować jako osobnej kopii." : " Kopię zachowano jako „" + Path.GetFileName(quarantine) + "”.") +
                    (backupError == null ? "" : " " + backupError);
            }
        }
    }

    public void Save()
    {
        lock (gate)
        {
            try
            {
                Settings.Validate();
                if (!TryPersist(Settings, preserveLastGoodBackup: true, out string? error))
                {
                    LastError = "Nie udało się bezpiecznie zapisać ustawień: " + error;
                    return;
                }
                LastError = null;
            }
            catch (Exception ex) when (IsSettingsError(ex))
            {
                LastError = "Nie udało się bezpiecznie zapisać ustawień: " + ex.Message;
                return;
            }
        }
        RaiseChanged();
    }

    public void Export(string destination) => File.WriteAllText(destination, JsonSerializer.Serialize(Settings, JsonOptions));

    public bool Import(string source)
    {
        lock (gate)
        {
            SentinelSettings previous = Settings;
            try
            {
                SentinelSettings candidate = ReadSettingsFile(source);
                if (!TryPersist(candidate, preserveLastGoodBackup: true, out string? error))
                {
                    Settings = previous;
                    LastError = "Import odrzucony — nie udało się bezpiecznie zapisać ustawień: " + error;
                    return false;
                }
                Settings = candidate;
                LastError = null;
            }
            catch (Exception ex) when (IsSettingsError(ex))
            {
                Settings = previous;
                LastError = "Import odrzucony: " + ex.Message;
                return false;
            }
        }
        RaiseChanged();
        return true;
    }

    public void ResetSection(string section)
    {
        switch (section)
        {
            case "Wygląd": Settings.Ui = new(); break;
            case "Głos": Settings.Voice = new(); break;
            case "AI": Settings.Ai = new(); break;
            case "Pamięć": Settings.Memory = new(); break;
            case "Watch": Settings.Watch = new(); break;
            case "Zasoby": Settings.Resources = new(); break;
            case "Ogólne": Settings.Startup = new() { AutopilotApplied = true }; Settings.Ui.CloseToTray = true; break;
            case "Telefon": Settings.Link = new(); break;
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

    private bool TryRecoverBackup(string reason, string? quarantine = null)
    {
        string backup = path + ".backup";
        if (!File.Exists(backup)) return false;
        try
        {
            Settings = ReadSettingsFile(backup);
            string recovery = reason + " Przywrócono ostatnią poprawną kopię „" + Path.GetFileName(backup) + "”.";
            if (quarantine != null) recovery += " Uszkodzony plik zachowano jako „" + Path.GetFileName(quarantine) + "”.";
            if (TryPersist(Settings, preserveLastGoodBackup: false, out string? repairError))
                LastError = recovery;
            else
                LastError = recovery + " Nie udało się odtworzyć głównego pliku: " + repairError;
            return true;
        }
        catch (Exception ex) when (IsSettingsError(ex))
        {
            LastError = reason + " Kopia zapasowa również jest nieczytelna: " + ex.Message;
            return false;
        }
    }

    private string? PreserveCorruptPrimary()
    {
        if (!File.Exists(path)) return null;
        string stem = path + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        for (int suffix = 0; suffix < 100; suffix++)
        {
            string destination = suffix == 0 ? stem : stem + "-" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            try
            {
                File.Move(path, destination);
                return destination;
            }
            catch (IOException) when (File.Exists(destination)) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Write("Settings", "Warning", "Could not quarantine a damaged settings file.", ex);
                return null;
            }
        }
        return null;
    }

    private bool TryPersist(SentinelSettings candidate, bool preserveLastGoodBackup, out string? error)
    {
        string? temp = null;
        string? backupTemp = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            candidate.Validate();
            string json = JsonSerializer.Serialize(candidate, JsonOptions);
            byte[] bytes = new UTF8Encoding(false).GetBytes(json);
            if (bytes.LongLength > MaximumSettingsBytes)
                throw new InvalidDataException("Zapis ustawień przekracza limit 1 MB.");

            temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            _ = ReadSettingsFile(temp); // Validate the exact staged bytes before replacing the live file.

            if (preserveLastGoodBackup && File.Exists(path))
            {
                // Never replace a usable backup with a corrupt or partially written primary file.
                _ = ReadSettingsFile(path);
                string backup = path + ".backup";
                backupTemp = backup + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.Copy(path, backupTemp, false);
                using (var backupStream = new FileStream(backupTemp, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    backupStream.Flush(flushToDisk: true);
                File.Move(backupTemp, backup, true);
                backupTemp = null;
            }

            File.Move(temp, path, true);
            temp = null;
            error = null;
            return true;
        }
        catch (Exception ex) when (IsSettingsError(ex))
        {
            error = ex.Message;
            AppLog.Write("Settings", "Error", "Settings could not be committed atomically.", ex);
            return false;
        }
        finally
        {
            DeleteTemporary(temp);
            DeleteTemporary(backupTemp);
        }
    }

    private static SentinelSettings ReadSettingsFile(string source)
    {
        var info = new FileInfo(source);
        if (info.Length > MaximumSettingsBytes) throw new InvalidDataException("Plik ustawień przekracza 1 MB.");
        string json = File.ReadAllText(source);
        using JsonDocument parsed = JsonDocument.Parse(json);
        if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Ustawienia muszą być obiektem JSON.");
        var candidate = JsonSerializer.Deserialize<SentinelSettings>(MigrateLegacyJson(json), JsonOptions)
            ?? throw new InvalidDataException("Pusty plik ustawień.");
        candidate.Validate();
        return candidate;
    }

    private static SentinelSettings CreateDefaults()
    {
        var settings = new SentinelSettings();
        settings.Validate();
        return settings;
    }

    private static bool IsSettingsError(Exception ex) => ex is IOException or UnauthorizedAccessException or JsonException
        or InvalidOperationException or InvalidDataException or NotSupportedException or ArgumentException or System.Security.SecurityException;

    private static void DeleteTemporary(string? file)
    {
        if (file == null) return;
        try { if (File.Exists(file)) File.Delete(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void RaiseChanged()
    {
        foreach (Action observer in Changed?.GetInvocationList() ?? [])
        {
            try { observer(); }
            catch (Exception ex) { AppLog.Write("Settings", "Warning", "A settings-change observer failed.", ex); }
        }
    }

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
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void CopyBool(JsonElement root, Utf8JsonWriter writer, string name, bool fallback, string? outName = null) =>
        writer.WriteBoolean(outName ?? name, root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback);

    private static void CopyInt(JsonElement root, Utf8JsonWriter writer, string name, int fallback, string? outName = null) =>
        writer.WriteNumber(outName ?? name, root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : fallback);

    private static void CopyString(JsonElement root, Utf8JsonWriter writer, string name, string fallback, string? outName = null) =>
        writer.WriteString(outName ?? name, root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback);
}
