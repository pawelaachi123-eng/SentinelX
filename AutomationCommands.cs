using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;

namespace SentinelX;

/// <summary>Zestaw serwisów, których potrzebują polecenia automatyzacji. Wszystkie opcjonalne —
/// brak serwisu oznacza uczciwą odmowę zamiast udawanego sukcesu.</summary>
public sealed class AutomationContext
{
    public SchedulerService? Scheduler { get; init; }
    public WatchdogService? Watchdog { get; init; }
    public TaskService? Tasks { get; init; }
    /// <summary>Ścieżki plików danych Sentinela — do sprawdzenia spójności (SHA-256).</summary>
    public Func<IEnumerable<string>>? StoreFiles { get; init; }
}

/// <summary>0.97 · harmonogram (#007), watchdog folderów (#008), spójność danych (#022),
/// dziennik zdarzeń (#010), eksport kalendarza (#1002) i bezpieczny schowek (#145).
/// <para>Statyczna, bezstanowa warstwa poleceń: korzystają z niej oba interfejsy
/// (Centrum Sterowania i klasyczne okno), więc nie ma dwóch różnych prawd o tym, co wolno
/// zaplanować. Nic się nie wykonuje samo poza aplikacją — to świadoma różnica względem
/// harmonogramu zadań Windows.</para></summary>
public static class AutomationCommands
{
    /// <summary>Zwraca odpowiedź do czatu albo null, gdy polecenie nie należy do automatyzacji.</summary>
    public static string? TryHandle(string command, string text, AutomationContext context)
    {
        Match schedule = Regex.Match(text,
            @"^zaplanuj\s*[:]?\s*(?:w\s+|na\s+)?(?<days>.*?)\s*(?<time>\d{1,2}[:.]\d{2})\s+(?<cmd>\S.{0,159})$",
            RegexOptions.IgnoreCase);
        if (schedule.Success) return AddSchedule(context, schedule.Groups["time"].Value, schedule.Groups["days"].Value, command);

        if (text.StartsWith("zaplanuj", StringComparison.Ordinal))
            return "Jak zaplanować: „zaplanuj: 7:30 dzień dobry” — najpierw godzina (HH:MM), potem polecenie. " +
                   "Dni: „zaplanuj w dni robocze 8:00 zadania”, „zaplanuj w weekend 9:00 plan dnia”. " +
                   "Polecenie wykona się, gdy aplikacja jest uruchomiona (nie rejestruję nic w harmonogramie Windows).";

        if (text is "zaplanowane" or "harmonogram" or "zaplanowane zadania" or "pokaz harmonogram" or "co zaplanowane")
            return context.Scheduler == null ? NoScheduler() : context.Scheduler.Describe();

        Match remove = Regex.Match(text, @"^(?:usun|usun zaplanowane|skasuj zaplanowane)\s+(?<what>\d{1,2}|wszystko|wszystkie)$", RegexOptions.IgnoreCase);
        if (remove.Success && text.Contains("zaplanowan", StringComparison.Ordinal)) return RemoveSchedule(context, remove.Groups["what"].Value);

        if (text is "obserwowane" or "obserwator" or "obserwowane foldery" or "co obserwuje" or "watchdog")
            return context.Watchdog == null ? NoWatchdog() : context.Watchdog.Describe();

        if (text is "co nowego w folderze" or "co nowego w folderach" or "zmiany w folderze" or "nowe pliki")
            return WatchEvents(context);

        Match watch = Regex.Match(command, @"^obserwuj(?:\s+folder)?\s*:?\s*(?<path>.+)$", RegexOptions.IgnoreCase);
        if (watch.Success) return AddWatch(context, watch.Groups["path"].Value);

        Match unwatch = Regex.Match(command, @"^(?:przestan obserwowac|nie obserwuj|usun folder)(?:\s+|:)\s*(?<what>\d{1,2}|wszystko|wszystkie|.+)$", RegexOptions.IgnoreCase);
        if (unwatch.Success) return RemoveWatch(context, unwatch.Groups["what"].Value);

        if (text is "spojnosc danych" or "integrity" or "sprawdz spojnosc" or "spojnosc plikow" or "sumy kontrolne")
            return Integrity(context);

        if (text is "dziennik" or "dziennik zdarzen" or "logi" or "log" or "pokaz dziennik" or "ostatnie zdarzenia")
            return JsonLog.Describe();

        if (text is "kopie zapasowe" or "kopie" or "lista kopii" or "backup lista" or "jakie mam kopie")
            return DescribeBackups();

        if (text is "eksportuj kalendarz" or "kalendarz" or "eksport kalendarza" or "ics" or "wyeksportuj kalendarz")
            return ExportCalendar(context);

        Match clip = Regex.Match(text, @"^schowek auto(?:matycznie)?\s+(?<value>\d{1,4}|wylacz|wylaczone|off|0)$", RegexOptions.IgnoreCase);
        if (clip.Success) return SetClipboardAutoClear(clip.Groups["value"].Value);

        if (text is "wyczysc schowek" or "czysc schowek" or "wyczysc schowek teraz")
            return ClearClipboardNow();

        if (text is "bezpieczny schowek" or "status schowka" or "schowek auto")
            return SecureClipboard.Describe(DateTime.Now);

        return null;
    }

