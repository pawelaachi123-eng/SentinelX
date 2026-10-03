using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SentinelX;

public static class AppPaths
{
    public static string Root { get; } = Resolve();
    public static string RootDirectory => Root;
    public static string SettingsDirectory => Path.Combine(Root, "Settings");
    public static string LogsDirectory => Path.Combine(Root, "Logs");
    public static string HistoryDirectory => Path.Combine(Root, "History");
    public static string MemoryDirectory => Path.Combine(Root, "Memory");
    public static string BackupsDirectory => Path.Combine(Root, "Backups");
    public static string CacheDirectory => Path.Combine(Root, "Cache");

    private static string Resolve()
    {
        string? custom = Environment.GetEnvironmentVariable("SENTINEL_DATA_DIR");
        try
        {
            if (!string.IsNullOrWhiteSpace(custom) && Path.IsPathFullyQualified(custom))
                return Path.GetFullPath(custom);
        }
        catch (ArgumentException) { /* Ignore an invalid optional data-directory override. */ }
        catch (NotSupportedException) { /* Ignore an invalid optional data-directory override. */ }
        catch (PathTooLongException) { /* Ignore an invalid optional data-directory override. */ }
        catch (IOException) { /* Ignore an invalid optional data-directory override. */ }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SentinelX");
    }
}

/// <summary>Small structured JSONL logger with redaction and bounded disk retention.</summary>
public static class AppLog
{
    private const long MaxLogBytes = 2 * 1024 * 1024;
    private const int RetainedArchives = 4;
    private static readonly object Gate = new();
    private static readonly Regex LabeledSecret = new(
        """(?i)(\b(?:authorization|access[_-]?token|refresh[_-]?token|id[_-]?token|client[_-]?secret|private[_-]?key|token|api[_-]?key|password|passwd|credential|secret)\b["']?\s*[:=]\s*)(?:"[^"]*"|'[^']*'|[^\s,;"']+)""",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex BearerSecret = new(@"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex BasicSecret = new(@"(?i)\bBasic\s+[A-Za-z0-9+/=]+", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex QuerySecret = new(@"(?i)([?&](?:access_token|refresh_token|id_token|client_secret|token|api_key|key|password|secret)=)[^&#\s]+", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static string LogPath => Path.Combine(AppPaths.LogsDirectory, "sentinel.jsonl");

    /// <summary>Compatibility overload used by older call sites.</summary>
    public static void Write(Exception exception) => Write("App", "Error", "An unexpected operation failed.", exception);

    public static void Write(string category, string severity, string message, Exception? exception = null)
    {
        try
        {
            lock (Gate)
            {
                string path = LogPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var record = new
                {
                    Timestamp = DateTimeOffset.Now,
                    Severity = NormalizeSeverity(severity),
                    Category = NormalizeCategory(category),
                    Message = Clip(Redact(message), 4000),
                    ExceptionType = exception?.GetType().FullName,
                    Details = exception == null ? null : Clip(Redact(exception.ToString()), 12000)
                };
                string line = JsonSerializer.Serialize(record) + Environment.NewLine;
                byte[] bytes = Utf8NoBom.GetBytes(line);
                RotateIfNeeded(path, bytes.Length);
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.SequentialScan);
                stream.Write(bytes);
            }
        }
        catch { /* Diagnostics must never crash the application. */ }
    }

    private static void RotateIfNeeded(string path, int incomingBytes)
    {
        if (!File.Exists(path) || new FileInfo(path).Length + incomingBytes <= MaxLogBytes) return;
        string oldest = path + "." + RetainedArchives;
        if (File.Exists(oldest)) File.Delete(oldest);
        for (int index = RetainedArchives - 1; index >= 1; index--)
        {
            string source = path + "." + index;
            if (File.Exists(source)) File.Move(source, path + "." + (index + 1), true);
        }
        File.Move(path, path + ".1", true);
    }

    private static string NormalizeCategory(string? category)
    {
        string value = (category ?? "").Trim();
        return value.ToLowerInvariant() switch
        {
            "ai" => "AI",
            "voice" => "Voice",
            "automation" => "Automation",
            "devices" => "Devices",
            "network" => "Network",
            "files" => "Files",
            "system" or "monitoring" => "System",
            "ui" => "UI",
            "settings" => "Settings",
            "startup" => "Startup",
            "security" => "Security",
            "performance" or "gaming" or "overlay" => "Performance",
            "diagnostics" => "Diagnostics",
            "engine" => "Engine",
            "memory" => "Memory",
            "history" => "History",
            "desktop" => "Desktop",
            "home" => "Home",
            _ => "App"
        };
    }

    private static string NormalizeSeverity(string? severity) => severity?.Trim().ToLowerInvariant() switch
    {
        "trace" => "Trace",
        "debug" => "Debug",
        "info" or "information" => "Information",
        "warn" or "warning" => "Warning",
        "critical" => "Critical",
        _ => "Error"
    };

    private static string Redact(string? text)
    {
        string value = text ?? "";
        value = BearerSecret.Replace(value, "Bearer [REDACTED]");
        value = BasicSecret.Replace(value, "Basic [REDACTED]");
        value = LabeledSecret.Replace(value, "$1[REDACTED]");
        value = QuerySecret.Replace(value, "$1[REDACTED]");
        return value;
    }

    private static string Clip(string value, int maximum) => value.Length <= maximum ? value : value[..maximum] + "…[truncated]";
}

public static class LocalFileService
{
    public static async Task<string> CreateAsync(string name, string content, CancellationToken token = default)
    {
        name = name.Trim().Trim('"');
        string stem = name.Split('.')[0].ToUpperInvariant();
        if (name.Length == 0 || name.Length > 150 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name.EndsWith(' ') || name is "." or ".." || stem is "CON" or "PRN" or "AUX" or "NUL" || System.Text.RegularExpressions.Regex.IsMatch(stem, @"^(COM|LPT)[0-9]$"))
            return "Podaj zwykłą nazwę pliku bez ścieżki, np. notatka.txt.";
        if (content.Length > 1000000) return "Treść przekracza limit 1 miliona znaków.";
        string directory = Path.Combine(AppPaths.Root, "CreatedFiles"); Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        try
        {
            await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
            { byte[] bytes = Encoding.UTF8.GetBytes(content); await stream.WriteAsync(bytes, token); await stream.FlushAsync(token); }
            string readBack = await File.ReadAllTextAsync(path, token);
            return readBack == content ? $"Utworzono plik i sprawdzono treść:\n{path}" : "Treść pliku nie zgadza się z odczytem kontrolnym. Sprawdź plik: " + path;
        }
        catch (IOException) when (File.Exists(path)) { return "Plik już istnieje lub zapis nie został ukończony. Wybierz inną nazwę: " + path; }
    }
}
