using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SentinelX.Core.Runtime;

public sealed record BackupInfo(string Name, string Path, long Size, int Files, DateTimeOffset CreatedAt, string Sha256);

/// <summary>
/// SEKCJA 1 · pozycja 20 — Backup Manager z rotacją i kompresją.
/// <para>Kopia zapasowa to ZIP z manifestem SHA-256 w środku plus skrót całego archiwum obok.
/// <see cref="Verify"/> rozpakowuje archiwum do katalogu tymczasowego i liczy skróty od nowa —
/// czyli naprawdę sprawdza, czy kopia da się odtworzyć, zamiast tylko sprawdzać, że plik istnieje.</para>
/// <para>Rotacja jest jawna (domyślnie 5 kopii) i dotyczy wyłącznie plików utworzonych przez ten
/// manager — nic innego w katalogu nie jest kasowane.</para>
/// </summary>
public sealed class BackupManager
{
    private const string Prefix = "sentinel-backup-";
    private const int MaxFiles = 5000;

    public BackupManager(string? directory = null, int keep = 5)
    {
        Directory = directory ?? AppPaths.BackupsDirectory;
        Keep = Math.Clamp(keep, 1, 50);
    }

    public string Directory { get; }
    public int Keep { get; }

    public IReadOnlyList<BackupInfo> List()
    {
        if (!System.IO.Directory.Exists(Directory)) return [];
        return System.IO.Directory.EnumerateFiles(Directory, Prefix + "*.zip")
            .Select(path =>
            {
                var info = new FileInfo(path);
                int files = 0;
                string sidecar = Sidecar(path);
                if (File.Exists(sidecar))
                {
                    try
                    {
                        var record = JsonSerializer.Deserialize<SidecarRecord>(File.ReadAllText(sidecar));
                        files = record?.Files ?? 0;
                    }
                    catch { files = 0; }
                }
                return new BackupInfo(info.Name, info.FullName, info.Length, files, info.CreationTimeUtc, ReadHash(sidecar));
            })
            .OrderByDescending(x => x.CreatedAt)
            .ToArray();
    }

    private sealed class SidecarRecord
    {
        public int Files { get; set; }
        public long Bytes { get; set; }
        public string Sha256 { get; set; } = "";
        public string CreatedAt { get; set; } = "";
    }

    /// <summary>Tworzy kopię katalogu. Zwraca czytelny raport (albo uczciwy powód odmowy).</summary>
    public string Create(string sourceDirectory, string label = "dane", IEnumerable<string>? includeExtensions = null,
        IEnumerable<string>? excludeDirectories = null)
    {
        if (!System.IO.Directory.Exists(sourceDirectory)) return "Nie ma katalogu do kopii: " + sourceDirectory;
        string safeLabel = Sanitize(label);
        var allowed = includeExtensions?
            .Select(x => x.StartsWith('.') ? x.ToLowerInvariant() : "." + x.ToLowerInvariant())
            .ToHashSet();
        // Katalog kopii i cache są wykluczone: inaczej kopia zawierałaby poprzednie kopie i rosła w nieskończoność.
        var excluded = (excludeDirectories ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => System.IO.Path.GetFullPath(x).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar)
            .ToArray();

        List<(string Full, string Relative)> files;
        try
        {
            files = System.IO.Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
                .Where(x => allowed is null or { Count: 0 } || allowed.Contains(System.IO.Path.GetExtension(x).ToLowerInvariant()))
                .Where(x => !excluded.Any(e => System.IO.Path.GetFullPath(x).StartsWith(e, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Take(MaxFiles)
                .Select(x => (x, System.IO.Path.GetRelativePath(sourceDirectory, x)))
                .ToList();
        }
        catch (Exception ex)
        {
            return "Nie odczytałem katalogu źródłowego: " + ex.Message;
        }
        if (files.Count == 0) return "Nie ma czego kopiować — katalog " + sourceDirectory + " nie ma plików pasujących do filtra.";

        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            string stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string archivePath = System.IO.Path.Combine(Directory, Prefix + safeLabel + "-" + stamp + ".zip");

            var manifest = new List<ManifestEntry>();
            using (var stream = File.Create(archivePath))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach (var (full, relative) in files)
                {
                    string hash;
                    long size;
                    using (var fileStream = File.OpenRead(full))
                    using (var sha = SHA256.Create())
                    {
                        size = fileStream.Length;
                        hash = Convert.ToHexString(sha.ComputeHash(fileStream)).ToLowerInvariant();
                    }
                    manifest.Add(new ManifestEntry(relative.Replace('\\', '/'), size, hash));
                    archive.CreateEntryFromFile(full, "files/" + relative.Replace('\\', '/'), CompressionLevel.Optimal);
                }
                var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
                using var writer = new StreamWriter(manifestEntry.Open(), new UTF8Encoding(false));
                writer.Write(IntegrityManifest.Serialize(manifest));
            }

            string archiveHash = IntegrityManifest.HashFile(archivePath);
            long archiveSize = new FileInfo(archivePath).Length;
            File.WriteAllText(Sidecar(archivePath), JsonSerializer.Serialize(new SidecarRecord
            {
                Files = files.Count,
                Bytes = archiveSize,
                Sha256 = archiveHash,
                CreatedAt = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)
            }, new JsonSerializerOptions { WriteIndented = true }));

            int removed = Rotate();
            return "Kopia zapasowa utworzona i odczytana zwrotnie (manifest wewnątrz archiwum)." + Environment.NewLine +
                "· plik: " + archivePath + Environment.NewLine +
                "· plików w kopii: " + files.Count + " · rozmiar: " + (archiveSize / 1024.0).ToString("0.#", CultureInfo.GetCultureInfo("pl-PL")) + " KiB" + Environment.NewLine +
                "· SHA-256 archiwum: " + archiveHash + Environment.NewLine +
                (removed > 0 ? "· rotacja usunęła " + removed + " najstarszych kopii (limit " + Keep + ")." : "· rotacja: nic nie usunięto (limit " + Keep + ").");
        }
        catch (Exception ex)
        {
            return "Nie udało się utworzyć kopii: " + ex.GetType().Name + " — " + ex.Message;
        }
    }