    private static string AddSchedule(AutomationContext context, string time, string daysText, string rawCommand)
    {
        if (context.Scheduler == null) return NoScheduler();
        string[] parts = time.Replace('.', ':').Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int hour) || !int.TryParse(parts[1], out int minute))
            return "Godzinę podaj w formacie HH:MM, np. „zaplanuj: 7:30 dzień dobry”.";
        // The command is everything after the time — taken from the raw text so casing survives.
        Match tail = Regex.Match(rawCommand, @"(?<time>\d{1,2}[:.]\d{2})\s+(?<cmd>.+)$", RegexOptions.IgnoreCase);
        string command = tail.Success ? tail.Groups["cmd"].Value.Trim() : "";
        int days = SchedulerService.ParseDays(daysText);
        if (days == 0)
            return "Nie rozpoznałem dni: „" + daysText.Trim() + "”. Użyj: codziennie, dni robocze, weekend albo nazwy dni (pon, śr, pt).";
        var saved = context.Scheduler.Add(command, hour, minute, days);
        if (saved == null) return context.Scheduler.LastStorageError ?? "Nie udało się dodać wpisu.";
        DateTime? next = SchedulerService.NextRun(saved, DateTime.Now);
        return "Zaplanowane: „" + saved.Command + "” o " + saved.Hour.ToString("00") + ":" + saved.Minute.ToString("00") +
            " (" + SchedulerService.DaysText(saved.Days) + ")" + (next.HasValue ? ", najbliższe uruchomienie: " + next.Value.ToString("ddd d.MM HH:mm", new CultureInfo("pl-PL")) : "") +
            ".\nPolecenie wykona się samo tylko wtedy, gdy Sentinel jest uruchomiony — nie rejestruję nic w harmonogramie zadań Windows.";
    }

    private static string RemoveSchedule(AutomationContext context, string what)
    {
        if (context.Scheduler == null) return NoScheduler();
        if (what is "wszystko" or "wszystkie")
            return context.Scheduler.Clear() ? "Wyczyściłem harmonogram. Nic nie uruchomi się samo." : context.Scheduler.LastStorageError ?? "Nie udało się wyczyścić.";
        return context.Scheduler.Remove(what)
            ? "Usunięty wpis " + what + ". „zaplanowane” pokazuje aktualną listę."
            : context.Scheduler.LastStorageError ?? "Nie udało się usunąć wpisu.";
    }

    private static string AddWatch(AutomationContext context, string path)
    {
        if (context.Watchdog == null) return NoWatchdog();
        string clean = path.Trim().Trim('"').Trim();
        if (clean.Length == 0) return "Podaj folder: „obserwuj: C:\\Users\\Twój-login\\Pobrane”.";
        return context.Watchdog.Add(clean)
            ? "Obserwuję folder: " + clean + "\nSprawdzam co " + (int)WatchdogService.MinimumInterval.TotalSeconds +
              " s i tylko podglądam: nic nie przenoszę, nie kasuję i nie uruchamiam akcji po wykryciu zmiany. Zapytaj: „co nowego w folderze”."
            : context.Watchdog.LastError ?? "Nie udało się dodać folderu.";
    }

    private static string RemoveWatch(AutomationContext context, string what)
    {
        if (context.Watchdog == null) return NoWatchdog();
        if (what is "wszystko" or "wszystkie")
            return context.Watchdog.Clear() ? "Przestałem obserwować foldery i wyczyściłem listę zdarzeń." : context.Watchdog.LastError ?? "Nie udało się wyczyścić.";
        return context.Watchdog.Remove(what)
            ? "Przestałem obserwować: " + what + "."
            : context.Watchdog.LastError ?? "Nie udało się usunąć folderu.";
    }

    private static string WatchEvents(AutomationContext context)
    {
        if (context.Watchdog == null) return NoWatchdog();
        IReadOnlyList<WatchEvent> fresh = context.Watchdog.Poll();
        IReadOnlyList<WatchEvent> recent = context.Watchdog.Recent(12);
        var builder = new StringBuilder();
        builder.AppendLine("ZMIANY W OBSERWOWANYCH FOLDERACH");
        if (recent.Count == 0)
        {
            builder.AppendLine("Nic się nie zmieniło od dodania folderów — to dobra wiadomość.");
            builder.AppendLine("Dodaj folder: „obserwuj: ścieżka”. Lista: „obserwowane”.");
            return builder.ToString().TrimEnd();
        }
        builder.AppendLine(fresh.Count == 0 ? "Od ostatniego sprawdzenia bez zmian. Ostatnie zdarzenia:" : "Wykryte przed chwilą:");
        foreach (WatchEvent item in recent) builder.AppendLine("· " + item);
        builder.AppendLine();
        builder.AppendLine("To tylko podgląd: Sentinel niczego nie przenosi ani nie kasuje po wykryciu zmiany.");
        return builder.ToString().TrimEnd();
    }

    private static string Integrity(AutomationContext context)
    {
        IEnumerable<string> files = context.StoreFiles?.Invoke() ?? [];
        var list = files.ToList();
        if (list.Count == 0) return "Nie mam dostępu do listy plików danych w tym oknie — sprawdź spójność w Centrum Sterowania.";
        return IntegrityChecker.Describe(IntegrityChecker.Check(list));
    }

    private static string ExportCalendar(AutomationContext context)
    {
        if (context.Tasks == null) return "Eksport kalendarza potrzebuje magazynu zadań — w tym oknie nie jest dostępny.";
        var entries = new List<CalendarExport.Entry>();
        foreach (var task in context.Tasks.GetTasks(includeDone: false))
            if (task.DueAt.HasValue)
                entries.Add(new CalendarExport.Entry("Zadanie: " + task.Title, task.DueAt.Value, TimeSpan.FromMinutes(30),
                    "Z SentinelX — priorytet: " + task.Priority));
        foreach (var reminder in context.Tasks.GetReminders().Where(x => x.NotifiedAt == null))
            entries.Add(new CalendarExport.Entry("Przypomnienie: " + reminder.Text, reminder.RemindAt, TimeSpan.FromMinutes(10), "Z SentinelX"));
        if (entries.Count == 0)
            return "Nie mam żadnego terminu ani przypomnienia do wyeksportowania. Dodaj zadanie z datą („zrób raport jutro 18:00”) albo przypomnienie.";
        string desktop;
        try { desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { desktop = AppPaths.Root; }
        string path = Path.Combine(desktop.Length > 0 ? desktop : AppPaths.Root, "sentinel-kalendarz.ics");
        string message = CalendarExport.Save(Path.GetDirectoryName(path)!, entries, Path.GetFileName(path), out string saved, out int count);
        return count == 0 ? message : message + "\nTerminów: " + count + " (zadania z datą + przypomnienia).";
    }

    private static string SetClipboardAutoClear(string value)
    {
        if (value is "wylacz" or "wylaczone" or "off" or "0")
        {
            SecureClipboard.AutoClearSeconds = 0;
            return "Automatyczne czyszczenie schowka wyłączone. Włącz: „schowek auto 30”.";
        }
        if (!int.TryParse(value, out int seconds) || seconds < 5 || seconds > SecureClipboard.MaxSeconds)
            return "Podaj czas od 5 do " + SecureClipboard.MaxSeconds + " sekund, np. „schowek auto 30”.";
        SecureClipboard.AutoClearSeconds = seconds;
        return "Bezpieczny schowek włączony: to, co sam skopiuję komendą „kopiuj: …”, zniknie ze schowka po " + seconds +
            " s. Zawartość skopiowana przez Ciebie w innej aplikacji zostaje nietknięta.";
    }

    private static string ClearClipboardNow()
        => SecureClipboard.Clear()
            ? "Schowek wyczyszczony. (Zadziałało — wyczyściłem zawartość systemowego schowka.)"
            : "Schowek był pusty albo nieaktywny — nie było harmonogramu czyszczenia do odwołania.";

    /// <summary>#020 · lista kopii zapasowych: archiwa ZIP i foldery z odczytem zwrotnym.
    /// Tylko odczyt — lista niczego nie usuwa (rotacja działa przy tworzeniu nowej kopii).</summary>
    private static string DescribeBackups()
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("KOPIE ZAPASOWE · " + AppPaths.BackupsDirectory);
        try
        {
            if (!Directory.Exists(AppPaths.BackupsDirectory))
            {
                builder.AppendLine("Nie ma jeszcze żadnej kopii. Zrób ją teraz: backup");
                return builder.ToString().TrimEnd();
            }
            var zips = Directory.EnumerateFiles(AppPaths.BackupsDirectory, "sentinel-*.zip")
                .OrderByDescending(x => x, StringComparer.Ordinal).ToList();
            var folders = Directory.EnumerateDirectories(AppPaths.BackupsDirectory)
                .OrderByDescending(x => x, StringComparer.Ordinal).ToList();
            foreach (string zip in zips.Take(WorkspaceInsightsService.KeepZipBackups))
                builder.AppendLine("· " + Path.GetFileName(zip) + " — " + (new FileInfo(zip).Length / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) +
                    " KB (ZIP, " + new FileInfo(zip).CreationTime.ToString("yyyy-MM-dd HH:mm") + ")");
            foreach (string folder in folders.Take(WorkspaceInsightsService.KeepFolderBackups))
                builder.AppendLine("· " + Path.GetFileName(folder) + " — folder z manifestem (" + new DirectoryInfo(folder).CreationTime.ToString("yyyy-MM-dd HH:mm") + ")");
            if (zips.Count == 0 && folders.Count == 0) builder.AppendLine("Folder kopii jest pusty — wpisz: backup");
            builder.AppendLine("Limit: " + WorkspaceInsightsService.KeepZipBackups + " archiwów ZIP i " + WorkspaceInsightsService.KeepFolderBackups +
                " foldery — starsze usuwa rotacja przy każdej nowej kopii.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            builder.AppendLine("Nie udało się odczytać folderu kopii: " + ex.Message);
        }
        return builder.ToString().TrimEnd();
    }

    private static string NoScheduler() => "Harmonogram nie jest dostępny w tym oknie. Użyj Centrum Sterowania (Centrum w menu).";
    private static string NoWatchdog() => "Obserwacja folderów nie jest dostępna w tym oknie. Użyj Centrum Sterowania (Centrum w menu).";
}
