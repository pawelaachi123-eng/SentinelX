using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SentinelX.Core.Runtime;

public sealed record ManifestEntry(string RelativePath, long Size, string Sha256);

/// <summary>
/// SEKCJA 1 · pozycja 22 — Integrity Checker: manifest SHA-256 plików krytycznych i porównanie
/// „co się zmieniło”. Manifest jest tekstem JSON, więc można go schować razem z kopią zapasową.
/// <para>Zasada uczciwości: brak pliku i plik zmieniony to dwie różne informacje; plik nieczytelny
/// (zablokowany, bez uprawnień) też jest raportowany osobno i nigdy nie udaje zgodnego.</para>
/// </summary>
public static class IntegrityManifest
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static IReadOnlyList<ManifestEntry> Build(string root, int maxFiles = 2000, IEnumerable<string>? includeExtensions = null)
    {
        var entries = new List<ManifestEntry>();
        if (!Directory.Exists(root)) return entries;
        var allowed = includeExtensions?.Select(x => x.StartsWith('.') ? x.ToLowerInvariant() : "." + x.ToLowerInvariant()).ToHashSet();
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (entries.Count >= maxFiles) break;
            if (allowed is { Count: > 0 } && !allowed.Contains(System.IO.Path.GetExtension(file).ToLowerInvariant())) continue;
            try
            {
                var info = new FileInfo(file);
                entries.Add(new ManifestEntry(Relative(root, file), info.Length, HashFile(file)));
            }
            catch (Exception)
            {
                entries.Add(new ManifestEntry(Relative(root, file), -1, "nieczytelny"));
            }
        }
        return entries;
    }

    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static string Relative(string root, string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        string rootFull = System.IO.Path.GetFullPath(root);
        return full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)
            ? full[rootFull.Length..].TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
            : full;
    }

    public static string Serialize(IReadOnlyList<ManifestEntry> entries) => JsonSerializer.Serialize(entries, Json);

    public static bool TryDeserialize(string json, out IReadOnlyList<ManifestEntry> entries, out string error)
    {
        entries = [];
        error = "";
        try
        {
            var parsed = JsonSerializer.Deserialize<List<ManifestEntry>>(json);
            if (parsed is null) { error = "Manifest jest pusty."; return false; }
            entries = parsed;
            return true;
        }
        catch (Exception ex)
        {
            error = "Nie odczytałem manifestu: " + ex.Message;
            return false;
        }
    }

    /// <summary>Porównanie oczekiwanego manifestu z rzeczywistym stanem katalogu.</summary>
    public static string Compare(IReadOnlyList<ManifestEntry> expected, IReadOnlyList<ManifestEntry> actual)
    {
        var expectedMap = expected.ToDictionary(x => x.RelativePath, x => x, StringComparer.OrdinalIgnoreCase);
        var actualMap = actual.ToDictionary(x => x.RelativePath, x => x, StringComparer.OrdinalIgnoreCase);
        var changed = new List<string>();
        var missing = new List<string>();
        var added = new List<string>();
        var unreadable = new List<string>();

        foreach (var pair in expectedMap)
        {
            if (!actualMap.TryGetValue(pair.Key, out var now)) { missing.Add(pair.Key); continue; }
            if (now.Size < 0 || now.Sha256 == "nieczytelny") { unreadable.Add(pair.Key); continue; }
            if (!string.Equals(now.Sha256, pair.Value.Sha256, StringComparison.OrdinalIgnoreCase) || now.Size != pair.Value.Size)
                changed.Add(pair.Key);
        }
        foreach (var pair in actualMap)
        {
            if (!expectedMap.ContainsKey(pair.Key)) added.Add(pair.Key);
        }

        if (changed.Count == 0 && missing.Count == 0 && added.Count == 0 && unreadable.Count == 0)
            return "Zgodne: " + expected.Count + " plików ma dokładnie te same rozmiary i skróty SHA-256 co w manifeście.";

        var lines = new List<string>
        {
            "Różnice wobec manifestu (" + expected.Count + " plików w manifeście):",
            "· zmienione: " + changed.Count, "· brakujące: " + missing.Count, "· nowe: " + added.Count, "· nieczytelne: " + unreadable.Count
        };
        lines.AddRange(changed.Take(10).Select(x => "  ~ " + x));
        lines.AddRange(missing.Take(10).Select(x => "  - " + x));
        lines.AddRange(added.Take(10).Select(x => "  + " + x));
        lines.AddRange(unreadable.Take(10).Select(x => "  ? " + x));
        return string.Join(Environment.NewLine, lines);
    }

    public static string Describe(IReadOnlyList<ManifestEntry> entries)
    {
        if (entries.Count == 0) return "Manifest jest pusty — nie ma czego pilnować.";
        long total = entries.Where(x => x.Size > 0).Sum(x => x.Size);
        var unreadable = entries.Count(x => x.Size < 0);
        return "Manifest: " + entries.Count + " plików · " + (total / 1024.0).ToString("0.#", CultureInfo.GetCultureInfo("pl-PL")) + " KiB" +
            (unreadable > 0 ? " · nieczytelne: " + unreadable : "") + Environment.NewLine +
            string.Join(Environment.NewLine, entries.Take(10).Select(x => "· " + x.RelativePath + " · " + x.Size + " B · " + x.Sha256[..Math.Min(16, x.Sha256.Length)] + "…")) +
            (entries.Count > 10 ? Environment.NewLine + "… i " + (entries.Count - 10) + " więcej." : "");
    }
}
