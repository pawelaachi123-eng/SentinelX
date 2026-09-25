using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace SentinelX.Services.Files;

/// <summary>Moves one file to the Windows Recycle Bin (restorable). Abstracted so CI tests run headless.</summary>
public interface IFileRecycler
{
    bool TryRecycle(string fullPath, out string message);
}

public sealed class RecycleBinFileRecycler : IFileRecycler
{
    public bool TryRecycle(string fullPath, out string message)
    {
        try
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(fullPath,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            message = "";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            message = ex.Message;
            return false;
        }
    }
}

/// <summary>0.92 · safe file work: duplicate scan (SHA-256), read-only tidying report, and Recycle-Bin
/// deletion behind an explicit two-step confirmation. Reports never change anything; the only mutating
/// operation sends a single file to the Recycle Bin and only after the user confirms the exact path.
/// Junctions/symlinks are never followed (no scope escape), inaccessible entries are skipped, and hard
/// caps keep scans bounded.</summary>
public sealed class FileCleanupService
{
    internal const int MaxEnumeratedFiles = 50_000;
    internal const long MaxHashedFileSize = 256L * 1024 * 1024;
    internal const int MaxReportedGroups = 25;
    internal const int MaxListedPaths = 10;

    private static readonly Regex Duplicates = new(@"^(?:duplikaty|duplikatow|duplikatów|znajdź duplikaty|znajdz duplikaty)(?::\s*|\s+|w\s+)(?<dir>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TidyReport = new(@"^(?:porzadki|porządki|raport porzadkowy|raport porządkowy|plan porzadkow|plan porządków)(?::\s*|\s+|w\s+)(?<dir>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RecycleFile = new(@"^(?:usuń do kosza|usun do kosza|przenieś do kosza|przenies do kosza|wyrzuć do kosza|wyrzuc do kosza)(?::\s*|\s+)(?<path>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // Compared against ConversationMemoryService.Normalize(input) — diacritics are already stripped there.
    private static readonly string[] Confirmations = ["tak", "potwierdz", "tak usun", "tak, usun", "usun to", "ok"];
    private static readonly string[] Cancellations = ["nie", "anuluj", "stop", "nie usuwaj"];

    private readonly ActionHistoryService history;
    private readonly IFileRecycler recycler;
    private PendingDeletion? pending;

    private sealed record PendingDeletion(string Path, long Size);

    public FileCleanupService(ActionHistoryService? history = null, IFileRecycler? recycler = null)
    {
        history = history ?? new ActionHistoryService();
        this.history = history;
        this.recycler = recycler ?? new RecycleBinFileRecycler();
    }

    /// <summary>Returns null when the input is not a file-cleanup command, so the router moves on.</summary>
    public async Task<string?> ProcessAsync(string command, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string normalized = ConversationMemoryService.Normalize(command).Trim().TrimEnd('.', '!', '?');

        // A pending Recycle-Bin deletion is one-shot: confirm executes it, cancel drops it,
        // anything else drops it too — a stale pending delete must never fire on a later „tak”.
        if (pending is { } waiting)
        {
            if (Array.Exists(Confirmations, c => c == normalized)) { pending = null; return Recycle(waiting, command); }
            if (Array.Exists(Cancellations, c => c == normalized)) { pending = null; return "Anulowane — nic nie zostało usunięte."; }
            pending = null;
        }

        // Bare commands without an argument: show usage instead of falling through to the model.
        if (normalized is "duplikaty" or "znajdz duplikaty" or "porzadki" or "raport porzadkowy" or "plan porzadkow" or "duplikaty:" or "porzadki:")
            return "Podaj folder, np. „duplikaty: C:\\Dane” albo „porzadki: C:\\Dane”. Raport jest tylko do odczytu.";
        if (normalized is "usun do kosza" or "usun do kosza:")
            return "Podaj pełną ścieżkę pliku, np. „usuń do kosza: C:\\Dane\\stary raport.txt”. Foldery zostawiam w spokoju.";

        var dup = Duplicates.Match(command.Trim());
        if (dup.Success) return await DuplicatesReportAsync(CleanDir(dup.Groups["dir"].Value), token);
        var tidy = TidyReport.Match(command.Trim());
        if (tidy.Success) return await TidyReportAsync(CleanDir(tidy.Groups["dir"].Value), token);
        var recycle = RecycleFile.Match(command.Trim());
        if (recycle.Success) return ProposeRecycle(recycle.Groups["path"].Value.Trim().Trim('"'));
        return null;
    }

    private static string CleanDir(string raw)
    {
        string dir = raw.Trim().Trim('"');
        foreach (string lead in ["folderze ", "folderu ", "katalogu ", "katalog "])
            if (dir.StartsWith(lead, StringComparison.OrdinalIgnoreCase)) { dir = dir[lead.Length..]; break; }
        return Environment.ExpandEnvironmentVariables(dir.Trim());
    }

    // ---------- scans (read-only) ----------

    private sealed record ScannedFile(string Path, long Length, DateTime LastWriteUtc);
    private sealed record ScanResult(List<ScannedFile> Files, bool Truncated, int InaccessibleSkipped);

    private static async Task<ScanResult> ScanAsync(string directory, CancellationToken token)
    {
        var files = new List<ScannedFile>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint, // never follow junctions/symlinks — no scope escape
        };
        bool truncated = false;
        foreach (string path in Directory.EnumerateFiles(directory, "*", options))
        {
            token.ThrowIfCancellationRequested();
            if (files.Count >= MaxEnumeratedFiles) { truncated = true; break; }
            try
            {
                var info = new FileInfo(path);
                files.Add(new ScannedFile(path, info.Length, info.LastWriteTimeUtc));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* counted below via size */ }
        }
        return new ScanResult(files, truncated, 0);
    }

    private static string TryOpenDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return "Podaj folder, np. „duplikaty: C:\\Dane”.";
        string full;
        try { full = Path.GetFullPath(directory); }
        catch (Exception ex) when (ex is ArgumentException or System.Security.SecurityException or NotSupportedException or PathTooLongException)
        { return "Nie rozumiem tej ścieżki: " + ex.Message; }
        if (!Directory.Exists(full)) return "Taki folder nie istnieje: " + full;
        try
        {
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                return "Ten folder jest dowiązaniem (junction/symlink) — nie skanuję poza jego obręb.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "Brak dostępu do folderu: " + full; }
        return full;
    }

    private sealed record DuplicateGroup(string Hash, long Size, List<string> Paths);

    private static async Task<(List<DuplicateGroup> groups, long reclaimable, int tooLarge, int inaccessible)> FindDuplicatesAsync(ScanResult scan, CancellationToken token)
    {
        var groups = new List<DuplicateGroup>();
        int tooLarge = 0, inaccessible = 0;
        foreach (var byLength in scan.Files.Where(f => f.Length > 0).GroupBy(f => f.Length).Where(g => g.Count() > 1))
        {
            if (byLength.Key > MaxHashedFileSize) { tooLarge += byLength.Count(); continue; }
            var byHash = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in byLength)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    await using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
                    string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
                    if (!byHash.TryGetValue(hash, out var list)) byHash[hash] = list = [];
                    list.Add(file.Path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { inaccessible++; }
            }
            foreach (var (hash, paths) in byHash.Where(x => x.Value.Count > 1))
                groups.Add(new DuplicateGroup(hash, byLength.Key, paths));
        }
        groups.Sort((a, b) => (b.Size * (b.Paths.Count - 1)).CompareTo(a.Size * (a.Paths.Count - 1)));
        long reclaimable = groups.Sum(g => g.Size * (g.Paths.Count - 1));
        return (groups, reclaimable, tooLarge, inaccessible);
    }

    private async Task<string> DuplicatesReportAsync(string directory, CancellationToken token)
    {
        string opened = TryOpenDirectory(directory);
        if (!Directory.Exists(opened)) return opened;
        string id = history.CreateActionId();
        history.AddRunning(id, "FILE_SCAN_DUPLICATES", "duplikaty: " + opened);
        try
        {
            var scan = await ScanAsync(opened, token);
            var (groups, reclaimable, tooLarge, inaccessible) = await FindDuplicatesAsync(scan, token);
            var lines = new List<string>
            {
                "DUPLIKATY • " + opened,
                "Przeskanowano: " + scan.Files.Count.ToString("N0", CultureInfo.InvariantCulture) + " plików"
                    + (scan.Truncated ? " (limit " + MaxEnumeratedFiles.ToString("N0", CultureInfo.InvariantCulture) + " — reszta pominięta)" : "")
                    + (tooLarge > 0 ? ", pominięte >256 MB: " + tooLarge : "")
                    + (inaccessible > 0 ? ", niedostępne: " + inaccessible : "") + ".",
            };
            if (groups.Count == 0) lines.Add("Brak duplikatów — każda treść występuje najwyżej raz.");
            else
            {
                lines.Add("Grup duplikatów: " + groups.Count + " — można odzyskać ok. " + FormatBytes(reclaimable) + ".");
                int shown = 0;
                foreach (var group in groups.Take(MaxReportedGroups))
                {
                    shown++;
                    lines.Add("");
                    lines.Add(shown + ". " + Path.GetFileName(group.Paths[0]) + " × " + group.Paths.Count + " (" + FormatBytes(group.Size) + " każdy, SHA-256: " + group.Hash[..12] + "…)");
                    foreach (string path in group.Paths.Take(MaxListedPaths)) lines.Add("   · " + path);
                    if (group.Paths.Count > MaxListedPaths) lines.Add("   · …i " + (group.Paths.Count - MaxListedPaths) + " kolejnych");
                }
                if (groups.Count > MaxReportedGroups)
                {
                    lines.Add("");
                    lines.Add("…oraz " + (groups.Count - MaxReportedGroups) + " dalszych grup (limit raportu to " + MaxReportedGroups + ").");
                }
            }
            lines.Add("");
            lines.Add("To jest raport — niczego nie usuwam. Pojedynczy plik usuniesz poleceniem „usuń do kosza: <pełna ścieżka>”.");
            string text = string.Join(Environment.NewLine, lines);
            history.AddResult(id, "FILE_SCAN_DUPLICATES", "duplikaty: " + opened,
                ActionExecutionResult.VerifiedSuccess(text, $"Katalog: {opened}; plików: {scan.Files.Count}; grup: {groups.Count}; odczyt {DateTime.Now:O}"));
            return text;
        }
        catch (OperationCanceledException) { history.AddCancelled(id, "FILE_SCAN_DUPLICATES", "duplikaty: " + opened, "Skanowanie przerwane."); throw; }
    }

    private async Task<string> TidyReportAsync(string directory, CancellationToken token)
    {
        string opened = TryOpenDirectory(directory);
        if (!Directory.Exists(opened)) return opened;
        string id = history.CreateActionId();
        history.AddRunning(id, "FILE_SCAN_TIDY", "porzadki: " + opened);
        try
        {
            var scan = await ScanAsync(opened, token);
            var (groups, reclaimable, _, _) = await FindDuplicatesAsync(scan, token);
            var largest = scan.Files.OrderByDescending(f => f.Length).Take(5).ToArray();
            var empty = scan.Files.Where(f => f.Length == 0).ToArray();
            var oldest = scan.Files.OrderBy(f => f.LastWriteUtc).Take(5).ToArray();
            long total = scan.Files.Sum(f => f.Length);
            var lines = new List<string>
            {
                "RAPORT PORZĄDKOWY • " + opened,
                "Plików: " + scan.Files.Count.ToString("N0", CultureInfo.InvariantCulture) + " • rozmiar: " + FormatBytes(total)
                    + (scan.Truncated ? " • limit " + MaxEnumeratedFiles.ToString("N0", CultureInfo.InvariantCulture) + " plików osiągnięty" : ""),
                "",
                "Największe pliki:",
            };
            if (largest.Length == 0) lines.Add("   (brak plików)");
            foreach (var file in largest) lines.Add("   · " + FormatBytes(file.Length) + " — " + file.Path);
            lines.Add("");
            lines.Add("Puste pliki (0 B): " + empty.Length);
            foreach (var file in empty.Take(MaxListedPaths)) lines.Add("   · " + file.Path);
            lines.Add("");
            lines.Add("Duplikaty: " + groups.Count + " grup, ok. " + FormatBytes(reclaimable) + " do odzyskania (szczegóły: „duplikaty: " + opened + "”).");
            lines.Add("");
            lines.Add("Najstarsze pliki:");
            foreach (var file in oldest) lines.Add("   · " + file.LastWriteUtc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " — " + file.Path);
            lines.Add("");
            lines.Add("Raport jest tylko do odczytu — nic nie został zmieniony ani usunięty.");
            string text = string.Join(Environment.NewLine, lines);
            history.AddResult(id, "FILE_SCAN_TIDY", "porzadki: " + opened,
                ActionExecutionResult.VerifiedSuccess(text, $"Katalog: {opened}; plików: {scan.Files.Count}; odczyt {DateTime.Now:O}"));
            return text;
        }
        catch (OperationCanceledException) { history.AddCancelled(id, "FILE_SCAN_TIDY", "porzadki: " + opened, "Skanowanie przerwane."); throw; }
    }

    // ---------- Recycle-Bin deletion (two-step) ----------

    private string ProposeRecycle(string rawPath)
    {
        string path;
        try { path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(rawPath)); }
        catch (Exception ex) when (ex is ArgumentException or System.Security.SecurityException or NotSupportedException or PathTooLongException)
        { return "Nie rozumiem tej ścieżki: " + ex.Message; }
        var info = new FileInfo(path);
        if (!info.Exists) return "Taki plik nie istnieje: " + path;
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) return "To dowiązanie (junction/symlink) — nie usuwam dowiązań.";
        pending = new PendingDeletion(path, info.Length);
        return "KOSZ • Gotowy do usunięcia:\n" + path + " (" + FormatBytes(info.Length) + ")"
            + "\nPlik trafi do Kosza Windows — można go stamtąd przywrócić. Napisz „potwierdz”, aby usunąć, albo „anuluj”.";
    }

    private string Recycle(PendingDeletion item, string command)
    {
        string id = history.CreateActionId();
        history.AddRunning(id, "FILE_RECYCLE", command);
        try
        {
            if (!File.Exists(item.Path))
            {
                history.AddResult(id, "FILE_RECYCLE", command, ActionExecutionResult.Failure("Plik już nie istnieje: " + item.Path));
                return "FAILED • " + id + "\nPlik już nie istnieje: " + item.Path;
            }
            if (!recycler.TryRecycle(item.Path, out string error))
            {
                history.AddResult(id, "FILE_RECYCLE", command, ActionExecutionResult.Failure("Kosz Windows odmówił: " + error));
                return "FAILED • " + id + "\nKosz Windows odmówił: " + error;
            }
            if (File.Exists(item.Path))
            {
                history.AddResult(id, "FILE_RECYCLE", command, ActionExecutionResult.Failure("Plik nadal istnieje po operacji Kosza."));
                return "FAILED • " + id + "\nPlik nadal istnieje — operacja Kosza nie została potwierdzona.";
            }
            var result = ActionExecutionResult.VerifiedSuccess("Plik przeniesiony do Kosza.",
                item.Path + " (" + FormatBytes(item.Size) + ")\nUsunięcie można cofnąć w Koszu Windows.");
            history.AddResult(id, "FILE_RECYCLE", command, result);
            return "VERIFIED • " + id + "\n" + result.Message + "\n" + result.Evidence;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            history.AddResult(id, "FILE_RECYCLE", command, ActionExecutionResult.Failure(ex.Message));
            return "FAILED • " + id + "\n" + ex.Message;
        }
    }

    internal static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes; int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return (unit == 0 ? value.ToString("0", CultureInfo.InvariantCulture) : value.ToString("0.#", CultureInfo.InvariantCulture)) + " " + units[unit];
    }
}
