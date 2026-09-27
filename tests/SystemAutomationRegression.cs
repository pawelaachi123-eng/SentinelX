using System.Globalization;
using System.Text;
using System.IO;

namespace SentinelX.Tests;

/// <summary>0.97 · regresje automatyzacji i narzędzi z listy 1550: harmonogram (#007), watchdog (#008),
/// dziennik JSON (#010), spójność danych (#022), bezpieczny schowek (#145), siła hasła (#143),
/// wyszukiwanie w treści (#811), porównanie plików (#1020), profil CSV (#1075), eksport .ics (#1002)
/// oraz kalkulatory finansowe i zdrowotne.
/// <para>Zasada jak w 0.96: testy nie ruszają stanu maszyny. Nie zamykają systemu, nie blokują ekranu,
/// nie czyszczą prawdziwego schowka i nie dotykają folderów użytkownika — wszystko dzieje się
/// w katalogu tymczasowym, a schowek jest sprawdzany wyłącznie przez czystą funkcję czasu
/// (SecureClipboard używa wstrzykniętego „teraz”).</para></summary>
internal static class SystemAutomationRegression
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        string store = Path.Combine(directory, "schedules");
        var scheduler = new SchedulerService(store);

        // ============================ harmonogram (#007) ============================
        Check(scheduler.GetSchedules().Count == 0, "a fresh schedule store has no entries");
        var morning = scheduler.Add("dzień dobry", 7, 30, 127);
        Check(morning != null, "a daily schedule saves: " + scheduler.LastStorageError);
        Check(scheduler.GetSchedules().Count == 1, "one entry after adding one");
        Check(new SchedulerService(store).GetSchedules().Count == 1, "schedules survive a reload from disk");

        // Destructive and power commands must be refused up front — a schedule runs unattended.
        Check(scheduler.Add("zamknij komputer", 23, 0) == null, "a shutdown schedule is refused");
        Check((scheduler.LastStorageError ?? "").Contains("zgody"), "the refusal explains itself: " + scheduler.LastStorageError);
        Check(scheduler.Add("usuń wszystkie wspomnienia", 3, 0) == null, "a destructive schedule is refused");
        Check(scheduler.Add("zadania", 24, 0) == null, "hour 24 is refused");
        Check(scheduler.Add("zadania", 8, 61) == null, "minute 61 is refused");
        Check(scheduler.Add("x", 8, 0) == null, "a one-character command is refused");

        // Day masks.
        Check(SchedulerService.ParseDays("codziennie") == 127, "„codziennie” is every day");
        Check(SchedulerService.ParseDays("w dni robocze") == 31, "weekdays are Monday–Friday");
        Check(SchedulerService.ParseDays("weekend") == 96, "weekend is Saturday and Sunday");
        Check(SchedulerService.ParseDays("pon, sr, pt") == 21, "named days build a mask (mon+wed+fri = 21)");
        Check(SchedulerService.ParseDays("") == 127, "an empty phrase defaults to daily");

        // Due window: fires inside 15 minutes of the agreed hour and never twice a day.
        DateTime monday = new(2026, 9, 28, 7, 30, 0); // a Monday
        Check(monday.DayOfWeek == DayOfWeek.Monday, "the fixture date is a Monday");
        Check(scheduler.TakeDue(monday.AddMinutes(1)).Count == 1, "the schedule fires just after the agreed minute");
        Check(scheduler.TakeDue(monday.AddMinutes(2)).Count == 0, "the schedule does not fire twice a day");
        var weekly = scheduler.Add("zadania", 8, 0, SchedulerService.ParseDays("wtorek"));
        Check(weekly != null, "a weekday schedule saves");
        Check(scheduler.TakeDue(monday.AddHours(2)).Count == 0, "a Tuesday-only schedule stays silent on Monday");
        Check(scheduler.TakeDue(monday.AddDays(1).AddMinutes(31)).Count == 1, "a Tuesday-only schedule fires on Tuesday after its hour");

        // A schedule older than the catch-up window is skipped, not silently dropped.
        var stale = scheduler.Add("stare", 1, 0, 127);
        Check(stale != null, "a stale-probe schedule saves");
        Check(scheduler.TakeDue(monday.AddDays(3).AddHours(9)).Count == 0, "an old schedule is not chased down");
        Check(scheduler.GetSchedules().First(x => x.Id == stale!.Id).Skipped >= 1, "a missed schedule is counted as skipped, not hidden");

        // Removal.
        int before = scheduler.GetSchedules().Count;
        Check(scheduler.Remove("1"), "removing by number works");
        Check(scheduler.GetSchedules().Count == before - 1, "the entry is gone");
        Check(!scheduler.Remove("999"), "removing a number that does not exist fails honestly");
        Check(SchedulerService.DaysText(127) == "codziennie", "day mask describes itself");

        // ============================ watchdog folderów (#008) ============================
        string watched = Path.Combine(directory, "watch");
        Directory.CreateDirectory(watched);
        var watchdog = new WatchdogService();
        Check(watchdog.Add(watched), "an existing folder is watched: " + watchdog.LastError);
        Check(!watchdog.Add(watched), "the same folder is not watched twice");
        Check(!watchdog.Add(Path.Combine(directory, "nie-ma-takiego")), "a missing folder is refused");

        DateTime tick = new(2026, 9, 28, 10, 0, 0);
        File.WriteAllText(Path.Combine(watched, "raport.txt"), "start");
        IReadOnlyList<WatchEvent> fresh = watchdog.Poll(tick);
        Check(fresh.Count == 1 && fresh[0].Kind == "nowy", "a new file is reported as new, got " + fresh.Count);
        Check(watchdog.Poll(tick.AddSeconds(5)).Count == 0, "the second poll inside the interval reports nothing");

        File.WriteAllText(Path.Combine(watched, "raport.txt"), "zmiana");
        IReadOnlyList<WatchEvent> changed = watchdog.Poll(tick.AddSeconds(30));
        Check(changed.Any(x => x.Kind == "zmieniony"), "a modified file is reported as changed");

        File.Delete(Path.Combine(watched, "raport.txt"));
        IReadOnlyList<WatchEvent> removed = watchdog.Poll(tick.AddSeconds(60));
        Check(removed.Any(x => x.Kind == "usunięty"), "a deleted file is reported as removed");
        Check(watchdog.Recent(10).Count >= 3, "events are kept for the session");
        Check(watchdog.Describe().Contains("OBSERWOWANE FOLDERY"), "the watchdog describes itself");
        Check(watchdog.Remove("1"), "a folder can stop being watched");
        Check(!watchdog.Remove("1"), "removing twice fails honestly");

        // ============================ poller: jeden tik dla wszystkiego ============================
        var pollerStore = Path.Combine(directory, "poller");
        var pollerScheduler = new SchedulerService(pollerStore);
        pollerScheduler.Add("raport", 9, 0, 127);
        var reports = new List<string>();
        var ran = new List<string>();
        var poller = new AutomationPoller(pollerScheduler, null, (command, _) => { ran.Add(command); return Task.CompletedTask; }, line => reports.Add(line));
        DateTime before = new(2026, 9, 28, 8, 59, 30);
        poller.Tick(before);
        Check(ran.Count == 0, "a tick before the agreed hour does nothing");
        poller.Tick(before.AddSeconds(15));
        Check(ran.Count == 0, "a tick inside the 20 s interval does nothing (no hammering)");
        poller.Tick(before.AddSeconds(40)); // 9:00:10 — the schedule came due
        Check(ran.Count == 1 && ran[0] == "raport", "the scheduled command runs once the hour passes");
        Check(reports.Any(x => x.Contains("Harmonogram")), "the report says what ran");
        poller.Tick(before.AddSeconds(70));
        Check(ran.Count == 1, "a schedule does not run twice the same day");

        // ============================ bezpieczny schowek (#145) ============================
        DateTime now = new(2026, 9, 28, 12, 0, 0);
        SecureClipboard.AutoClearSeconds = 30;
        Check(SecureClipboard.AutoClearSeconds == 30, "auto-clear is configurable");
        SecureClipboard.AutoClearSeconds = 0;
        Check(!SecureClipboard.Armed, "clearing the clipboard leaves nothing armed");
        Check(SecureClipboard.Poll(now) == false, "polling without an armed clipboard does nothing");
        Check(SecureClipboard.Describe(now).Contains("wyłączone"), "the clipboard status is honest when disabled");

        // ============================ czyste narzędzia ============================
        // Siła hasła (#143): deterministycznie, bez zapisywania.
        string weak = Core.PasswordStrength.Describe("haslo");
        Check(weak.Contains("SIŁA HASŁA"), "the password report has a header");
        Check(Core.PasswordStrength.Analyze("haslo123").Score < Core.PasswordStrength.Analyze("Kot$7mArkoWnIa!9xq").Score,
            "a long mixed password scores better than a dictionary word");
        Check(Core.PasswordStrength.Analyze("Kot$7mArkoWnIa!9xq").EntropyBits > 60, "a strong password has real entropy");
        Check(Core.PasswordStrength.Analyze("aaa").Findings.Count > 0, "findings are always reported");

        // Porównanie plików (#1020 / #1019).
        Core.TextDiff.Result identical = Core.TextDiff.Compare("a\nb\nc", "a\nb\nc");
        Check(identical.Identical && identical.Same == 3, "identical text has no differences");
        Core.TextDiff.Result changedText = Core.TextDiff.Compare("a\nb\nc", "a\nb\ncc");
        Check(changedText.Removed == 1 && changedText.Added == 1, "one line changed = one removed and one added");
        Check(Core.TextDiff.Describe("a\nb", "a\nc").Contains("Różnice"), "the diff describes itself in Polish");
        Check(Core.TextDiff.Compare("a", "a").Lines.Count == 1, "the diff keeps common lines once");

        // Profil CSV (#1075) na pliku z katalogu tymczasowego.
        string csvPath = Path.Combine(directory, "dane.csv");
        File.WriteAllText(csvPath, "imie,miasto,wiek\nAla,Kraków,30\nBartek,,41\nAla,Kraków,30\n", Encoding.UTF8);
        Core.CsvAnalyzer.Report csv = Core.CsvAnalyzer.AnalyzePath(csvPath);
        Check(csv.Rows == 3, "every data row is counted, got " + csv.Rows);
        Check(csv.Columns.Count == 3, "every column is counted, got " + csv.Columns.Count);
        Check(csv.Columns[0].Header == "imie", "the header row is used as column names");
        Check(csv.Columns[1].Empty == 1, "missing values are counted");
        Check(csv.DuplicateRows == 1, "duplicate rows are detected");
        Check(csv.Columns[2].Numeric, "a numeric column is recognised");
        Check(Core.CsvAnalyzer.Describe(csv).Contains("PROFIL DANYCH"), "the profile describes itself");
        string tsvPath = Path.Combine(directory, "dane.tsv");
        File.WriteAllText(tsvPath, "a\tb\n1\t2\n", Encoding.UTF8);
        Check(Core.CsvAnalyzer.AnalyzePath(tsvPath).Separator == '\t', "a tab-separated file is detected");

        // Eksport kalendarza (#1002).
        string ics = Core.CalendarExport.Build([new Core.CalendarExport.Entry("Spotkanie", new DateTime(2026, 12, 24, 10, 0, 0))]);
        Check(ics.StartsWith("BEGIN:VCALENDAR", StringComparison.Ordinal), "the ICS starts with the calendar header");
        Check(ics.Contains("BEGIN:VEVENT") && ics.Contains("DTSTART:"), "the event carries its start time");
        Check(ics.Contains("\r\n"), "the ICS uses CRLF line endings");
        Check(Core.CalendarExport.Build([]) == "", "an empty calendar is not produced");
        string icsMessage = Core.CalendarExport.Save(directory, [new Core.CalendarExport.Entry("Test", DateTime.Now)], "test.ics", out string icsPath, out int icsCount);
        Check(icsCount == 1 && File.Exists(icsPath), "the calendar file is written: " + icsMessage);

        // Szukanie w treści plików (#811) — tylko w katalogu podanym wprost.
        string searchRoot = Path.Combine(directory, "tresc");
        Directory.CreateDirectory(searchRoot);
        File.WriteAllText(Path.Combine(searchRoot, "notatka.txt"), "Kluczowe hasło: sentinel");
        File.WriteAllText(Path.Combine(searchRoot, "inny.txt"), "nic ciekawego");
        Core.FileSearch.Result search = Core.FileSearch.Search([searchRoot], "sentinel");
        Check(search.Hits.Count == 1, "the phrase is found in one file, got " + search.Hits.Count);
        Check(search.Hits[0].Line.Contains("sentinel"), "the matching line comes back");
        Check(Core.FileSearch.Search([searchRoot], "nie-ma-tego").Hits.Count == 0, "a missing phrase finds nothing");
        Check(Core.FileSearch.Search([searchRoot], "x").Hits.Count == 0, "a one-character phrase is not searched");

        // Sumy kontrolne (#022).
        var integrity = Core.IntegrityChecker.Check([csvPath, Path.Combine(directory, "nie-ma-tego.json")]);
        Check(integrity.Count == 2, "every requested file gets a row");
        Check(integrity[0].Exists && integrity[0].Sha256.Length == 64, "a readable file gets a SHA-256");
        Check(!integrity[1].Exists && integrity[1].Status.Contains("brak"), "a missing file is reported, not invented");
        Check(Core.IntegrityChecker.Describe(integrity).Contains("SPÓJNOŚĆ DANYCH"), "the integrity report describes itself");

        // Kalkulatory finansowe (#1141–1200).
        Check(Core.PlCalculators.Inflacja(1000, 5, 3).Contains("Siła nabywcza"), "inflation reports the real value");
        Check(Core.PlCalculators.Inflacja(1000, 100, 1).Contains("500"), "100% inflation halves the value");
        Check(Core.PlCalculators.CelOszczedzania(1000, 100, 0).Contains("10"), "1000 at 100 a month = 10 months");
        Check(Core.PlCalculators.Roi(1000, 1500).Contains("50"), "ROI of 1000 → 1500 is 50%");
        Check(Core.PlCalculators.ProgRentownosci(50, 30, 1000).Contains("50"), "1000 / (50 − 30) = 50 units");
        Check(Core.PlCalculators.Deprecjacja(1200, 4).Contains("300"), "1200 over 4 years = 300 a year");
        string oneDebt = Core.PlCalculators.SplataDlugu([("dług", 1000, 100, 0)]);
        Check(oneDebt.Contains("10 miesięcy"), "1000 at 100 with no interest takes 10 months, got: " + oneDebt);
        string twoDebts = Core.PlCalculators.SplataDlugu([("a", 500, 50, 20), ("b", 2000, 60, 5)]);
        Check(twoDebts.Contains("Kula śnieżna") && twoDebts.Contains("Lawina"), "two debts are compared by both methods");

        // Kalkulatory zdrowotne (#1201–1250).
        Check(Core.PlCalculators.Bmr(80, 180, 30, true).Contains("1780"), "Mifflin-St Jeor for 80/180/30 male = 1780 kcal");
        Check(Core.PlCalculators.Bmr(80, 180, 30, false).Contains("1614"), "the female variant subtracts 161");
        Check(Core.PlCalculators.Tdee(80, 180, 30, true, 1).Contains("2136"), "sedentary TDEE = BMR × 1.2");
        Check(Core.PlCalculators.Tdee(80, 180, 30, true, 6).Contains("Poziom"), "an out-of-range activity level is refused");
        Check(Core.PlCalculators.Woda(80).Contains("2,80"), "80 kg × 35 ml = 2.8 l");
        Check(Core.PlCalculators.Tetno(30).Contains("190"), "max heart rate at 30 = 190");
        Check(Core.PlCalculators.Makro(2000).Contains("Białko"), "the macro split lists protein");
        Check(Core.PlCalculators.CykleSnu(new TimeSpan(23, 0, 0)).Contains("CYKLE SNU"), "sleep cycles describe themselves");
        Check(Core.PlCalculators.CykleSnu(new TimeSpan(23, 0, 0), new TimeSpan(7, 0, 0)).Contains("cykli"), "a fixed wake time is analysed");

        // Dziennik zdarzeń (#010) — własny katalog danych, więc nic nie psuje w maszynie CI.
        string journalDirectory = Path.Combine(directory, "logs");
        Directory.CreateDirectory(journalDirectory);
        JsonLog.OverridePath = Path.Combine(journalDirectory, "sentinel.jsonl");
        JsonLog.Write("test", "regresja 0.97", "szczegóły");
        IReadOnlyList<string> lines = JsonLog.Recent(5);
        Check(lines.Count >= 1, "the journal keeps the written record");
        Check(lines.Any(x => x.Contains("regresja 0.97")), "the record is readable back");
        Check(JsonLog.Describe(5).Contains("DZIENNIK ZDARZEŃ"), "the journal describes itself");
        JsonLog.OverridePath = "";

        // Komendy automatyzacji: uczciwa odmowa, gdy brak serwisu.
        Check(AutomationCommands.TryHandle("zaplanowane", "zaplanowane", new AutomationContext())!.Contains("Centrum"),
            "without the service the schedule command refuses honestly instead of inventing");
        Check(AutomationCommands.TryHandle("obserwowane", "obserwowane", new AutomationContext())!.Contains("Centrum"),
            "without the service the watchdog command refuses honestly");
        Check(AutomationCommands.TryHandle("coś zupełnie innego", "cos zupelnie innego", new AutomationContext()) == null,
            "unrelated commands fall through");
        Check(AutomationCommands.TryHandle("zaplanuj: 25:00 test", "zaplanuj: 25:00 test", new AutomationContext { Scheduler = scheduler })!.Contains("HH:MM"),
            "an impossible hour is refused with a hint");
        Check(AutomationCommands.TryHandle("zaplanuj: 8:00 zamknij komputer", "zaplanuj: 8:00 zamknij komputer", new AutomationContext { Scheduler = scheduler })!.Contains("zgody"),
            "a scheduled shutdown is refused even through the command layer");

        await Task.CompletedTask;
    }
}
