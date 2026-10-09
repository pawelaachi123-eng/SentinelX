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
/// SEKCJA 1 ┬Ě pozycja 22 ÔÇö Integrity Checker: manifest SHA-256 plik├│w krytycznych i por├│wnanie
/// ÔÇ×co si─Ö zmieni┼éoÔÇŁ. Manifest jest tekstem JSON, wi─Öc mo┼╝na go schowa─ç razem z kopi─ů zapasow─ů.
/// <para>Zasada uczciwo┼Ťci: brak pliku i plik zmieniony to dwie r├│┼╝ne informacje; plik nieczytelny
/// (zablokowany, bez uprawnie┼ä) te┼╝ jest raportowany osobno i nigdy nie udaje zgodnego.</para>
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
        string relative = full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)
            ? full[rootFull.Length..].TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
            : full;
        // 0.97 ┬Ě ┼Ücie┼╝ki w manife┼Ťcie s─ů ZAWSZE z uko┼Ťnikiem ÔÇ×/ÔÇŁ, niezale┼╝nie od systemu ÔÇö inaczej
        // ten sam plik raz by┼é zapisany jako ÔÇ×podkatalog\notatka.mdÔÇŁ (Windows), a raz
        // ÔÇ×podkatalog/notatka.mdÔÇŁ, i por├│wnanie kopii zapasowej pokazywa┼éo fa┼észywe r├│┼╝nice.
        return relative.Replace('\\', '/');
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
            error = "Nie odczyta┼éem manifestu: " + ex.Message;
            return false;
        }
    }

    /// <summary>Por├│wnanie oczekiwanego manifestu z rzeczywistym stanem katalogu.</summary>
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
            return "Zgodne: " + expected.Count + " plik├│w ma dok┼éadnie te same rozmiary i skr├│ty SHA-256 co w manife┼Ťcie.";

        var lines = new List<string>
        {
            "R├│┼╝nice wobec manifestu (" + expected.Count + " plik├│w w manife┼Ťcie):",
            "┬Ě zmienione: " + changed.Count, "┬Ě brakuj─ůce: " + missing.Count, "┬Ě nowe: " + added.Count, "┬Ě nieczytelne: " + unreadable.Count
        };
        lines.AddRange(changed.Take(10).Select(x => "  ~ " + x));
        lines.AddRange(missing.Take(10).Select(x => "  - " + x));
        lines.AddRange(added.Take(10).Select(x => "  + " + x));
        lines.AddRange(unreadable.Take(10).Select(x => "  ? " + x));
        return string.Join(Environment.NewLine, lines);
    }

    public static string Describe(IReadOnlyList<ManifestEntry> entries)
    {
        if (entries.Count == 0) return "Manifest jest pusty ÔÇö nie ma czego pilnowa─ç.";
        long total = entries.Where(x => x.Size > 0).Sum(x => x.Size);
        var unreadable = entries.Count(x => x.Size < 0);
        return "Manifest: " + entries.Count + " plik├│w ┬Ě " + (total / 1024.0).ToString("0.#", CultureInfo.GetCultureInfo("pl-PL")) + " KiB" +
            (unreadable > 0 ? " ┬Ě nieczytelne: " + unreadable : "") + Environment.NewLine +
            string.Join(Environment.NewLine, entries.Take(10).Select(x => "┬Ě " + x.RelativePath + " ┬Ě " + x.Size + " B ┬Ě " + x.Sha256[..Math.Min(16, x.Sha256.Length)] + "ÔÇŽ")) +
            (entries.Count > 10 ? Environment.NewLine + "ÔÇŽ i " + (entries.Count - 10) + " wi─Öcej." : "");
    }
}
