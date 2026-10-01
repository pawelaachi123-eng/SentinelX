using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SentinelX;

public sealed class TaskRecord
{
    public const string PriorityLow = "niski";
    public const string PriorityNormal = "normalny";
    public const string PriorityHigh = "wysoki";
    public const string StatusOpen = "otwarte";
    public const string StatusDoing = "w toku";
    public const string StatusDone = "zrobione";
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Priority { get; set; } = PriorityNormal;
    public string Status { get; set; } = StatusOpen;
    public DateTime? DueAt { get; set; }
    public string ProjectId { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DoneAt { get; set; }
}

public sealed class ReminderRecord
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime RemindAt { get; set; }
    public DateTime? NotifiedAt { get; set; }
    /// <summary>Set when the app was closed at RemindAt and the reminder surfaced only on a later launch.</summary>
    public bool Missed { get; set; }
    public string ProjectId { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public sealed class TaskState
{
    public int Version { get; set; } = 1;
    public List<TaskRecord> Tasks { get; set; } = [];
    public List<ReminderRecord> Reminders { get; set; } = [];
}

/// <summary>Tasks and one-shot reminders. Reminders fire only while the app runs; anything missed surfaces at launch, never silently.</summary>
public sealed class TaskService
{
    private const int MaxTasks = 500;
    private const int MaxReminders = 500;
    private readonly object syncRoot = new();
    private readonly string storePath;
    private readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private TaskState state = new();
    public string? LastStorageError { get; private set; }
    public string StoragePath => storePath;
    /// <summary>Clock seam for regression runs; production keeps the system local time.</summary>
    public Func<DateTime> NowProvider { get; set; } = () => DateTime.Now;
    public event Action<string>? ReminderFired;
    public event Action? Changed;

    public TaskService(string? directory = null)
    {
        storePath = Path.Combine(directory ?? AppPaths.MemoryDirectory, "tasks.json");
        Load();
    }

    public IReadOnlyList<TaskRecord> GetTasks(bool includeDone = false)
    {
        lock (syncRoot)
            return state.Tasks.Where(x => includeDone || x.Status != TaskRecord.StatusDone).OrderByDescending(x => x.Priority == TaskRecord.PriorityHigh).ThenBy(x => x.DueAt ?? DateTime.MaxValue).Select(Clone).ToArray();
    }

    public IReadOnlyList<ReminderRecord> GetReminders()
    {
        lock (syncRoot)
            return state.Reminders.OrderBy(x => x.NotifiedAt == null ? x.RemindAt : DateTime.MaxValue).ThenBy(x => x.RemindAt).Select(Clone).ToArray();
    }

    /// <summary>0.96 · KUŹNIA: read-only search through titles of tasks (also done ones) and reminders.
    /// Diacritics and case are ignored and every word of the query must occur (same rule as the Tools page).</summary>
    public (IReadOnlyList<TaskRecord> Tasks, IReadOnlyList<ReminderRecord> Reminders) Search(string query, int limit = 20)
    {
        string[] terms = ConversationMemoryService.Normalize(query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) return (Array.Empty<TaskRecord>(), Array.Empty<ReminderRecord>());
        bool Matches(string text)
        {
            string haystack = ConversationMemoryService.Normalize(text);
            return terms.All(term => haystack.Contains(term, StringComparison.Ordinal));
        }
        lock (syncRoot)
        {
            var foundTasks = state.Tasks.Where(x => Matches(x.Title))
                .OrderBy(x => x.Status == TaskRecord.StatusDone).ThenBy(x => x.DueAt ?? DateTime.MaxValue)
                .Take(limit).Select(Clone).ToArray();
            var foundReminders = state.Reminders.Where(x => Matches(x.Text))
                .OrderBy(x => x.RemindAt).Take(limit).Select(Clone).ToArray();
            return (foundTasks, foundReminders);
        }
    }

    public TaskRecord? AddTask(string title, string priority, DateTime? dueAt, string projectId)
    {
        bool fire = false;
        TaskRecord created;
        lock (syncRoot)
        {
            title = (title ?? "").Trim();
            if (state.Tasks.Count(x => x.Status != TaskRecord.StatusDone) >= MaxTasks) { LastStorageError = "Osiągnięto limit 500 otwartych zadań."; return null; }
            if (title.Length is < 1 or > 500) { LastStorageError = "Zadanie musi mieć 1–500 znaków."; return null; }
            created = new()
            {
                Id = Guid.NewGuid().ToString("N")[..12], Title = title,
                Priority = priority is TaskRecord.PriorityLow or TaskRecord.PriorityHigh ? priority : TaskRecord.PriorityNormal,
                DueAt = dueAt, ProjectId = projectId ?? "", CreatedAt = NowProvider(), UpdatedAt = NowProvider(),
            };
            state.Tasks.Add(created);
            SaveLocked();
            fire = true;
        }
        if (fire) Changed?.Invoke();
        return Clone(created);
    }

    public bool RenameTask(string id, string title)
    {
        title = (title ?? "").Trim();
        if (title.Length is < 1 or > 500) { LastStorageError = "Zadanie musi mieć 1–500 znaków."; return false; }
        bool fire = false;
        lock (syncRoot)
        {
            var task = state.Tasks.FirstOrDefault(x => x.Id == id);
            if (task == null) return false;
            task.Title = title; task.UpdatedAt = NowProvider();
            SaveLocked(); fire = true;
        }
        if (fire) Changed?.Invoke(); return true;
    }

    public bool SetTaskPriority(string id, string priority)
    {
        bool fire = false;
        lock (syncRoot)
        {
            var task = state.Tasks.FirstOrDefault(x => x.Id == id);
            if (task == null || priority is not (TaskRecord.PriorityLow or TaskRecord.PriorityNormal or TaskRecord.PriorityHigh)) return false;
            task.Priority = priority; task.UpdatedAt = NowProvider();
            SaveLocked(); fire = true;
        }
        if (fire) Changed?.Invoke(); return true;
    }

    public bool SetTaskDue(string id, DateTime? dueAt)
    {
        bool fire = false;
        lock (syncRoot)
        {
            var task = state.Tasks.FirstOrDefault(x => x.Id == id);
            if (task == null) return false;
            task.DueAt = dueAt; task.UpdatedAt = NowProvider();
            SaveLocked(); fire = true;
        }
        if (fire) Changed?.Invoke(); return true;
    }

    public bool SetTaskStatus(string id, string status)
    {
        bool fire = false;
        lock (syncRoot)
        {
            var task = state.Tasks.FirstOrDefault(x => x.Id == id);
            if (task == null || status is not (TaskRecord.StatusOpen or TaskRecord.StatusDoing or TaskRecord.StatusDone)) return false;
            task.Status = status;
            task.DoneAt = status == TaskRecord.StatusDone ? NowProvider() : null;
            task.UpdatedAt = NowProvider();
            SaveLocked(); fire = true;
        }
        if (fire) Changed?.Invoke(); return true;
    }

    /// <summary>Permanent removal of exactly one record; exports and the action audit stay untouched.</summary>
    public bool DeleteTask(string id)
    {
        bool fire = false;
        lock (syncRoot)
        {
            if (state.Tasks.RemoveAll(x => x.Id == id) == 0) return false;
            SaveLocked(); fire = true;
        }
        if (fire) Changed?.Invoke(); return true;
    }

    public ReminderRecord? AddReminder(string text, DateTime remindAt, string projectId)
    {
        bool fire = false;
        ReminderRecord created;
        lock (syncRoot)
        {
            text = (text ?? "").Trim();
            if (state.Reminders.Count(x => x.NotifiedAt == null) >= MaxReminders) { LastStorageError = "Osiągnięto limit 500 aktywnych przypomnień."; return null; }
            if (text.Length is < 1 or > 500) { LastStorageError = "Przypomnienie musi mieć 1–500 znaków."; return null; }
            if (remindAt <= NowProvider()) { LastStorageError = "Termin przypomnienia jest w przeszłości — nic nie ustawiono."; return null; }
            created = new() { Id = Guid.NewGuid().ToString("N")[..12], Text = text, RemindAt = remindAt, ProjectId = projectId ?? "", CreatedAt = NowProvider() };
            state.Reminders.Add(created);
            SaveLocked();
            fire = true;
        }
        if (fire) Changed?.Invoke();
        return Clone(created);
    }

    public bool DeleteReminder(string id)
    {
        bool fire = false;
        lock (syncRoot)
        {
            if (state.Reminders.RemoveAll(x => x.Id == id) == 0) return false;
            SaveLocked(); fire = true;
        }
        if (fire) Changed?.Invoke(); return true;
    }

    /// <summary>Marks due reminders as notified and returns them. At startup everything overdue is flagged Missed — the app cannot remind while closed.</summary>
    public IReadOnlyList<ReminderRecord> CheckDue(bool atStartup = false)
    {
        var fired = new List<ReminderRecord>();
        bool fire = false;
        lock (syncRoot)
        {
            var now = NowProvider();
            foreach (var reminder in state.Reminders.Where(x => x.NotifiedAt == null && x.RemindAt <= now))
            {
                reminder.NotifiedAt = now;
                reminder.Missed = atStartup;
                fired.Add(Clone(reminder));
            }
            if (fired.Count > 0) { SaveLocked(); fire = true; }
        }
        foreach (var reminder in fired) ReminderFired?.Invoke("⏰ " + reminder.Text);
        if (fire) Changed?.Invoke();
        return fired;
    }

    internal bool VerifyPersistedState(out string evidence)
    {
        lock (syncRoot)
        {
            try
            {
                if (LastStorageError != null) { evidence = LastStorageError; return false; }
                string expected = JsonSerializer.Serialize(state, jsonOptions);
                if (File.ReadAllText(storePath, Encoding.UTF8) != expected) { evidence = "Zapis zadań nie odpowiada bieżącemu stanowi."; return false; }
                evidence = $"Odczyt zwrotny: {storePath}; SHA-256: {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(storePath)))}; zadania: {state.Tasks.Count}, przypomnienia: {state.Reminders.Count}.";
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { evidence = "Nie można sprawdzić zapisu zadań: " + ex.Message; return false; }
        }
    }

    private void Load()
    {
        lock (syncRoot)
        {
            try
            {
                if (!File.Exists(storePath)) return;
                if (new FileInfo(storePath).Length > 5 * 1024 * 1024) throw new IOException("Plik zadań przekracza 5 MB.");
                var loaded = JsonSerializer.Deserialize<TaskState>(File.ReadAllText(storePath, Encoding.UTF8), jsonOptions);
                if (loaded == null) throw new JsonException("Pusty plik zadań.");
                state = loaded;
                state.Tasks ??= []; state.Reminders ??= [];
                foreach (var task in state.Tasks)
                {
                    if (task.Id.Length == 0) task.Id = Guid.NewGuid().ToString("N")[..12];
                    if (task.Status.Length == 0) task.Status = TaskRecord.StatusOpen;
                    if (task.Priority.Length == 0) task.Priority = TaskRecord.PriorityNormal;
                }
                foreach (var reminder in state.Reminders)
                    if (reminder.Id.Length == 0) reminder.Id = Guid.NewGuid().ToString("N")[..12];
                LastStorageError = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                state = new();
                LastStorageError = "Nie udało się wczytać zadań: " + ex.Message;
                try { if (File.Exists(storePath)) File.Copy(storePath, storePath + $".damaged-{DateTime.Now:yyyyMMddHHmmss}", false); }
                catch (Exception copyEx) when (copyEx is IOException or UnauthorizedAccessException) { LastStorageError += " Nie udało się utworzyć kopii."; }
            }
        }
    }

    private void SaveLocked()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
            string temporary = storePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, jsonOptions), Encoding.UTF8);
            File.Move(temporary, storePath, true);
            LastStorageError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LastStorageError = "Zmiany zadań działają tylko do zamknięcia aplikacji. Błąd zapisu: " + ex.Message; }
    }

    private static TaskRecord Clone(TaskRecord x) => new() { Id = x.Id, Title = x.Title, Priority = x.Priority, Status = x.Status, DueAt = x.DueAt, ProjectId = x.ProjectId, CreatedAt = x.CreatedAt, UpdatedAt = x.UpdatedAt, DoneAt = x.DoneAt };
    private static ReminderRecord Clone(ReminderRecord x) => new() { Id = x.Id, Text = x.Text, RemindAt = x.RemindAt, NotifiedAt = x.NotifiedAt, Missed = x.Missed, ProjectId = x.ProjectId, CreatedAt = x.CreatedAt };
}

