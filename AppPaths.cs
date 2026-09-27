using System.IO;
using System.Text;

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
        return !string.IsNullOrWhiteSpace(custom) && Path.IsPathFullyQualified(custom) ? Path.GetFullPath(custom) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SentinelX");
    }
}
/// <summary>Dziennik zdarzeń Sentinela. 0.97: każdy wpis trafia też do strukturyzowanego
/// dziennika JSON (JsonLog.cs) z rotacją, a sam plik errors.log rotuje przez 3 starsze kopie
/// zamiast jednej. Nic nie jest wysyłane — to lokalne pliki obok danych aplikacji.</summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private const long MaxBytes = 1024 * 1024;
    private const int KeepRotated = 3;

    public static void Write(Exception exception)
    {
        WriteLine("error", exception.ToString());
        JsonLog.Append("error", exception.Message, exception.GetType().Name);
    }

    /// <summary>Wpis z kategorią (np. „harmonogram”, „schowek”) — czytelny w dzienniku JSON.</summary>
    public static void Write(string category, string message)
    {
        WriteLine(category, message);
        JsonLog.Append(category, message, null);
    }

    private static void WriteLine(string category, string message)
    {
        try
        {
            lock (Gate)
            {
                string directory = Path.Combine(AppPaths.Root, "Logs");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "errors.log");
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes) Rotate(path);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} [{category}] {message}\n");
            }
        }
        catch { }
    }

    /// <summary>Rotacja: errors.log → .1 → .2 → .3 (najstarsza znika). Pliki są lokalne i małe.</summary>
    private static void Rotate(string path)
    {
        for (int index = KeepRotated; index >= 1; index--)
        {
            string from = index == 1 ? path : path + "." + (index - 1);
            string to = path + "." + index;
            try
            {
                if (File.Exists(from)) File.Copy(from, to, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        try { File.WriteAllText(path, ""); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
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
            return readBack == content ? $"Utworzono plik i sprawdzono treść:\n{path}" : "Treść pliku nie zgadza się z żądaniem. Sprawdź plik: " + path;
        }
        catch (IOException) when (File.Exists(path)) { return "Plik już istnieje lub zapis nie został ukończony. Wybierz inną nazwę: " + path; }
    }
}
