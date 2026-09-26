using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SentinelX.Services.Intent;

namespace SentinelX;

/// <summary>Cross-module insights: one-click local backup with proof, honest statistics, unified search
/// and a daily briefing. Read-only except for the backup, which only copies files inside the app folder.
/// Since 0.91 also runs the „samokontrola” integrity report and writes the „propozycje” list — both
/// strictly informational: they never change anything on their own.</summary>
public sealed class WorkspaceInsightsService
{
    private readonly ConversationMemoryService memory;
    private readonly TaskService tasks;
    private readonly ProjectService projects;
    private readonly DiagnosticSnapshotService snapshots;
    private readonly ActionHistoryService history;
    private readonly MemoryArchiveService? archives;

    public Func<DateTime> NowProvider { get; set; } = () => DateTime.Now;
    public string? LastError { get; private set; }

    public WorkspaceInsightsService(ConversationMemoryService? memory = null, TaskService? tasks = null, ProjectService? projects = null,
        DiagnosticSnapshotService? snapshots = null, ActionHistoryService? history = null, MemoryArchiveService? archives = null)
    {
        this.memory = memory ?? new ConversationMemoryService();
        this.tasks = tasks ?? new TaskService();
        this.projects = projects ?? new ProjectService();
        this.snapshots = snapshots ?? new DiagnosticSnapshotService();
        this.history = history ?? new ActionHistoryService();
        this.archives = archives;
    }

    public string Statistics()
    {
        var allTasks = tasks.GetTasks(includeDone: true);
        var reminders = tasks.GetReminders();
        DateTime now = NowProvider();
        var builder = new StringBuilder();
        builder.AppendLine("STATYSTYKI DANYCH LOKALNYCH");
        builder.AppendLine("Rozmowy: " + memory.GetConversations().Count + " · wypowiedzi w magazynie: " + memory.GetAllEntries().Count);
        builder.AppendLine("Wspomnienia: " + memory.NoteCount + " (przypięte: " + memory.GetNotes().Count(x => x.Pinned) +
            ", nieaktualne: " + memory.GetNotes().Count(x => x.SupersededAt != null) + ")");
        builder.AppendLine("Zadania: " + allTasks.Count(x => x.Status != TaskRecord.StatusDone) + " otwartych, " +
            allTasks.Count(x => x.Status == TaskRecord.StatusDone) + " zrobionych");
        builder.AppendLine("Przypomnienia: " + reminders.Count(x => x.NotifiedAt == null) + " oczekujących, " +
            reminders.Count(x => x.Missed) + " przegapionych");
        builder.AppendLine("Projekty: " + projects.GetProjects().Count + " aktywnych, " + projects.GetProjects(includeArchived: true).Count(x => x.ArchivedAt != null) + " zarchiwizowanych");
        builder.AppendLine("Odczyty diagnostyczne: " + snapshots.GetSnapshots().Count + " (limit 20)");
        builder.AppendLine("Historia akcji: " + history.GetRecentEntries(1000).Count + " ostatnich stanów");
        builder.AppendLine();
        builder.AppendLine("PLIKI (rozmiar na dysku)");
        foreach (var file in StoreFiles())
            builder.AppendLine("· " + Path.GetFileName(file) + ": " + (File.Exists(file) ? Size(new FileInfo(file).Length) : "brak"));
        builder.AppendLine("Folder danych: " + AppPaths.Root + " (" + Size(DirectoryBytes(AppPaths.Root)) + ")");
        builder.AppendLine("To są ilości zapisanych danych, nie miara jakości pamięci ani stanu komputera.");
        return builder.ToString().TrimEnd();
    }