/// <summary>Polish date/time phrases → concrete local DateTime. Everything resolves to an absolute, visible moment — never saved as text.</summary>
internal static class PolishTimeParser
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public static bool TryParse(string input, DateTime now, out DateTime when, out string description)
    {
        when = default;
        description = "";
        string text = " " + ConversationMemoryService.Normalize(input).Trim() + " ";

        Match relative = Regex.Match(text, @"\sza (\d+) (min|minuty|minute|minut|godz|godziny|godzine|godzin|dni|dzien)\b");
        if (relative.Success && int.TryParse(relative.Groups[1].Value, out int amount))
        {
            string unit = relative.Groups[2].Value;
            when = unit.StartsWith("min") ? now.AddMinutes(amount)
                 : unit.StartsWith("godz") ? now.AddHours(amount)
                 : now.AddDays(amount).Date.AddHours(9);
            return Describe(when, out description);
        }
        if (text.Contains(" za pol godziny ")) { when = now.AddMinutes(30); return Describe(when, out description); }

        // An explicit calendar date decides the day first; a clock is then searched only after the date,
        // so „12.06 o 18" means June 12th at 18:00, never 12:06.
        Match explicitDate = Regex.Match(text, @"\s(\d{1,2})[.\-/](\d{1,2})(?:[.\-/](\d{2,4}))?\b");
        string timeRegion = explicitDate.Success ? text[(explicitDate.Index + explicitDate.Length)..] : text;
        Match clock = Regex.Match(timeRegion, @"\s(?:o )?(\d{1,2})[:.](\d{2})\b");
        Match hourOnly = Regex.Match(timeRegion, @"\so (\d{1,2})\b");
        TimeSpan? time = null;
        if (clock.Success && int.TryParse(clock.Groups[1].Value, out int hClock) && int.TryParse(clock.Groups[2].Value, out int mClock) && hClock < 24 && mClock < 60)
            time = new TimeSpan(hClock, mClock, 0);
        else if (hourOnly.Success && int.TryParse(hourOnly.Groups[1].Value, out int hOnly) && hOnly < 24)
            time = new TimeSpan(hOnly, 0, 0);
        var defaultTime = new TimeSpan(9, 0, 0); // only a day given → 09:00, always shown in the description

        if (explicitDate.Success && int.TryParse(explicitDate.Groups[1].Value, out int day) && int.TryParse(explicitDate.Groups[2].Value, out int month))
        {
            int year = explicitDate.Groups[3].Success && int.TryParse(explicitDate.Groups[3].Value, out int y) ? (y < 100 ? 2000 + y : y) : now.Year;
            if (day is >= 1 and <= 31 && month is >= 1 and <= 12)
            {
                try
                {
                    var candidate = new DateTime(year, month, day).Add(time ?? defaultTime);
                    if (!explicitDate.Groups[3].Success && candidate <= now) candidate = candidate.AddYears(1);
                    when = candidate;
                    return Describe(when, out description);
                }
                catch (ArgumentOutOfRangeException) { description = "Taka data nie istnieje w kalendarzu."; return false; }
            }
        }

        if (text.Contains(" pojutrze ")) { when = now.Date.AddDays(2).Add(time ?? defaultTime); return Describe(when, out description); }
        if (text.Contains(" jutro ")) { when = now.Date.AddDays(1).Add(time ?? defaultTime); return Describe(when, out description); }
        if (text.Contains(" dzis ") || text.Contains(" dzisiaj "))
        {
            if (time == null) { description = "Podaj godzinę, np. „dziś o 18”."; return false; }
            when = now.Date.Add(time.Value);
            if (when <= now) { description = $"Godzina {time.Value:hh\\:mm} już dziś minęła. Podaj późniejszą godzinę albo „jutro”."; return false; }
            return Describe(when, out description);
        }

        string[] weekday = ["niedziele", "poniedzialek", "wtorek", "srode", "czwartek", "piatek", "sobote"];
        for (int i = 0; i < weekday.Length; i++)
        {
            if (!text.Contains(" " + weekday[i] + " ")) continue;
            int daysAhead = ((i - (int)now.DayOfWeek) % 7 + 7) % 7;
            if (daysAhead == 0 && time.HasValue && now.TimeOfDay >= time.Value) daysAhead = 7;
            if (daysAhead == 0 && time == null) daysAhead = 7; // „w poniedziałek" without a time = next week
            when = now.Date.AddDays(daysAhead).Add(time ?? defaultTime);
            return Describe(when, out description);
        }

        if (time != null)
        {
            // Only a clock time was given: next occurrence — today if still ahead, otherwise tomorrow.
            when = now.Date.Add(time.Value);
            if (when <= now) when = when.AddDays(1);
            return Describe(when, out description);
        }
        description = "Nie rozpoznano daty ani godziny. Spróbuj: „za 30 minut”, „jutro o 18”, „w piątek o 15”, „24.12 o 12”.";
        return false;
    }

    private static bool Describe(DateTime when, out string description)
    {
        description = when.ToString("d MMMM yyyy (dddd), HH:mm", Pl);
        return true;
    }
}
