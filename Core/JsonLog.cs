using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SentinelX;

/// <summary>0.97 · structured JSON-lines log with size rotation (#010). Local file only; nothing is uploaded.</summary>
public static class JsonLog
{
    private const long MaxBytes = 512 * 1024;
    private const int KeepRotated = 3;
    private static readonly object Gate = new();

    /// <summary>Ścieżka dziennika. Testy mogą ją nadpisać (OverridePath), żeby nie dotykać katalogu użytkownika.</summary>
    public static string Path => OverridePath.Length > 0 ? OverridePath : System.IO.Path.Combine(AppPaths.LogsDirectory, "sentinel.jsonl");

    /// <summary>Tylko dla testów: pusty ciąg oznacza prawdziwy katalog Logs obok danych aplikacji.</summary>
    public static string OverridePath { get; set; } = "";

    /// <summary>Dopisuje jeden rekord JSON. Nigdy nie rzuca — logowanie nie może zepsuć komendy.</summary>
    public static void Write(string category, string message, string? detail = null) => Append(category, message, detail);

    /// <summary>Wewnętrzny zapis bez odbicia do pliku errors.log (AppLog dba o oba dzienniki sam).</summary>
    internal static void Append(string category, string message, string? detail = null)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogsDirectory);
            string line = JsonSerializer.Serialize(new
            {
                ts = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
                category,
                message,
                detail
            }) + Environment.NewLine;
            lock (Gate)
            {
                File.AppendAllText(Path, line, Encoding.UTF8);
                Rotate();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // The journal is the last resort: when even it fails, there is nothing left to write to.
            _ = ex;
        }
    }

    public static IReadOnlyList<string> Recent(int count = 20)
    {
        try
        {
            if (!File.Exists(Path)) return [];
            var lines = File.ReadAllLines(Path);
            return lines.Skip(Math.Max(0, lines.Length - Math.Max(1, count))).Reverse().ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    public static string Describe(int count = 20)
    {
        IReadOnlyList<string> lines = Recent(count);
        var builder = new StringBuilder();
        builder.AppendLine("DZIENNIK ZDARZEŃ · " + Path);
        if (lines.Count == 0)
        {
            builder.AppendLine("Dziennik jest pusty — nic jeszcze nie zapisałem w tej sesji.");
            return builder.ToString().TrimEnd();
        }
        foreach (string line in lines)
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                string time = root.TryGetProperty("ts", out JsonElement ts) ? ts.GetString() ?? "" : "";
                if (time.Length > 19) time = time[..19].Replace('T', ' ');
                string category = root.TryGetProperty("category", out JsonElement c) ? c.GetString() ?? "" : "";
                string message = root.TryGetProperty("message", out JsonElement m) ? m.GetString() ?? "" : "";
                builder.AppendLine("· " + time + " [" + category + "] " + message);
            }
            catch (JsonException) { builder.AppendLine("· " + line); }
        }
        builder.AppendLine("Dziennik leży na Twoim dysku i nie jest nigdzie wysyłany. Rotacja: po 512 KB, trzymam 3 starsze kopie.");
        return builder.ToString().TrimEnd();
    }

    private static void Rotate()
    {
        try
        {
            var info = new FileInfo(Path);
            if (!info.Exists || info.Length <= MaxBytes) return;
            for (int i = KeepRotated; i >= 1; i--)
            {
                string from = i == 1 ? Path : Path + "." + (i - 1);
                string to = Path + "." + i;
                if (File.Exists(from)) File.Copy(from, to, true);
            }
            File.WriteAllText(Path, "", new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
