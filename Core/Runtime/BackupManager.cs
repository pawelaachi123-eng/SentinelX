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
/// SEKCJA 1 ┬Ě pozycja 20 ÔÇö Backup Manager z rotacj─ů i kompresj─ů.
/// <para>Kopia zapasowa to ZIP z manifestem SHA-256 w ┼Ťrodku plus skr├│t ca┼éego archiwum obok.
/// <see cref="Verify"/> rozpakowuje archiwum do katalogu tymczasowego i liczy skr├│ty od nowa ÔÇö
/// czyli naprawd─Ö sprawdza, czy kopia da si─Ö odtworzy─ç, zamiast tylko sprawdza─ç, ┼╝e plik istnieje.</para>
/// <para>Rotacja jest jawna (domy┼Ťlnie 5 kopii) i dotyczy wy┼é─ůcznie plik├│w utworzonych przez ten
/// manager ÔÇö nic innego w katalogu nie jest kasowane.</para>
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

    /// <summary>Tworzy kopi─Ö katalogu. Zwraca czytelny raport (albo uczciwy pow├│d odmowy).</summary>
    public string Create(string sourceDirectory, string label = "dane", IEnumerable<string>? includeExtensions = null,
        IEnumerable<string>? excludeDirectories = null)
    {
        if (!System.IO.Directory.Exists(sourceDirectory)) return "Nie ma katalogu do kopii: " + sourceDirectory;
        string safeLabel = Sanitize(label);
        var allowed = includeExtensions?
            .Select(x => x.StartsWith('.') ? x.ToLowerInvariant() : "." + x.ToLowerInvariant())
            .ToHashSet();
        // Katalog kopii i cache s─ů wykluczone: inaczej kopia zawiera┼éaby poprzednie kopie i ros┼éa w niesko┼äczono┼Ť─ç.
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
            return "Nie odczyta┼éem katalogu ┼║r├│d┼éowego: " + ex.Message;
        }
        if (files.Count == 0) return "Nie ma czego kopiowa─ç ÔÇö katalog " + sourceDirectory + " nie ma plik├│w pasuj─ůcych do filtra.";

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
            return "Kopia zapasowa utworzona i odczytana zwrotnie (manifest wewn─ůtrz archiwum)." + Environment.NewLine +
                "┬Ě plik: " + archivePath + Environment.NewLine +
                "┬Ě plik├│w w kopii: " + files.Count + " ┬Ě rozmiar: " + (archiveSize / 1024.0).ToString("0.#", CultureInfo.GetCultureInfo("pl-PL")) + " KiB" + Environment.NewLine +
                "┬Ě SHA-256 archiwum: " + archiveHash + Environment.NewLine +
                (removed > 0 ? "┬Ě rotacja usun─Ö┼éa " + removed + " najstarszych kopii (limit " + Keep + ")." : "┬Ě rotacja: nic nie usuni─Öto (limit " + Keep + ").");
        }
        catch (Exception ex)
        {
            return "Nie uda┼éo si─Ö utworzy─ç kopii: " + ex.GetType().Name + " ÔÇö " + ex.Message;
        }
    }

    /// <summary>Sprawdza kopi─Ö od zera: rozpakowanie do katalogu tymczasowego + ponowne liczenie SHA-256.</summary>
    public string Verify(BackupInfo backup)
    {
        if (!File.Exists(backup.Path)) return "Nie ma pliku kopii: " + backup.Path;
        string temporary = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sentinel-verify-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            System.IO.Directory.CreateDirectory(temporary);
            ZipFile.ExtractToDirectory(backup.Path, temporary);
            string manifestPath = System.IO.Path.Combine(temporary, "manifest.json");
            if (!File.Exists(manifestPath)) return "W archiwum nie ma manifestu ÔÇö nie umiem potwierdzi─ç zawarto┼Ťci.";
            if (!IntegrityManifest.TryDeserialize(File.ReadAllText(manifestPath), out var expected, out string error)) return error;
            var actual = IntegrityManifest.Build(System.IO.Path.Combine(temporary, "files"));
            string comparison = IntegrityManifest.Compare(expected, actual);
            string currentHash = IntegrityManifest.HashFile(backup.Path);
            bool hashMatches = string.Equals(currentHash, backup.Sha256, StringComparison.OrdinalIgnoreCase);
            return "Weryfikacja kopii ÔÇ×" + backup.Name + "ÔÇŁ:" + Environment.NewLine +
                comparison + Environment.NewLine +
                "┬Ě SHA-256 archiwum: " + (backup.Sha256.Length == 0
                    ? "brak w pliku towarzysz─ůcym ÔÇö policzone teraz: " + currentHash
                    : hashMatches ? "zgadza si─Ö z zapisanym" : "NIE zgadza si─Ö z zapisanym ÔÇö archiwum zmieni┼éo si─Ö po utworzeniu");
        }
        catch (Exception ex)
        {
            return "Nie zweryfikowa┼éem kopii: " + ex.GetType().Name + " ÔÇö " + ex.Message;
        }
        finally
        {
            try { if (System.IO.Directory.Exists(temporary)) System.IO.Directory.Delete(temporary, true); } catch { /* sprz─ůtanie */ }
        }
    }

    /// <summary>Usuwa najstarsze kopie ponad limit. Zwraca liczb─Ö usuni─Ötych plik├│w kopii.</summary>
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
            catch { /* nieudane usuni─Öcie nie przerywa rotacji */ }
        }
        return removed;
    }

    public string Describe()
    {
        var all = List();
        if (all.Count == 0) return "Kopii zapasowych nie ma. Utw├│rz: ÔÇ×kopia danychÔÇŁ.";
        var lines = new List<string> { "Kopie zapasowe (" + all.Count + "/" + Keep + ") w " + Directory + ":" };
        lines.AddRange(all.Select(x => "┬Ě " + x.Name + " ┬Ě " + (x.Size / 1024.0).ToString("0.#", CultureInfo.GetCultureInfo("pl-PL")) + " KiB ┬Ě " +
            x.Files + " plik├│w ┬Ě " + x.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm") +
            (x.Sha256.Length > 0 ? " ┬Ě SHA-256 " + x.Sha256[..12] + "ÔÇŽ" : "")));
        lines.Add("Weryfikacja: ÔÇ×weryfikuj kopie: " + all[0].Name + "ÔÇŁ (rozpakowanie i ponowne liczenie skr├│t├│w).");
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
