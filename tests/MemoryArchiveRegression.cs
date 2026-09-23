using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace SentinelX.Tests;

/// <summary>Monthly conversation archive in its own folder, honest reporting, and the cross-module
/// insights (statistics, unified search, briefing, verified local backup).</summary>
internal static class MemoryArchiveRegression
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    public static Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        string memoryDirectory = Path.Combine(directory, "memory");
        string archiveDirectory = Path.Combine(directory, "archives");
        var memory = new ConversationMemoryService(memoryDirectory);
        memory.AddUserMessage("Stare pytanie o dysk ZXCVBNM.", "TEST");
        memory.AddAssistantMessage("Dysk jest w porządku ZXCVBNM.");
        Check(memory.AddNote("Wspomnienie, które musi przetrwać archiwizację ZXCVBNM") == NoteAddResult.Added, "the fixture note must be added");
        string month = DateTime.Now.ToString("yyyy-MM");

        // The default archive window is "the month before now", so the clock is shifted by one month
        // to make the freshly recorded month the archivable one.
        var archives = new MemoryArchiveService(memory, archiveDirectory) { NowProvider = () => DateTime.Now.AddMonths(1) };
        var result = archives.ArchiveMonth();
        Check(result.Success && result.Summary != null, "archiving must succeed: " + result.Message);
        Check(result.Summary!.Turns == 2, "both turns of the month must be archived, got " + result.Summary.Turns);
        Check(result.Summary.Conversations == 1, "the conversation count must be reported");
        Check(result.Summary.Month == month, "the archived month must be the one before the shifted clock");
        Check(archiveDirectory == archives.ArchiveRoot && Directory.Exists(archiveDirectory), "the archive must live in its own folder: " + archives.ArchiveRoot);
        Check(File.Exists(result.Summary.JsonPath) && File.Exists(result.Summary.MarkdownPath), "JSON and readable Markdown must both exist");
        byte[] stored = File.ReadAllBytes(result.Summary.JsonPath);
        Check(Convert.ToHexString(SHA256.HashData(stored)) == result.Summary.Sha256, "the reported hash must match the file on disk");
        Check(File.ReadAllText(result.Summary.MarkdownPath).Contains("ZXCVBNM"), "the Markdown archive must contain the conversation text");
        Check(memory.GetAllEntries().Count == 0, "archived turns must leave the live store");
        Check(memory.NoteCount == 1, "explicit memories must survive archiving");
        Check(result.Message.Contains("Wspomnienia i profil nie zostały ruszone"), "the result must state what was not touched");

        // Idempotency and honest reporting.
        Check(archives.ArchiveMonth().Message.Contains("Brak rozmów"), "a second run must report nothing to archive");
        Check(!archives.ArchiveMonth("2026-13").Success, "an impossible month must be refused");
        Check(archives.ArchiveMonth("zzzz").Message.Contains("RRRR-MM"), "a malformed month must explain the expected format");
        Check(archives.Describe().Contains(month), "the archive list must name the month");
        Check(archives.List().Single().Sha256.Length == 64, "the list must recompute the archive hash from disk");

        // Retention window: 0 keeps everything; 1 keeps the previous month live.
        memory.AddUserMessage("Nowa rozmowa po archiwizacji ZXCVBNM.", "TEST");
        Check(archives.ArchiveDue(0).Count == 0, "0 months must disable automatic archiving");
        Check(archives.ArchiveDue(1).Count == 0, "a month inside the window must stay live");
        archives.NowProvider = () => DateTime.Now.AddMonths(2);
        var due = archives.ArchiveDue(1);
        Check(due.Count == 1 && due[0].Turns == 1, "a month outside the window must be archived automatically, got " + due.Count);
        Check(memory.GetAllEntries().Count == 0, "the automatically archived turn must leave the live store");
        Check(memory.NoteCount == 1, "explicit memories must survive automatic archiving");

        // Private mode: nothing was stored, so nothing may be written or deleted.
        memory.SetPrivateMode(true);
        var privateResult = archives.ArchiveMonth();
        Check(!privateResult.Success && privateResult.Message.Contains("Tryb prywatny"), "private mode must refuse archiving and say why");
        memory.SetPrivateMode(false);
        Check(archives.ArchiveMonth().Success, "after private mode archiving works again");

        // Deletion touches only the archive folder.
        Check(archives.DeleteArchive(month, out _), "the archive must be deletable");
        Check(archives.List().Count == 0, "the archive list must be empty after deletion");
        Check(!archives.DeleteArchive("1999-01", out string missing) && missing.Contains("Nie ma archiwum"), "deleting a missing archive must say so");
        Check(memory.NoteCount == 1, "deleting an archive must not touch memories");

        // Cross-module insights over isolated stores.
        var insights = new WorkspaceInsightsService(memory, new TaskService(directory), new ProjectService(directory),
            new DiagnosticSnapshotService(null, directory), new ActionHistoryService(directory));
        string stats = insights.Statistics();
        Check(stats.Contains("STATYSTYKI DANYCH LOKALNYCH") && stats.Contains("Folder danych:"), "statistics must report counts and the data folder");
        Check(stats.Contains("Wspomnienia: 1"), "statistics must count the surviving note: " + stats);
        Check(insights.Briefing().Contains("PLAN NA") && insights.Briefing().Contains("działają tylko, gdy aplikacja"),
            "the briefing must state the closed-app limitation");
        Check(insights.SearchAll("ZXCVBNM").Contains("Wspomnienia"), "unified search must find the note");
        Check(insights.SearchAll("qqqqnieistnieje").Contains("Nic nie pasuje"), "an empty search must say so honestly");
        Check(insights.SearchAll("x").StartsWith("Podaj co najmniej"), "a one-character search must be refused");
        string backupText = insights.Backup(out string backupPath);
        Check(backupText.StartsWith("Kopia zapasowa zapisana"), "the backup must report success: " + backupText);
        Check(Directory.Exists(backupPath) && File.Exists(Path.Combine(backupPath, "manifest.json")), "the backup folder must exist with a manifest");
        Check(Directory.EnumerateFiles(backupPath, "conversation-memory.json").Any(), "the memory store must be part of the backup");
        return Task.CompletedTask;
    }
}
