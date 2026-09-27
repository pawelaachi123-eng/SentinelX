using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SentinelX.Core;

/// <summary>Wynik szukania plików: ścieżki, liczba przejrzanych katalogów i uczciwe flagi
/// ograniczenia (ucięcie wyniku, katalogi bez dostępu, przekroczony czas).</summary>
public sealed record FileFinderResult(IReadOnlyList<string> Paths, int SearchedDirectories, bool Truncated, bool AccessDenied, bool TimedOut)
{
    /// <summary>Czytelny dopisek o ograniczeniach — żaden wynik nie udaje pełnego skanu dysku.</summary>
    public string Notes
    {
        get
        {
            var notes = new List<string>();
            if (Truncated) notes.Add("pokaż pierwsze wyniki — zawęź nazwę, aby zobaczyć więcej");
            if (AccessDenied) notes.Add("część katalogów pominąłem (brak dostępu)");
            if (TimedOut) notes.Add("przerwałem po limicie czasu — to nie jest pełny skan");
            return notes.Count == 0 ? "" : " · " + string.Join(" · ", notes);
        }
    }
}

/// <summary>0.96 · „znajdź plik: raport” — szukanie po fragmencie nazwy w folderach użytkownika.
/// <para>Bezpieczne z zasady: tylko odczyt, twarde limity (głębokość, liczba katalogów, czas,
/// wyniki), katalogi systemowe i techniczne z góry pomijane. Sentinel nie skanuje całych dysków
/// i nie wchodzi w katalogi, do których nie ma dostępu — mówi o tym wprost.</para></summary>
public static class FileFinder
{
    private const int DefaultMaxResults = 20;
    private const int DefaultMaxDepth = 4;
    private const int DefaultMaxDirectories = 3000;
    private static readonly TimeSpan DefaultTimeLimit = TimeSpan.FromSeconds(8);

    private static readonly HashSet<string> SkipDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", ".svn", "AppData", "$Recycle.Bin", "System Volume Information",
        "Windows", "Windows.old", "Program Files", "Program Files (x86)", "ProgramData",
        "bin", "obj", ".cache", ".nuget", ".venv", "__pycache__", "dist", "build", ".idea", ".vs",
    };

    /// <summary>Standardowe foldery użytkownika: pulpit, dokumenty, pobrane (i pulpit publiczny pomijamy).</summary>
    public static IReadOnlyList<string> DefaultRoots()
    {
        var roots = new List<string>();
        foreach (var folder in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments })
        {
            try
            {
                string path = Environment.GetFolderPath(folder);
                if (path.Length > 0 && Directory.Exists(path) && !roots.Contains(path, StringComparer.OrdinalIgnoreCase)) roots.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        // Downloads is not a stable SpecialFolder on every Windows version — resolve it from the profile.
        try
        {
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(downloads) && !roots.Contains(downloads, StringComparer.OrdinalIgnoreCase)) roots.Add(downloads);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        return roots;
    }

    /// <summary>Szuka plików, których nazwa zawiera <paramref name="pattern"/>.</summary>
    public static FileFinderResult Find(IEnumerable<string> roots, string pattern,
        int maxResults = DefaultMaxResults, int maxDepth = DefaultMaxDepth,
        int maxDirectories = DefaultMaxDirectories, TimeSpan? timeLimit = null)
    {
        var results = new List<string>();
        bool truncated = false, accessDenied = false, timedOut = false;
        string needle = (pattern ?? "").Trim();
        if (needle.Length == 0) return new FileFinderResult([], 0, false, false, false);

        var limit = timeLimit ?? DefaultTimeLimit;
        var clock = Stopwatch.StartNew();
        var queue = new Queue<(string Path, int Depth)>();
        int searched = 0;
        foreach (string root in roots)
        {
            if (root.Length > 0 && Directory.Exists(root)) queue.Enqueue((root, 0));
        }

        while (queue.Count > 0 && results.Count < maxResults)
        {
            if (clock.Elapsed > limit) { timedOut = true; break; }
            var (directory, depth) = queue.Dequeue();
            searched++;
            if (searched > maxDirectories) { truncated = true; break; }

            try
            {
                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    if (Path.GetFileName(file).Contains(needle, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(file);
                        if (results.Count >= maxResults) { truncated = true; break; }
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
            {
                accessDenied = true;
            }

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
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
            {
                accessDenied = true;
            }
        }

        return new FileFinderResult(results, searched, truncated, accessDenied, timedOut);
    }

    /// <summary>Czytelna odpowiedź do czatu — zawsze z informacją, gdzie szukałem.</summary>
    public static string Describe(IReadOnlyList<string> roots, string pattern)
    {
        var result = Find(roots, pattern);
        if (result.Paths.Count == 0)
            return "Nie znalazłem pliku z „" + pattern + "” w nazwie (szukałem w: " + string.Join(", ", roots.Select(x => Short(x))) + ", do głębokości 4, tylko odczyt" +
                result.Notes + "). Spróbuj innego fragmentu nazwy.";
        return "Znalazłem " + result.Paths.Count + " plików z „" + pattern + "” w nazwie (katalogi: " + string.Join(", ", roots.Select(x => Short(x))) + ", przejrzane: " +
                result.SearchedDirectories + result.Notes + "):\n" + string.Join("\n", result.Paths.Select((x, i) => $"{i + 1}. {x}"));
    }

    private static string Short(string path)
    {
        try
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return path.StartsWith(profile, StringComparison.OrdinalIgnoreCase) ? "~" + path[profile.Length..] : path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return path;
        }
    }
}
