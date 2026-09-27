using System.Text;

namespace SentinelX;

/// <summary>0.97 · content search inside text files (#811 file content search). Read-only, bounded, honest about limits.</summary>
public static class FileSearch
{
    public const long MaxFileBytes = 1024 * 1024;
    private const int DefaultMaxMatches = 15;
    private const int DefaultMaxFiles = 400;
    private const int DefaultMaxDepth = 3;
    private static readonly TimeSpan DefaultTimeLimit = TimeSpan.FromSeconds(6);

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".csv", ".tsv", ".json", ".jsonl", ".log", ".ini", ".cfg", ".conf", ".xml", ".yml", ".yaml",
        ".cs", ".py", ".js", ".ts", ".html", ".css", ".sql", ".xaml", ".ps1", ".bat", ".sh", ".rs", ".go", ".java"
    };

    private static readonly HashSet<string> SkipDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", ".svn", "AppData", "$Recycle.Bin", "System Volume Information",
        "Windows", "Windows.old", "Program Files", "Program Files (x86)", "ProgramData",
        "bin", "obj", ".cache", ".nuget", ".venv", "__pycache__", "dist", "build", ".idea", ".vs"
    };

    public sealed record Hit(string Path, int LineNumber, string Line);

    public sealed record Result(IReadOnlyList<Hit> Hits, int SearchedFiles, int SearchedDirectories, bool Truncated, bool TimedOut, bool AccessDenied)
    {
        public string Notes => (TimedOut ? " • przerwałem po limicie czasu" : "") + (Truncated ? " • pokazuję tylko pierwsze trafienia" : "") +
            (AccessDenied ? " • część folderów była niedostępna" : "");
    }

    /// <summary>Szuka frazy w treści plików tekstowych. Nigdy nie modyfikuje plików i nie wchodzi w foldery systemowe.</summary>
    public static Result Search(IEnumerable<string> roots, string phrase, bool ignoreCase = true,
        int maxMatches = DefaultMaxMatches, int maxFiles = DefaultMaxFiles, int maxDepth = DefaultMaxDepth,
        TimeSpan? timeLimit = null, string? extensionFilter = null)
    {
        string needle = (phrase ?? "").Trim();
        var hits = new List<Hit>();
        if (needle.Length == 0) return new Result(hits, 0, 0, false, false, false);

        DateTime deadline = DateTime.UtcNow + (timeLimit ?? DefaultTimeLimit);
        int searchedFiles = 0, searchedDirectories = 0;
        bool truncated = false, timedOut = false, accessDenied = false;
        var queue = new Queue<(string Directory, int Depth)>();
        foreach (string root in roots.Where(r => r.Length > 0))
            if (Directory.Exists(root)) queue.Enqueue((root, 0));

        while (queue.Count > 0 && hits.Count < maxMatches && searchedFiles < maxFiles)
        {
            if (DateTime.UtcNow > deadline) { timedOut = true; break; }
            (string directory, int depth) = queue.Dequeue();
            searchedDirectories++;
            try
            {
                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    if (searchedFiles >= maxFiles || hits.Count >= maxMatches || DateTime.UtcNow > deadline) break;
                    string extension = Path.GetExtension(file);
                    if (extension.Length == 0 || !TextExtensions.Contains(extension)) continue;
                    if (extensionFilter != null && !extension.Equals(extensionFilter, StringComparison.OrdinalIgnoreCase)) continue;
                    FileInfo info;
                    try { info = new FileInfo(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { accessDenied = true; continue; }
                    if (!info.Exists || info.Length > MaxFileBytes) continue;
                    searchedFiles++;
                    try
                    {
                        int number = 0;
                        foreach (string line in File.ReadLines(file))
                        {
                            number++;
                            if (number > 4000) break;
                            if (!line.Contains(needle, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) continue;
                            hits.Add(new Hit(file, number, line.Length > 160 ? line[..160] + "…" : line));
                            break; // one hit per file keeps the report readable
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { accessDenied = true; }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException) { accessDenied = true; }

            if (depth >= maxDepth) continue;
            try
            {
                foreach (string child in Directory.EnumerateDirectories(directory))
                {
                    string name = Path.GetFileName(child);
                    if (name.Length == 0 || SkipDirectories.Contains(name)) continue;
                    queue.Enqueue((child, depth + 1));
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException) { accessDenied = true; }
        }

        if (hits.Count >= maxMatches) truncated = true;
        return new Result(hits, searchedFiles, searchedDirectories, truncated, timedOut, accessDenied);
    }

    public static string Describe(IEnumerable<string> roots, string phrase, bool ignoreCase = true)
    {
        var rootList = roots.ToList();
        Result result = Search(rootList, phrase, ignoreCase);
        var builder = new StringBuilder();
        if (result.Hits.Count == 0)
        {
            builder.AppendLine("Nie znalazłem „" + phrase + "” w treści plików (przejrzane pliki: " + result.SearchedFiles +
                ", katalogi: " + result.SearchedDirectories + result.Notes + ").");
            builder.AppendLine("Szukam tylko w Pulpicie, Dokumentach i Pobranych, tylko w plikach tekstowych do 1 MB i do głębokości 3.");
            return builder.ToString().TrimEnd();
        }
        builder.AppendLine("Znalazłem „" + phrase + "” w " + result.Hits.Count + " plikach (przejrzane: " + result.SearchedFiles + " plików, " +
            result.SearchedDirectories + " katalogów" + result.Notes + "):");
        foreach ((Hit hit, int index) in result.Hits.Select((x, i) => (x, i)))
            builder.AppendLine($"{index + 1}. {hit.Path} (linia {hit.LineNumber}): {hit.Line.Trim()}");
        builder.AppendLine("Pliki zostały tylko odczytane — niczego w nich nie zmieniłem.");
        return builder.ToString().TrimEnd();
    }
}
