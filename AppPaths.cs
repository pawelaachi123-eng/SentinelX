using System.IO;
using System.Text;

namespace SentinelX;
public static class AppPaths
{
    public static string Root { get; } = Resolve();
    private static string Resolve()
    {
        string? custom = Environment.GetEnvironmentVariable("SENTINEL_DATA_DIR");
        return !string.IsNullOrWhiteSpace(custom) && Path.IsPathFullyQualified(custom) ? Path.GetFullPath(custom) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SentinelX");
    }
}
public static class AppLog
{
    private static readonly object Gate = new();
    public static void Write(Exception exception)
    {
        try { lock (Gate) { string directory = Path.Combine(AppPaths.Root, "Logs"); Directory.CreateDirectory(directory); string path = Path.Combine(directory, "errors.log"); if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.Move(path, path + ".previous", true); File.AppendAllText(path, $"{DateTimeOffset.Now:O} {exception}\n"); } } catch { }
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