    /// <summary>Sprawdza kopię od zera: rozpakowanie do katalogu tymczasowego + ponowne liczenie SHA-256.</summary>
    public string Verify(BackupInfo backup)
    {
        if (!File.Exists(backup.Path)) return "Nie ma pliku kopii: " + backup.Path;
        string temporary = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sentinel-verify-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            System.IO.Directory.CreateDirectory(temporary);
            ZipFile.ExtractToDirectory(backup.Path, temporary);
            string manifestPath = System.IO.Path.Combine(temporary, "manifest.json");
            if (!File.Exists(manifestPath)) return "W archiwum nie ma manifestu — nie umiem potwierdzić zawartości.";
            if (!IntegrityManifest.TryDeserialize(File.ReadAllText(manifestPath), out var expected, out string error)) return error;
            var actual = IntegrityManifest.Build(System.IO.Path.Combine(temporary, "files"));
            string comparison = IntegrityManifest.Compare(expected, actual);
            string currentHash = IntegrityManifest.HashFile(backup.Path);
            bool hashMatches = string.Equals(currentHash, backup.Sha256, StringComparison.OrdinalIgnoreCase);
            return "Weryfikacja kopii „" + backup.Name + "”:" + Environment.NewLine +
                comparison + Environment.NewLine +
                "· SHA-256 archiwum: " + (backup.Sha256.Length == 0
                    ? "brak w pliku towarzyszącym — policzone teraz: " + currentHash
                    : hashMatches ? "zgadza się z zapisanym" : "NIE zgadza się z zapisanym — archiwum zmieniło się po utworzeniu");
        }
        catch (Exception ex)
        {
            return "Nie zweryfikowałem kopii: " + ex.GetType().Name + " — " + ex.Message;
        }
        finally
        {
            try { if (System.IO.Directory.Exists(temporary)) System.IO.Directory.Delete(temporary, true); } catch { /* sprzątanie */ }
        }
    }

    /// <summary>Usuwa najstarsze kopie ponad limit. Zwraca liczbę usuniętych plików kopii.</summary>
    public int Rotate()
    {
        var all = List();
        int removed = 0;
        foreach (var backup in all.Skip(Keep))
        {
            try
            {
                File.Delete(backup.Path);
                string sidecar = Sidecar(backup.Path);
                if (File.Exists(sidecar)) File.Delete(sidecar);
                removed++;
            }
            catch { /* nieudane usunięcie nie przerywa rotacji */ }
        }
        return removed;
    }

    public string Describe()
    {
        var all = List();
        if (all.Count == 0) return "Kopii zapasowych nie ma. Utwórz: „kopia danych”.";
        var lines = new List<string> { "Kopie zapasowe (" + all.Count + "/" + Keep + ") w " + Directory + ":" };
        lines.AddRange(all.Select(x => "· " + x.Name + " · " + (x.Size / 1024.0).ToString("0.#", CultureInfo.GetCultureInfo("pl-PL")) + " KiB · " +
            x.Files + " plików · " + x.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm") +
            (x.Sha256.Length > 0 ? " · SHA-256 " + x.Sha256[..12] + "…" : "")));
        lines.Add("Weryfikacja: „weryfikuj kopie: " + all[0].Name + "” (rozpakowanie i ponowne liczenie skrótów).");
        return string.Join(Environment.NewLine, lines);
    }

    private static string Sidecar(string archivePath) => archivePath + ".json";

    private static string ReadHash(string sidecarPath)
    {
        try
        {
            if (!File.Exists(sidecarPath)) return "";
            var record = JsonSerializer.Deserialize<SidecarRecord>(File.ReadAllText(sidecarPath));
            return record?.Sha256 ?? "";
        }
        catch { return ""; }
    }

    private static string Sanitize(string label)
    {
        var builder = new StringBuilder();
        foreach (char character in label ?? "")
            builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-');
        string clean = builder.ToString().Trim('-');
        return clean.Length == 0 ? "dane" : (clean.Length > 40 ? clean[..40] : clean);
    }
}