    /// <summary>„samokontrola”: verifies that Sentinel's own files are readable and parseable.
    /// Pure read-only — never writes, never repairs silently; every problem is reported with the file's name.</summary>
    public string SelfCheck()
    {
        var builder = new StringBuilder();
        builder.AppendLine("SAMOKONTROLA SENTINEL X");
        int problems = 0;

        void CheckFile(string path, bool json)
        {
            string name = Path.GetFileName(path);
            try
            {
                if (!File.Exists(path)) { builder.AppendLine("· ✓ " + name + " — jeszcze nie istnieje (powstanie przy pierwszym zapisie)"); return; }
                string content = File.ReadAllText(path);
                if (json && content.Trim().Length > 0) JsonSerializer.Deserialize<JsonElement>(content);
                builder.AppendLine("· ✓ " + name + " — czytelny" + (json ? ", składnia JSON poprawna" : "") + " (" + Size(new FileInfo(path).Length) + ")");
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            { problems++; builder.AppendLine("· ⚠ " + name + " — problem: " + ex.GetType().Name.Replace("Exception", "").ToLower(PlCulture)); }
        }

        foreach (string file in StoreFiles()) CheckFile(file, json: true);

        string lessons = Path.Combine(AppPaths.MemoryDirectory, UnderstandingJournal.FileName);
        if (File.Exists(lessons))
        {
            int bad = 0, all = 0;
            foreach (string line in File.ReadLines(lessons))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                all++;
                try { JsonSerializer.Deserialize<JsonElement>(line); } catch (JsonException) { bad++; }
            }
            if (bad == 0) builder.AppendLine("· ✓ " + UnderstandingJournal.FileName + " — " + all + " lekcji, wszystkie czytelne");
            else { problems++; builder.AppendLine("· ⚠ " + UnderstandingJournal.FileName + " — " + bad + " z " + all + " linii niepoprawne"); }
        }
        else builder.AppendLine("· ✓ " + UnderstandingJournal.FileName + " — jeszcze nie istnieje");

        string archiveRoot = Path.Combine(AppPaths.MemoryDirectory, "Archives");
        if (Directory.Exists(archiveRoot))
        {
            int archivesOk = 0, archivesBad = 0;
            foreach (string archive in Directory.EnumerateFiles(archiveRoot, "*.json", SearchOption.AllDirectories))
            {
                try { JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(archive)); archivesOk++; }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { archivesBad++; }
            }
            if (archivesBad == 0) builder.AppendLine("· ✓ archiwa — " + archivesOk + " plików, wszystkie czytelne");
            else { problems++; builder.AppendLine("· ⚠ archiwa — " + archivesBad + " plików nieczytelnych (poprawnych: " + archivesOk + ")"); }
        }
        else builder.AppendLine("· ✓ archiwa — brak folderu (nie było jeszcze archiwizacji)");

        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(AppPaths.Root)) ?? "C:\\");
            double freePercent = drive.TotalSize > 0 ? 100d * drive.AvailableFreeSpace / drive.TotalSize : 0;
            if (freePercent < 10) { problems++; builder.AppendLine("· ⚠ dysk " + drive.Name + " — tylko " + freePercent.ToString("0.0", PlCulture) + "% wolnego miejsca"); }
            else builder.AppendLine("· ✓ dysk " + drive.Name + " — " + freePercent.ToString("0.0", PlCulture) + "% wolnego miejsca");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { builder.AppendLine("· ⚠ dysk — nie udało się odczytać wolnego miejsca"); }

        builder.AppendLine();
        builder.AppendLine(problems == 0
            ? "Wszystkie sprawdzenia przeszły. Raport jest tylko do odczytu — nic nie zostało zmienione."
            : "Problemy: " + problems + ". Nic nie zmieniałem automatycznie — naprawa wymaga Twojej decyzji (np. „backup”, a uszkodzony plik możesz usunąć ręcznie).");
        return builder.ToString().TrimEnd();
    }

    private static readonly CultureInfo PlCulture = CultureInfo.GetCultureInfo("pl-PL");

    /// <summary>„napraw sie”: bezpieczna naprawa DANYCH SentinelX — przywracanie magazynów
    /// z kopii, odkładanie uszkodzonych plików na bok (nigdy kasowanie), czyszczenie dzienników
    /// JSONL. Kod aplikacji pozostaje nietknięty. Każdy krok jest w raporcie.</summary>
    public string SelfRepair()
    {
        var builder = new StringBuilder();
        int fixedCount = 0;
        foreach (string file in StoreFiles())
        {
            fixedCount += Core.SelfRepair.RepairJsonStore(file, out string line);
            builder.AppendLine(line);
        }
        fixedCount += Core.SelfRepair.RepairJsonl(Path.Combine(AppPaths.MemoryDirectory, UnderstandingJournal.FileName), out string lessonsLine);
        builder.AppendLine(lessonsLine);
        builder.AppendLine(fixedCount == 0
            ? "Nic nie wymagało naprawy — magazyny są czytelne."
            : "Naprawione pozycje: " + fixedCount + ". Oryginały uszkodzonych plików leżą obok (dopisek .corrupt-…) — niczego nie skasowałem.");
        return builder.ToString().TrimEnd();
    }

    /// <summary>„propozycje”: maintenance ideas computed from real state. Every line tells the user what to
    /// type — Sentinel never runs any of these on its own (explicit-approval autonomy).</summary>
    public string Suggestions()
    {
        DateTime now = NowProvider();
        var lines = new List<string>();

        try
        {
            string backups = AppPaths.BackupsDirectory;
            DateTime? lastBackup = Directory.Exists(backups)
                ? Directory.EnumerateDirectories(backups).Select(d => (DateTime?)new DirectoryInfo(d).CreationTime).Max()
                : null;
            if (lastBackup == null) lines.Add("Nie ma jeszcze żadnej kopii zapasowej danych — wpisz: backup");
            else if (now - lastBackup.Value > TimeSpan.FromDays(30))
                lines.Add("Ostatnia kopia zapasowa ma " + (int)(now - lastBackup.Value).TotalDays + " dni — wpisz: backup");
        }
        catch (IOException) { /* the suggestion list must not fail because of one folder */ }

        if (archives != null)
        {
            var due = archives.ArchiveDue().ToList();
            if (due.Count > 0) lines.Add("Rozmowy z " + due.Count + " mies. czekają na archiwizację — wpisz: archiwizuj rozmowy");
        }

        int openTasks = tasks.GetTasks().Count;
        if (openTasks > 0) lines.Add("Masz " + openTasks + " otwartych zadań — wpisz: zadania, aby je przejrzeć");
        int missed = tasks.GetReminders().Count(x => x.Missed);
        if (missed > 0) lines.Add("Przegapione przypomnienia: " + missed + " — wpisz: przypomnienia");
        int superseded = memory.GetNotes().Count(x => x.SupersededAt != null);
        if (superseded > 0) lines.Add("Nieaktualne wspomnienia: " + superseded + " — znajdziesz je w zakładce Pamięć (🧠 w Centrum)");
        if (memory.PrivateMode) lines.Add("Tryb prywatny jest włączony — rozmowa nie jest zapisywana; wyłącz go przyciskiem w Centrum, jeśli chcesz pamiętać");

        var builder = new StringBuilder();
        builder.AppendLine("PROPOZYCJE · nic nie wykona się samo");
        if (lines.Count == 0) builder.AppendLine("Wszystko wygląda dobrze: kopia zapasowa świeża, brak zaległości. Zajrzyj tu kiedy indziej albo wpisz: samokontrola");
        else foreach (string line in lines) builder.AppendLine("· " + line);
        builder.AppendLine();
        builder.AppendLine("Każda propozycja czeka na Twoją decyzję — Sentinel niczego nie uruchamia bez polecenia.");
        return builder.ToString().TrimEnd();
    }

    /// <summary>Copies the local stores into a timestamped backup folder and proves every copy with a read-back hash.</summary>
    public string Backup(out string backupPath)
    {
        backupPath = "";
        DateTime now = NowProvider();
        string directory = Path.Combine(AppPaths.BackupsDirectory, now.ToString("yyyyMMdd-HHmmss"));
        try
        {
            Directory.CreateDirectory(directory);
            var manifest = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string source in StoreFiles())
            {
                if (!File.Exists(source)) continue;
                string target = Path.Combine(directory, Path.GetFileName(source));
                File.Copy(source, target, true);
                string expected = Hash(File.ReadAllBytes(source));
                string actual = Hash(File.ReadAllBytes(target));
                if (expected != actual) return Failure("Kopia " + Path.GetFileName(source) + " nie zgadza się z odczytem kontrolnym — kopia zapasowa odrzucona.", out backupPath);
                manifest[Path.GetFileName(source)] = expected;
            }
            if (manifest.Count == 0) return Failure("Nie ma jeszcze żadnych plików danych do skopiowania.", out backupPath);
            string manifestPath = Path.Combine(directory, "manifest.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            backupPath = directory;
            return "Kopia zapasowa zapisana lokalnie (" + manifest.Count + " plików) i sprawdzona odczytem zwrotnym:\n" + directory +
                "\n" + string.Join("\n", manifest.Select(x => "· " + x.Key + " SHA-256 " + x.Value[..16] + "…")) +
                "\nKopia zawiera dane prywatne. Nic nie zostało wysłane do internetu.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return Failure("Nie udało się zapisać kopii: " + ex.Message, out backupPath); }
    }

    private string Failure(string message, out string path) { path = ""; LastError = message; return message; }

    public string SearchAll(string query)
    {
        string needle = ConversationMemoryService.Normalize(query ?? "");
        if (needle.Length < 2) return "Podaj co najmniej 2 znaki do wyszukania.";
        DateTime now = NowProvider();
        var builder = new StringBuilder();
        int total = 0;

        var notes = memory.GetNotes().Where(x => ConversationMemoryService.Normalize(x.Text).Contains(needle, StringComparison.Ordinal)).Take(5).ToArray();
        if (notes.Length > 0)
        {
            total += notes.Length;
            builder.AppendLine("Wspomnienia (" + notes.Length + "):");
            foreach (var note in notes) builder.AppendLine("· " + Truncate(note.Text, 140) + (note.Category.Length > 0 ? "  [" + note.Category + "]" : ""));
        }
        var turns = memory.SearchConversation(query, 5);
        if (turns.Count > 0)
        {
            total += turns.Count;
            builder.AppendLine("Aktywna rozmowa (" + turns.Count + "):");
            foreach (var turn in turns) builder.AppendLine("· [" + turn.Timestamp.ToString("dd.MM HH:mm") + "] " + (turn.Role == "user" ? "Ty" : "Sentinel") + ": " + Truncate(turn.Text, 140));
        }
        var foundTasks = tasks.GetTasks(includeDone: true).Where(x => ConversationMemoryService.Normalize(x.Title).Contains(needle, StringComparison.Ordinal)).Take(5).ToArray();
        if (foundTasks.Length > 0)
        {
            total += foundTasks.Length;
            builder.AppendLine("Zadania (" + foundTasks.Length + "):");
            foreach (var task in foundTasks)
                builder.AppendLine("· " + Truncate(task.Title, 120) + "  [" + task.Status + (task.DueAt == null ? "" : ", termin " + task.DueAt.Value.ToString("dd.MM HH:mm")) + "]");
        }
        var foundProjects = projects.GetProjects(includeArchived: true)
            .Where(x => ConversationMemoryService.Normalize(x.Name + " " + x.Description).Contains(needle, StringComparison.Ordinal)).Take(5).ToArray();
        if (foundProjects.Length > 0)
        {
            total += foundProjects.Length;
            builder.AppendLine("Projekty (" + foundProjects.Length + "):");
            foreach (var project in foundProjects) builder.AppendLine("· " + project.Name + "  [" + project.Status + (project.ArchivedAt == null ? "" : ", zarchiwizowany") + "]");
        }
        var foundSnapshots = snapshots.GetSnapshots().Where(x => ConversationMemoryService.Normalize(x.Label).Contains(needle, StringComparison.Ordinal)).Take(5).ToArray();
        if (foundSnapshots.Length > 0)
        {
            total += foundSnapshots.Length;
            builder.AppendLine("Odczyty diagnostyczne (" + foundSnapshots.Length + "):");
            foreach (var snapshot in foundSnapshots) builder.AppendLine("· " + snapshot.Label + "  [" + snapshot.CapturedAt.ToString("dd.MM.yyyy HH:mm") + "]");
        }
        if (total == 0)
            return "Nic nie pasuje do „" + query + "” w danych lokalnych (wspomnienia, aktywna rozmowa, zadania, projekty, odczyty).\n" +
                "Szukanie jest dopasowaniem tekstu po normalizacji — bez literówek, odmiany i synonimów.";
        return "Znalezione w danych lokalnych (" + total + " trafień, do 5 w każdej kategorii):\n" + builder.ToString().TrimEnd();
    }

    public string Briefing()
    {
        DateTime now = NowProvider();
        var allTasks = tasks.GetTasks(includeDone: true);
        var overdue = allTasks.Where(x => x.Status != TaskRecord.StatusDone && x.DueAt != null && x.DueAt < now).OrderBy(x => x.DueAt).ToArray();
        var today = allTasks.Where(x => x.Status != TaskRecord.StatusDone && x.DueAt != null && x.DueAt >= now && x.DueAt < now.Date.AddDays(1)).OrderBy(x => x.DueAt).ToArray();
        var upcoming = tasks.GetReminders().Where(x => x.NotifiedAt == null).OrderBy(x => x.RemindAt).Take(3).ToArray();
        var pinned = memory.GetNotes().Where(x => x.Pinned && x.SupersededAt == null).Take(3).ToArray();
        var builder = new StringBuilder();
        builder.AppendLine("PLAN NA " + now.ToString("dddd, d MMMM yyyy", CultureInfo.GetCultureInfo("pl-PL")) + " · " + now.ToString("HH:mm"));
        builder.AppendLine(overdue.Length == 0 ? "Brak przeterminowanych zadań." : "Przeterminowane (" + overdue.Length + "):");
        foreach (var task in overdue.Take(5)) builder.AppendLine("· " + Truncate(task.Title, 110) + " (termin " + task.DueAt!.Value.ToString("dd.MM HH:mm") + ")");
        builder.AppendLine(today.Length == 0 ? "Dziś nic nie ma terminu." : "Dziś (" + today.Length + "):");
        foreach (var task in today.Take(5)) builder.AppendLine("· " + Truncate(task.Title, 110) + " (o " + task.DueAt!.Value.ToString("HH:mm") + ")");
        builder.AppendLine(upcoming.Length == 0 ? "Brak oczekujących przypomnień." : "Najbliższe przypomnienia:");
        foreach (var reminder in upcoming) builder.AppendLine("· " + Truncate(reminder.Text, 110) + " (" + reminder.RemindAt.ToString("dd.MM HH:mm") + ")");
        if (pinned.Length > 0)
        {
            builder.AppendLine("Przypięte wspomnienia:");
            foreach (var note in pinned) builder.AppendLine("· " + Truncate(note.Text, 110));
        }
        var project = projects.ActiveProject;
        builder.AppendLine("Aktywny projekt: " + (project == null ? "brak (kontekst globalny)" : project.Name));
        builder.AppendLine("Ostatnia rozmowa: " + memory.ActiveConversationTitle);
        builder.AppendLine("Przypomnienia działają tylko, gdy aplikacja jest uruchomiona — przegapione pokażą się jako przegapione.");
        return builder.ToString().TrimEnd();
    }

    private IEnumerable<string> StoreFiles() => new[]
    {
        memory.StoragePath, tasks.StoragePath, projects.StoragePath, snapshots.StoragePath,
        Path.Combine(AppPaths.SettingsDirectory, "settings.json"),
        Path.Combine(AppPaths.MemoryDirectory, "LearnedPatterns.json")
    }.Distinct(StringComparer.OrdinalIgnoreCase);

    private static long DirectoryBytes(string root)
    {
        if (!Directory.Exists(root)) return 0;
        try
        {
            return Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Sum(path => { try { return new FileInfo(path).Length; } catch (IOException) { return 0L; } });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    private static string Size(long bytes) => bytes switch
    {
        < 1024 => bytes + " B",
        < 1024 * 1024 => (bytes / 1024d).ToString("0.#", CultureInfo.GetCultureInfo("pl-PL")) + " kB",
        _ => (bytes / 1048576d).ToString("0.#", CultureInfo.GetCultureInfo("pl-PL")) + " MB"
    };

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
