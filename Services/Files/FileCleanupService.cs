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

/// <summary>0.93 · executable tidy: batch rename with preview, empty-file cleanup, and duplicate batch
/// cleanup — all behind an explicit two-step confirmation. Reports stay read-only; the only mutating
/// operations are Recycle-Bin moves or file renames, and only after the user confirms the exact list.
/// Junctions/symlinks are never followed, hard caps keep everything bounded.</summary>
public sealed class FileCleanupService
{
    internal const int MaxEnumeratedFiles = 50_000;
    internal const long MaxHashedFileSize = 256L * 1024 * 1024;
    internal const int MaxReportedGroups = 25;
    internal const int MaxListedPaths = 10;
    internal const int MaxBatchRename = 200;
    internal const int MaxBatchDelete = 100;

    private static readonly Regex Duplicates = new(@"^(?:duplikaty|duplikatow|duplikatów|znajdź duplikaty|znajdz duplikaty)(?::\s*|\s+|w\s+)(?<dir>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TidyReport = new(@"^(?:porzadki|porządki|raport porzadkowy|raport porządkowy|plan porzadkow|plan porządków)(?::\s*|\s+|w\s+)(?<dir>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RecycleFile = new(@"^(?:usuń do kosza|usun do kosza|przenieś do kosza|przenies do kosza|wyrzuć do kosza|wyrzuc do kosza)(?::\s*|\s+)(?<path>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 0.93 · batch rename: „zmien nazwy: <folder> z <old> na <new>” and „...: <folder>: <old> -> <new>”
    private static readonly Regex BatchRenameZ = new(@"^(?:zmien nazwy|zmień nazwy|przemianuj|rename)(?: w)?:\s*(?<dir>.+?)\s+z\s+(?<from>.+?)\s+na\s+(?<to>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BatchRenameArrow = new(@"^(?:zmien nazwy|zmień nazwy|przemianuj)(?: w)?:\s*(?<dir>.+?)\s*:\s*(?<from>.+?)\s*->\s*(?<to>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 0.93 · executable tidy: „uporzadkuj: <folder>” and „wykonaj porzadki: <folder>”
    private static readonly Regex TidyExec = new(@"^(?:uporzadkuj|uporządkuj|posprzataj|posprzątaj|wykonaj porzadki|wykonaj porządki|wykonaj plan porzadkow|wykonaj plan porządków)(?::\s*|\s+)(?<dir>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 0.93 · batch duplicate cleanup: „usun duplikaty: <folder>”
    private static readonly Regex DuplicateCleanup = new(@"^(?:usun duplikaty|usuń duplikaty|wyczysc duplikaty|wyczyść duplikaty|usun duplikaty w|usuń duplikaty w)(?::\s*|\s+)(?<dir>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Compared against ConversationMemoryService.Normalize(input) — diacritics are already stripped there.
    private static readonly string[] Confirmations = ["tak", "potwierdz", "tak usun", "tak, usun", "usun to", "ok", "wykonaj", "tak, wykonaj"];
    private static readonly string[] Cancellations = ["nie", "anuluj", "stop", "nie usuwaj"];

    private readonly ActionHistoryService history;
    private readonly IFileRecycler recycler;
    private PendingOperation? pending;

    private abstract record PendingOperation;
    private sealed record PendingDeletion(string Path, long Size) : PendingOperation;
    private sealed record PendingBatchRename(string Directory, List<(string OldPath, string NewPath)> Renames) : PendingOperation;
    private sealed record PendingTidyCleanup(string Directory, List<string> EmptyFiles) : PendingOperation;
    private sealed record PendingDuplicateCleanup(string Directory, List<string> ToDelete, List<DuplicateGroup> Groups, long Reclaimable) : PendingOperation;

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

        // Any pending operation is one-shot: confirm executes it, cancel drops it, anything else drops it.
        if (pending is { } waiting)
        {
            if (Array.Exists(Confirmations, c => c == normalized))
            {
                var op = pending; pending = null;
                return op switch
                {
                    PendingDeletion d => Recycle(d, command),
                    PendingBatchRename r => ExecuteBatchRename(r, command),
                    PendingTidyCleanup t => await ExecuteTidyCleanupAsync(t, command, token),
                    PendingDuplicateCleanup dup => await ExecuteDuplicateCleanupAsync(dup, command, token),
                    _ => null
                };
            }
            if (Array.Exists(Cancellations, c => c == normalized)) { pending = null; return "Anulowane — nic nie zostało zmienione."; }
            pending = null;
        }

        // Bare commands without an argument: show usage instead of falling through to the model.
        if (normalized is "duplikaty" or "znajdz duplikaty" or "porzadki" or "raport porzadkowy" or "plan porzadkow" or "duplikaty:" or "porzadki:")
            return "Podaj folder, np. „duplikaty: C:\\Dane” albo „porzadki: C:\\Dane”. Raport jest tylko do odczytu.";
        if (normalized is "usun do kosza" or "usun do kosza:")
            return "Podaj pełną ścieżkę pliku, np. „usuń do kosza: C:\\Dane\\stary raport.txt”. Foldery zostawiam w spokoju.";
        if (normalized is "zmien nazwy" or "zmien nazwy:" or "przemianuj" or "rename")
            return "Podaj folder i zamianę, np. „zmien nazwy: C:\\Dane z IMG_ na zdjecie_” albo „zmien nazwy: C:\\Dane: stary -> nowy”. Pokażę podgląd i dopiero po „potwierdz” zmienię nazwy.";
        if (normalized is "uporzadkuj" or "uporzadkuj:" or "posprzataj" or "wykonaj porzadki" or "wykonaj porzadki:")
            return "Podaj folder, np. „uporzadkuj: C:\\Dane”. Zaproponuję usunięcie pustych plików (0 B) do Kosza — dopiero po „potwierdz”.";
        if (normalized is "usun duplikaty" or "usun duplikaty:" or "wyczysc duplikaty")
            return "Podaj folder, np. „usun duplikaty: C:\\Dane”. Pokażę, które duplikaty usunę (zachowam pierwszy z grupy) i dopiero po „potwierdz” przeniosę je do Kosza.";

        var dup = Duplicates.Match(command.Trim());
        if (dup.Success) return await DuplicatesReportAsync(CleanDir(dup.Groups["dir"].Value), token);
        var tidy = TidyReport.Match(command.Trim());
        if (tidy.Success) return await TidyReportAsync(CleanDir(tidy.Groups["dir"].Value), token);
        var recycle = RecycleFile.Match(command.Trim());
        if (recycle.Success) return ProposeRecycle(recycle.Groups["path"].Value.Trim().Trim('"'));

        var renameZ = BatchRenameZ.Match(command.Trim());
        if (renameZ.Success) return await ProposeBatchRenameAsync(CleanDir(renameZ.Groups["dir"].Value), renameZ.Groups["from"].Value.Trim().Trim('"'), renameZ.Groups["to"].Value.Trim().Trim('"'), token);
        var renameArrow = BatchRenameArrow.Match(command.Trim());
        if (renameArrow.Success) return await ProposeBatchRenameAsync(CleanDir(renameArrow.Groups["dir"].Value), renameArrow.Groups["from"].Value.Trim().Trim('"'), renameArrow.Groups["to"].Value.Trim().Trim('"'), token);

        var execTidy = TidyExec.Match(command.Trim());
        if (execTidy.Success) return await ProposeTidyCleanupAsync(CleanDir(execTidy.Groups["dir"].Value), token);

        var dupClean = DuplicateCleanup.Match(command.Trim());
        if (dupClean.Success) return await ProposeDuplicateCleanupAsync(CleanDir(dupClean.Groups["dir"].Value), token);

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

    private static async Task<List<ScannedFile>> ScanTopLevelAsync(string directory, CancellationToken token)
    {
        var files = new List<ScannedFile>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Directory | FileAttributes.ReparsePoint,
        };
        foreach (string path in Directory.EnumerateFiles(directory, "*", options))
        {
            token.ThrowIfCancellationRequested();
            if (files.Count >= MaxBatchRename) break;
            try
            {
                var info = new FileInfo(path);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                files.Add(new ScannedFile(path, info.Length, info.LastWriteTimeUtc));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return files;
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
        groups.Sort((a, b) => b.Size * (b.Paths.Count - 1)).CompareTo(a.Size * (a.Paths.Count - 1)));
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
            lines.Add("Wiele duplikatów naraz: „usun duplikaty: " + opened + "” — pokażę plan i poproszę o „potwierdz”.");
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
            if (empty.Length > 0) lines.Add("Puste pliki możesz usunąć: „uporzadkuj: " + opened + "” — pokażę listę i poproszę o „potwierdz”.");
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

    // ---------- 0.93 · batch rename ----------

    private async Task<string> ProposeBatchRenameAsync(string directory, string from, string to, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(from)) return "Podaj, co zmienić w nazwie — np. „zmien nazwy: C:\\Dane z IMG_ na zdjecie_”.";
        if (from.Length > 100 || to.Length > 100) return "Frazy zamiany są za długie (maks. 100 znaków).";
        if (from.Contains('/') || from.Contains('\\') || to.Contains('/') || to.Contains('\\'))
            return "Zamiana dotyczy nazw plików, nie ścieżek — bez ukośników.";
        if (to.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "Nowa fraza zawiera niedozwolone znaki w nazwie pliku.";
        if (from.Equals(to, StringComparison.OrdinalIgnoreCase))
            return "Stara i nowa fraza są identyczne — nic do zmiany.";

        string opened = TryOpenDirectory(directory);
        if (!Directory.Exists(opened)) return opened;

        string id = history.CreateActionId();
        history.AddRunning(id, "FILE_BATCH_RENAME_PREVIEW", $"zmien nazwy: {opened} z {from} na {to}");

        try
        {
            var files = await ScanTopLevelAsync(opened, token);
            var renames = new List<(string OldPath, string NewPath)>();
            var skipped = new List<string>();
            int collisions = 0, invalid = 0;

            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                string fileName = Path.GetFileName(file.Path);
                if (!fileName.Contains(from, StringComparison.OrdinalIgnoreCase)) continue;
                string newFileName = fileName.Replace(from, to, StringComparison.OrdinalIgnoreCase);
                if (newFileName == fileName) continue;
                if (string.IsNullOrWhiteSpace(newFileName) || newFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                { invalid++; skipped.Add(fileName + " → (nieprawidłowa nazwa)"); continue; }
                string newPath = Path.Combine(opened, newFileName);
                if (File.Exists(newPath) || Directory.Exists(newPath))
                { collisions++; skipped.Add(fileName + " → " + newFileName + " (już istnieje)"); continue; }
                renames.Add((file.Path, newPath));
                if (renames.Count >= MaxBatchRename) break;
            }

            if (renames.Count == 0)
            {
                string reason = collisions > 0 || invalid > 0 ? $" Nic do zmiany — kolizje: {collisions}, nieprawidłowe: {invalid}." : " Żaden plik nie zawiera tej frazy.";
                history.AddResult(id, "FILE_BATCH_RENAME_PREVIEW", $"zmien nazwy: {opened} z {from} na {to}",
                    ActionExecutionResult.VerifiedSuccess("Brak plików do przemianowania." + reason, $"Katalog: {opened}; fraza: {from}"));
                return "Brak plików do przemianowania w: " + opened + "\nSzukano: „" + from + "” → „" + to + "”." + reason;
            }

            pending = new PendingBatchRename(opened, renames);
            var lines = new List<string>
            {
                $"ZMIANA NAZW • {opened}",
                $"Zamiana: „{from}” → „{to}” — znaleziono {renames.Count} plików (limit {MaxBatchRename}).",
                collisions > 0 || invalid > 0 ? $"Pominięte: kolizje {collisions}, nieprawidłowe {invalid}." : "",
                "",
                "Podgląd (max 20):"
            };
            foreach (var (oldPath, newPath) in renames.Take(20))
                lines.Add($"  · {Path.GetFileName(oldPath)} → {Path.GetFileName(newPath)}");
            if (renames.Count > 20) lines.Add($"  · …i {renames.Count - 20} kolejnych");
            if (skipped.Count > 0)
            {
                lines.Add("");
                lines.Add("Pominięte (max 10):");
                foreach (var s in skipped.Take(10)) lines.Add("  · " + s);
            }
            lines.Add("");
            lines.Add("To jest podgląd — nic nie zostało zmienione. Napisz „potwierdz”, aby wykonać, albo „anuluj”.");
            string text = string.Join(Environment.NewLine, lines.Where(l => l != null && l.Length > 0));
            history.AddResult(id, "FILE_BATCH_RENAME_PREVIEW", $"zmien nazwy: {opened} z {from} na {to}",
                ActionExecutionResult.VerifiedSuccess(text, $"Katalog: {opened}; zmian: {renames.Count}; odczyt {DateTime.Now:O}"));
            return text;
        }
        catch (OperationCanceledException) { history.AddCancelled(id, "FILE_BATCH_RENAME_PREVIEW", $"zmien nazwy: {directory}", "Skanowanie przerwane."); throw; }
    }

    private string ExecuteBatchRename(PendingBatchRename pendingRename, string command)
    {
        string id = history.CreateActionId();
        history.AddRunning(id, "FILE_BATCH_RENAME", command);
        int ok = 0, failed = 0;
        var errors = new List<string>();
        foreach (var (oldPath, newPath) in pendingRename.Renames)
        {
            try
            {
                if (!File.Exists(oldPath)) { failed++; errors.Add(Path.GetFileName(oldPath) + ": już nie istnieje"); continue; }
                if (File.Exists(newPath)) { failed++; errors.Add(Path.GetFileName(newPath) + ": cel już istnieje"); continue; }
                File.Move(oldPath, newPath);
                if (File.Exists(newPath) && !File.Exists(oldPath)) ok++;
                else { failed++; errors.Add(Path.GetFileName(oldPath) + ": przeniesienie niepotwierdzone"); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { failed++; errors.Add(Path.GetFileName(oldPath) + ": " + ex.Message); }
        }

        if (ok > 0 && failed == 0)
        {
            var result = ActionExecutionResult.VerifiedSuccess($"Zmieniono nazwy {ok} plików w {pendingRename.Directory}.",
                $"Katalog: {pendingRename.Directory}; zmian: {ok}; odczyt {DateTime.Now:O}");
            history.AddResult(id, "FILE_BATCH_RENAME", command, result);
            return $"VERIFIED • {id}\n{result.Message}\n{result.Evidence}";
        }
        if (ok > 0)
        {
            var result = ActionExecutionResult.VerifiedSuccess($"Zmieniono nazwy {ok} plików, {failed} nie udało się w {pendingRename.Directory}.",
                $"Katalog: {pendingRename.Directory}; ok: {ok}; failed: {failed}; błędy: {string.Join("; ", errors.Take(5))}");
            history.AddResult(id, "FILE_BATCH_RENAME", command, result);
            return $"VERIFIED • {id}\n{result.Message}\nBłędy: {string.Join("\n", errors.Take(10))}";
        }
        history.AddResult(id, "FILE_BATCH_RENAME", command, ActionExecutionResult.Failure("Nie zmieniono żadnej nazwy: " + string.Join("; ", errors.Take(5))));
        return $"FAILED • {id}\nNie zmieniono żadnej nazwy.\nBłędy: {string.Join("\n", errors.Take(10))}";
    }

    // ---------- 0.93 · executable tidy (empty files) ----------

    private async Task<string> ProposeTidyCleanupAsync(string directory, CancellationToken token)
    {
        string opened = TryOpenDirectory(directory);
        if (!Directory.Exists(opened)) return opened;
        string id = history.CreateActionId();
        history.AddRunning(id, "FILE_TIDY_EXEC_PREVIEW", "uporzadkuj: " + opened);
        try
        {
            var scan = await ScanAsync(opened, token);
            var empty = scan.Files.Where(f => f.Length == 0).Select(f => f.Path).ToList();
            if (empty.Count == 0)
            {
                history.AddResult(id, "FILE_TIDY_EXEC_PREVIEW", "uporzadkuj: " + opened,
                    ActionExecutionResult.VerifiedSuccess("Brak pustych plików (0 B) — nic do porządkowania.", $"Katalog: {opened}; plików: {scan.Files.Count}"));
                return $"Brak pustych plików w: {opened}\nPrzeskanowano {scan.Files.Count} plików — wszystkie mają zawartość.";
            }

            var toDelete = empty.Take(MaxBatchDelete).ToList();
            pending = new PendingTidyCleanup(opened, toDelete);
            var lines = new List<string>
            {
                $"PORZĄDKOWANIE • {opened}",
                $"Pustych plików (0 B): {empty.Count} — do Kosza trafi {toDelete.Count} (limit {MaxBatchDelete}).",
                "",
                "Podgląd (max 20):"
            };
            foreach (var p in toDelete.Take(20)) lines.Add("  · " + p);
            if (toDelete.Count > 20) lines.Add($"  · …i {toDelete.Count - 20} kolejnych");
            lines.Add("");
            lines.Add("Pliki trafią do Kosza Windows — można je przywrócić. Napisz „potwierdz”, aby wykonać, albo „anuluj”.");
            string text = string.Join(Environment.NewLine, lines);
            history.AddResult(id, "FILE_TIDY_EXEC_PREVIEW", "uporzadkuj: " + opened,
                ActionExecutionResult.VerifiedSuccess(text, $"Katalog: {opened}; pustych: {empty.Count}; do usunięcia: {toDelete.Count}"));
            return text;
        }
        catch (OperationCanceledException) { history.AddCancelled(id, "FILE_TIDY_EXEC_PREVIEW", "uporzadkuj: " + opened, "Skanowanie przerwane."); throw; }
    }

    private async Task<string> ExecuteTidyCleanupAsync(PendingTidyCleanup pendingTidy, string command, CancellationToken token)
    {
        string id = history.CreateActionId();
        history.AddRunning(id, "FILE_TIDY_EXEC", command);
        int ok = 0, failed = 0;
        var errors = new List<string>();
        foreach (var path in pendingTidy.EmptyFiles)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (!File.Exists(path)) { failed++; continue; }
                var info = new FileInfo(path);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) { failed++; errors.Add(path + ": dowiązanie"); continue; }
                if (!recycler.TryRecycle(path, out string err)) { failed++; errors.Add(Path.GetFileName(path) + ": " + err); continue; }
                if (!File.Exists(path)) ok++; else { failed++; errors.Add(Path.GetFileName(path) + ": nadal istnieje"); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed++; errors.Add(Path.GetFileName(path) + ": " + ex.Message); }
        }

        if (ok > 0)
        {
            var result = ActionExecutionResult.VerifiedSuccess($"Uporządkowano: {ok} pustych plików przeniesiono do Kosza w {pendingTidy.Directory}{(failed > 0 ? $", {failed} nie udało się" : "")}.",
                $"Katalog: {pendingTidy.Directory}; ok: {ok}; failed: {failed}");
            history.AddResult(id, "FILE_TIDY_EXEC", command, result);
            return $"VERIFIED • {id}\n{result.Message}\n{(errors.Count > 0 ? "Błędy: " + string.Join("\n", errors.Take(10)) : "")}".TrimEnd();
        }
        history.AddResult(id, "FILE_TIDY_EXEC", command, ActionExecutionResult.Failure("Nie usunięto pustych plików: " + string.Join("; ", errors.Take(5))));
        return $"FAILED • {id}\nNie usunięto pustych plików.\nBłędy: {string.Join("\n", errors.Take(10))}";
    }

    // ---------- 0.93 · batch duplicate cleanup ----------

    private async Task<string> ProposeDuplicateCleanupAsync(string directory, CancellationToken token)
    {
        string opened = TryOpenDirectory(directory);
        if (!Directory.Exists(opened)) return opened;
        string id = history.CreateActionId();
        history.AddRunning(id, "FILE_DUPLICATES_CLEANUP_PREVIEW", "usun duplikaty: " + opened);
        try
        {
            var scan = await ScanAsync(opened, token);
            var (groups, reclaimable, tooLarge, inaccessible) = await FindDuplicatesAsync(scan, token);
            if (groups.Count == 0)
            {
                history.AddResult(id, "FILE_DUPLICATES_CLEANUP_PREVIEW", "usun duplikaty: " + opened,
                    ActionExecutionResult.VerifiedSuccess("Brak duplikatów — nic do usuwania.", $"Katalog: {opened}"));
                return $"Brak duplikatów w: {opened}\nPrzeskanowano {scan.Files.Count} plików — każda treść występuje raz.";
            }

            // Keep first alphabetically per group, delete rest.
            var toDelete = new List<string>();
            foreach (var group in groups)
            {
                var sorted = group.Paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
                // keep first, delete rest
                foreach (var p in sorted.Skip(1))
                {
                    if (toDelete.Count >= MaxBatchDelete) break;
                    toDelete.Add(p);
                }
                if (toDelete.Count >= MaxBatchDelete) break;
            }

            if (toDelete.Count == 0)
            {
                history.AddResult(id, "FILE_DUPLICATES_CLEANUP_PREVIEW", "usun duplikaty: " + opened,
                    ActionExecutionResult.VerifiedSuccess("Duplikaty istnieją, ale lista do usunięcia jest pusta.", $"Katalog: {opened}; grup: {groups.Count}"));
                return $"Duplikaty w {opened}: {groups.Count} grup, ale nic nie zakwalifikowało się do usunięcia (limit {MaxBatchDelete}).";
            }

            pending = new PendingDuplicateCleanup(opened, toDelete, groups, reclaimable);
            var lines = new List<string>
            {
                $"USUWANIE DUPLIKATÓW • {opened}",
                $"Grup duplikatów: {groups.Count} — można odzyskać ok. {FormatBytes(reclaimable)}.",
                $"Do Kosza trafi {toDelete.Count} plików (zachowam pierwszy z każdej grupy, limit {MaxBatchDelete})."
                    + (tooLarge > 0 ? $" Pominięte >256 MB: {tooLarge}." : "")
                    + (inaccessible > 0 ? $" Niedostępne: {inaccessible}." : ""),
                "",
                "Podgląd grup (max 10 grup, max 3 usuwane na grupę):"
            };
            int shownGroups = 0;
            foreach (var group in groups.Take(10))
            {
                shownGroups++;
                var sorted = group.Paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
                lines.Add("");
                lines.Add($"{shownGroups}. {Path.GetFileName(sorted[0])} × {group.Paths.Count} ({FormatBytes(group.Size)} każdy) — zachowam:");
                lines.Add($"   ✔ {sorted[0]}");
                lines.Add("   Usunę:");
                foreach (var p in sorted.Skip(1).Take(3)) lines.Add($"   · {p}");
                if (sorted.Count - 1 > 3) lines.Add($"   · …i {sorted.Count - 4} kolejnych w tej grupie");
            }
            if (groups.Count > 10) lines.Add($"\n…oraz {groups.Count - 10} dalszych grup (limit podglądu 10).");
            lines.Add("");
            lines.Add("Pliki trafią do Kosza Windows — można je przywrócić. Napisz „potwierdz”, aby wykonać, albo „anuluj”.");
            string text = string.Join(Environment.NewLine, lines);
            history.AddResult(id, "FILE_DUPLICATES_CLEANUP_PREVIEW", "usun duplikaty: " + opened,
                ActionExecutionResult.VerifiedSuccess(text, $"Katalog: {opened}; grup: {groups.Count}; do usunięcia: {toDelete.Count}; odzyskiwalne: {reclaimable}"));
            return text;
        }
        catch (OperationCanceledException) { history.AddCancelled(id, "FILE_DUPLICATES_CLEANUP_PREVIEW", "usun duplikaty: " + opened, "Skanowanie przerwane."); throw; }
    }

    private async Task<string> ExecuteDuplicateCleanupAsync(PendingDuplicateCleanup pendingDup, string command, CancellationToken token)
    {
        string id = history.CreateActionId();
        history.AddRunning(id, "FILE_DUPLICATES_CLEANUP", command);
        int ok = 0, failed = 0;
        var errors = new List<string>();
        long reclaimed = 0;
        foreach (var path in pendingDup.ToDelete)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (!File.Exists(path)) { failed++; continue; }
                var info = new FileInfo(path);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) { failed++; errors.Add(path + ": dowiązanie"); continue; }
                long size = info.Length;
                if (!recycler.TryRecycle(path, out string err)) { failed++; errors.Add(Path.GetFileName(path) + ": " + err); continue; }
                if (!File.Exists(path)) { ok++; reclaimed += size; }
                else { failed++; errors.Add(Path.GetFileName(path) + ": nadal istnieje"); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed++; errors.Add(Path.GetFileName(path) + ": " + ex.Message); }
        }

        if (ok > 0)
        {
            var result = ActionExecutionResult.VerifiedSuccess($"Usunięto duplikaty: {ok} plików ({FormatBytes(reclaimed)}) przeniesiono do Kosza w {pendingDup.Directory}{(failed > 0 ? $", {failed} nie udało się" : "")}.",
                $"Katalog: {pendingDup.Directory}; ok: {ok}; failed: {failed}; odzyskane: {reclaimed}");
            history.AddResult(id, "FILE_DUPLICATES_CLEANUP", command, result);
            return $"VERIFIED • {id}\n{result.Message}\n{result.Evidence}{(errors.Count > 0 ? "\nBłędy: " + string.Join("\n", errors.Take(10)) : "")}".TrimEnd();
        }
        history.AddResult(id, "FILE_DUPLICATES_CLEANUP", command, ActionExecutionResult.Failure("Nie usunięto duplikatów: " + string.Join("; ", errors.Take(5))));
        return $"FAILED • {id}\nNie usunięto duplikatów.\nBłędy: {string.Join("\n", errors.Take(10))}";
    }

    internal static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes; int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return (unit == 0 ? value.ToString("0", CultureInfo.InvariantCulture) : value.ToString("0.#", CultureInfo.InvariantCulture)) + " " + units[unit];
    }
}
