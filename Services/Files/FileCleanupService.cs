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
    private static readonly Regex CleanDuplicates = new(@"^(?:usuń duplikaty|usun duplikaty|wyczysc duplikaty|wyczyść duplikaty)(?::\s*|\s+|w\s+)(?<dir>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CleanEmpty = new(@"^(?:usuń puste pliki|usun puste pliki|wyrzuć puste pliki|wyrzuc puste pliki|usun puste|usuń puste)(?::\s*|\s+|w\s+)(?<dir>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RenameBatch = new(@"^(?:zmień nazwy|zmien nazwy|zmień nazwy plików|zmien nazwy plikow|zmien nazwe plikow)(?::\s*|\s+)(?<rest>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // Compared against ConversationMemoryService.Normalize(input) — diacritics are already stripped there.
    private static readonly string[] Confirmations = ["tak", "potwierdz", "tak usun", "tak, usun", "usun to", "tak zmien", "ok"];
    private static readonly string[] Cancellations = ["nie", "anuluj", "stop", "nie usuwaj", "nie zmieniaj"];

    private readonly ActionHistoryService history;
    private readonly IFileRecycler recycler;
    private PendingOp? pending;

    /// <summary>One pending batch awaiting explicit consent. Confirm executes, anything else drops it.</summary>
    private abstract record PendingOp(string Label);
    private sealed record PendingRecycle(string Label, List<(string Path, long Size)> Files) : PendingOp(Label);
    private sealed record PendingRename(string Folder, List<(string From, string To)> Pairs, List<string> Skipped) : PendingOp("ZMIANA-NAZW");

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

        // A pending operation is one-shot: confirm executes it, cancel drops it,
        // anything else drops it too — a stale pending batch must never fire on a later „tak”.
        if (pending is { } waiting)
        {
            pending = null;
            if (Array.Exists(Confirmations, c => c == normalized))
                return waiting is PendingRename rename ? ExecuteRename(rename, command) : ExecuteRecycle((PendingRecycle)waiting, command);
            if (Array.Exists(Cancellations, c => c == normalized)) return "Anulowane — nic nie zostało zmienione.";
        }

        // Bare commands without an argument: show usage instead of falling through to the model.
        if (normalized is "duplikaty" or "znajdz duplikaty" or "porzadki" or "raport porzadkowy" or "plan porzadkow" or "duplikaty:" or "porzadki:")
            return "Podaj folder, np. „duplikaty: C:\\Dane” albo „porzadki: C:\\Dane”. Raport jest tylko do odczytu.";
        if (normalized is "usun do kosza" or "usun do kosza:")
            return "Podaj pełną ścieżkę pliku, np. „usuń do kosza: C:\\Dane\\stary raport.txt”. Foldery zostawiam w spokoju.";
        if (normalized is "usun duplikaty" or "usun duplikaty:")
            return "Podaj folder, np. „usuń duplikaty: C:\\Dane”. Z każdej grupy duplikatów zostawię 1 plik, resztę zaproponuję do Kosza.";
        if (normalized is "usun puste pliki" or "usun puste pliki:")
            return "Podaj folder, np. „usuń puste pliki: C:\\Dane”. Pliki o rozmiarze 0 B zaproponuję do Kosza.";
        if (normalized is "zmien nazwy" or "zmien nazwy:" or "zmien nazwy plikow")
            return "Użyj: „zmien nazwy: C:\\Dane zamien IMG_ na wakacje_”. Najpierw pokażę listę zmian, nic nie zmienię bez „potwierdz”.";

        var dup = Duplicates.Match(command.Trim());
        if (dup.Success) return await DuplicatesReportAsync(CleanDir(dup.Groups["dir"].Value), token);
        var tidy = TidyReport.Match(command.Trim());
        if (tidy.Success) return await TidyReportAsync(CleanDir(tidy.Groups["dir"].Value), token);
        var cleanDup = CleanDuplicates.Match(command.Trim());
        if (cleanDup.Success) return await ProposeDuplicateCleanupAsync(CleanDir(cleanDup.Groups["dir"].Value), token);
        var cleanEmpty = CleanEmpty.Match(command.Trim());
        if (cleanEmpty.Success) return await ProposeEmptyCleanupAsync(CleanDir(cleanEmpty.Groups["dir"].Value), token);
        var rename = RenameBatch.Match(command.Trim());
        if (rename.Success) return ProposeRename(CleanDir(rename.Groups["rest"].Value));
        var recycle = RecycleFile.Match(command.Trim());
        if (recycle.Success) return ProposeRecycle(recycle.Groups["path"].Value.Trim().Trim('"'));
        return null;
    }

    private static string CleanDir(string raw)
    {
        string dir = raw.Trim().Trim('"');
        foreach (string lead in new[] { "folderze ", "folderu ", "katalogu ", "katalog " })
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

    // ---------- Recycle-Bin deletion (two-step, single file or duplicate batch) ----------

    private string ProposeRecycle(string rawPath)
    {
        string path;
        try { path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(rawPath)); }
        catch (Exception ex) when (ex is ArgumentException or System.Security.SecurityException or NotSupportedException or PathTooLongException)
        { return "Nie rozumiem tej ścieżki: " + ex.Message; }
        var info = new FileInfo(path);
        if (!info.Exists) return "Taki plik nie istnieje: " + path;
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) return "To dowiązanie (junction/symlink) — nie usuwam dowiązań.";
        pending = new PendingRecycle("KOSZ", [(path, info.Length)]);
        return "KOSZ • Gotowy do usunięcia:\n" + path + " (" + FormatBytes(info.Length) + ")"
            + "\nPlik trafi do Kosza Windows — można go stamtąd przywrócić. Napisz „potwierdz”, aby usunąć, albo „anuluj”.";
    }

    /// <summary>Proposes recycling the redundant copies of every duplicate group. Exactly one file per
    /// group is kept (the shortest path — usually the original), everything else is listed for consent.</summary>
    private async Task<string> ProposeDuplicateCleanupAsync(string directory, CancellationToken token)
    {
        string opened = TryOpenDirectory(directory);
        if (!Directory.Exists(opened)) return opened;
        var scan = await ScanAsync(opened, token);
        var (groups, reclaimable, _, _) = await FindDuplicatesAsync(scan, token);
        if (groups.Count == 0) return "Brak duplikatów w " + opened + " — nie ma czego sprzątać.";
        var victims = new List<(string Path, long Size)>();
        foreach (var group in groups)
        {
            string keep = group.Paths.OrderBy(p => p.Length).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).First();
            foreach (string path in group.Paths.Where(p => p != keep)) victims.Add((path, group.Size));
        }
        pending = new PendingRecycle("KOSZ-DUPLIKATY", victims);
        var lines = new List<string>
        {
            "KOSZ-DUPLIKATY • " + opened,
            "W każdej grupie duplikatów zostawiam 1 plik (najkrótsza ścieżka), resztę przeniosę do Kosza:",
        };
        foreach (var (path, size) in victims.Take(MaxListedPaths)) lines.Add("   · " + path + " (" + FormatBytes(size) + ")");
        if (victims.Count > MaxListedPaths) lines.Add("   · …i " + (victims.Count - MaxListedPaths) + " kolejnych");
        lines.Add("");
        lines.Add("Razem: " + victims.Count + " plików, ok. " + FormatBytes(reclaimable) + " do odzyskania. Napisz „potwierdz”, aby przenieść je do Kosza, albo „anuluj”.");
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Proposes recycling every empty (0 B) file directly inside the folder and its subfolders.</summary>
    private async Task<string> ProposeEmptyCleanupAsync(string directory, CancellationToken token)
    {
        string opened = TryOpenDirectory(directory);
        if (!Directory.Exists(opened)) return opened;
        var scan = await ScanAsync(opened, token);
        var victims = scan.Files.Where(f => f.Length == 0).Select(f => (f.Path, f.Length)).ToList();
        if (victims.Count == 0) return "Brak pustych plików (0 B) w " + opened + " — nie ma czego sprzątać.";
        pending = new PendingRecycle("KOSZ-PUSTE", victims);
        var lines = new List<string>
        {
            "KOSZ-PUSTE • " + opened,
            "Pliki o rozmiarze 0 B przeniosę do Kosza:",
        };
        foreach (var (path, _) in victims.Take(MaxListedPaths)) lines.Add("   · " + path);
        if (victims.Count > MaxListedPaths) lines.Add("   · …i " + (victims.Count - MaxListedPaths) + " kolejnych");
        lines.Add("");
        lines.Add("Razem: " + victims.Count + " pustych plików. Napisz „potwierdz”, aby je przenieść do Kosza, albo „anuluj”.");
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Proposes a bulk rename of direct children of one folder: replace one text fragment with
    /// another in file names. Nothing changes before consent; collisions and unsafe results are skipped.</summary>
    private string ProposeRename(string rest)
    {
        int zamienIdx = rest.LastIndexOf(" zamien ", StringComparison.OrdinalIgnoreCase);
        if (zamienIdx <= 0)
            return "Użyj: „zmien nazwy: C:\\Dane zamien IMG_ na wakacje_” — najpierw pokażę listę zmian.";
        string dirPart = CleanDir(rest[..zamienIdx]);
        string opened = TryOpenDirectory(dirPart);
        if (!Directory.Exists(opened)) return opened;
        string after = rest[(zamienIdx + " zamien ".Length)..];
        int naIdx = after.LastIndexOf(" na ", StringComparison.OrdinalIgnoreCase);
        if (naIdx <= 0)
            return "Nie widzę, na co zmienić. Użyj: „… zamien IMG_ na wakacje_”.";
        string from = after[..naIdx].Trim().Trim('"');
        string to = after[(naIdx + " na ".Length)..].Trim().Trim('"');
        if (from.Length == 0) return "Podaj tekst do zamiany, np. „… zamien IMG_ na wakacje_”.";

        var pairs = new List<(string From, string To)>();
        var skipped = new List<string>();
        foreach (var info in new DirectoryInfo(opened).EnumerateFiles())
        {
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            string name = info.Name;
            if (name.IndexOf(from, StringComparison.OrdinalIgnoreCase) < 0) continue;
            string newName = name.Replace(from, to, StringComparison.OrdinalIgnoreCase);
            if (newName == name) continue;
            if (!FileWorkspaceService.IsSafeName(newName)) { skipped.Add(name + " → " + newName + " (wynikowa nazwa nie jest bezpieczna)"); continue; }
            string target = Path.Combine(opened, newName);
            if (File.Exists(target)) { skipped.Add(name + " → " + newName + " (taka nazwa już istnieje)"); continue; }
            pairs.Add((info.FullName, target));
        }
        if (pairs.Count == 0)
            return "Brak plików do zmiany" + (skipped.Count > 0 ? " — pominięte: " + string.Join("; ", skipped) : " — żaden plik bezpośrednio w " + opened + " nie zawiera „" + from + "”.");
        pending = new PendingRename(opened, pairs, skipped);
        var lines = new List<string>
        {
            "ZMIANA-NAZW • " + opened,
            "Zmienię nazwy " + pairs.Count + " plików (tylko bezpośrednio w tym folderze):",
        };
        foreach (var (fromPath, toPath) in pairs.Take(MaxReportedGroups)) lines.Add("   · " + Path.GetFileName(fromPath) + " → " + Path.GetFileName(toPath));
        if (pairs.Count > MaxReportedGroups) lines.Add("   · …i " + (pairs.Count - MaxReportedGroups) + " kolejnych");
        if (skipped.Count > 0) { lines.Add(""); lines.Add("Pominięte: " + string.Join("; ", skipped)); }
        lines.Add("");
        lines.Add("Napisz „potwierdz”, aby zmienić nazwy, albo „anuluj”. Zmianę nazwy można cofnąć ręcznie.");
        return string.Join(Environment.NewLine, lines);
    }

    private string ExecuteRename(PendingRename op, string command)
    {
        string id = history.CreateActionId();
        history.AddRunning(id, "FILE_RENAME", command);
        var done = new List<string>();
        var failed = new List<string>();
        foreach (var (from, to) in op.Pairs)
        {
            try
            {
                if (!File.Exists(from)) { failed.Add(Path.GetFileName(from) + " (plik już nie istnieje)"); continue; }
                if (File.Exists(to)) { failed.Add(Path.GetFileName(from) + " (nazwa docelowa właśnie powstała)"); continue; }
                File.Move(from, to, false);
                if (File.Exists(from) || !File.Exists(to)) { failed.Add(Path.GetFileName(from) + " (weryfikacja niepowiedziona)"); continue; }
                done.Add(Path.GetFileName(from) + " → " + Path.GetFileName(to));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed.Add(Path.GetFileName(from) + " (" + ex.Message + ")"); }
        }
        if (done.Count == 0)
        {
            history.AddResult(id, "FILE_RENAME", command, ActionExecutionResult.Failure("Żadna nazwa nie została zmieniona."));
            return "FAILED • " + id + "\nŻadna nazwa nie została zmieniona.\n" + string.Join("\n", failed.Select(f => "   · " + f));
        }
        string message = "Zmieniono nazwy: " + done.Count + (failed.Count > 0 ? ", pominięte: " + failed.Count : "") + ".";
        var result = ActionExecutionResult.VerifiedSuccess(message,
            string.Join("\n", done) + (failed.Count > 0 ? "\nPominięte:\n" + string.Join("\n", failed.Select(f => "   · " + f)) : ""));
        history.AddResult(id, "FILE_RENAME", command, failed.Count == 0 ? result : ActionExecutionResult.Failure(result.Message + " Część plików pominięta: " + failed.Count));
        return (failed.Count == 0 ? "VERIFIED • " : "PARTIAL • ") + id + "\n" + result.Message + "\n" + result.Evidence;
    }

    private string ExecuteRecycle(PendingRecycle batch, string command)
    {
        string id = history.CreateActionId();
        history.AddRunning(id, "FILE_RECYCLE", command);
        var done = new List<string>();
        var failed = new List<string>();
        foreach (var (path, size) in batch.Files)
        {
            try
            {
                if (!File.Exists(path)) { failed.Add(path + " (plik już nie istnieje)"); continue; }
                if (!recycler.TryRecycle(path, out string error)) { failed.Add(path + " (Kosz odmówił: " + error + ")"); continue; }
                if (File.Exists(path)) { failed.Add(path + " (plik nadal istnieje po operacji)"); continue; }
                done.Add(path + " (" + FormatBytes(size) + ")");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed.Add(path + " (" + ex.Message + ")"); }
        }
        if (done.Count == 0)
        {
            history.AddResult(id, "FILE_RECYCLE", command, ActionExecutionResult.Failure("Żaden plik nie został usunięty: " + string.Join("; ", failed)));
            return "FAILED • " + id + "\nŻaden plik nie został usunięty.\n" + string.Join("\n", failed.Select(f => "   · " + f));
        }
        string message = batch.Label == "KOSZ-DUPLIKATY"
            ? "Duplikaty przeniesione do Kosza: " + done.Count + (failed.Count > 0 ? ", pominięte: " + failed.Count : "") + "."
            : "Plik przeniesiony do Kosza.";
        var result = ActionExecutionResult.VerifiedSuccess(message,
            string.Join("\n", done) + (failed.Count > 0 ? "\nPominięte:\n" + string.Join("\n", failed.Select(f => "   · " + f)) : "")
            + "\nUsunięcie można cofnąć w Koszu Windows.");
        history.AddResult(id, "FILE_RECYCLE", command, failed.Count == 0 ? result : ActionExecutionResult.Failure(result.Message + " Część plików pominięta: " + failed.Count));
        return (failed.Count == 0 ? "VERIFIED • " : "PARTIAL • ") + id + "\n" + result.Message + "\n" + result.Evidence;
    }

    internal static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes; int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return (unit == 0 ? value.ToString("0", CultureInfo.InvariantCulture) : value.ToString("0.#", CultureInfo.InvariantCulture)) + " " + units[unit];
    }
}
