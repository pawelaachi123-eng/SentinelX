using System.Text;
using System.Text.Json;
using System.IO;

namespace SentinelX;

/// <summary>Jeden wpis harmonogramu: o której godzinie i w które dni uruchomić polecenie.</summary>
public sealed class ScheduleRecord
{
    public string Id { get; set; } = "";
    public string Command { get; set; } = "";
    public int Hour { get; set; }
    public int Minute { get; set; }
    /// <summary>Maska dni: 1=poniedziałek … 64=niedziela (127 = codziennie).</summary>
    public int Days { get; set; } = 127;
    /// <summary>Gdy ustawione — wpis jednorazowy, tylko w tym dniu.</summary>
    public DateTime? Date { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastRun { get; set; }
    public int Skipped { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class ScheduleState
{
    public int Version { get; set; } = 1;
    public List<ScheduleRecord> Schedules { get; set; } = [];
}

/// <summary>0.97 · cron w aplikacji (#007): „zaplanuj: 7:30 dzień dobry” — polecenie uruchamia się
/// samo o umówionej godzinie, <b>dopóki Sentinel jest uruchomiony</b>.
/// <para>Gwarancje jak w pamięci i rutynach: zapis atomowy (plik tymczasowy + Move), kopia
/// uszkodzonego pliku, odczyt zwrotny z SHA-256. <b>Harmonogram nie przyjmuje poleceń
/// niszczących ani wyłączających komputer</b> — takie polecenie wykonuj sam, bo wtedy dostajesz
/// pytanie albo okno zgody. Zamiast Windowsowego harmonogramu zadań: nic nie rejestruję w systemie
/// i nic nie uruchamia się po zamknięciu aplikacji.</para></summary>
public sealed class SchedulerService
{
    public const int MaxSchedules = 24;
    public const int MaxCommandLength = 160;
    public const int CatchUpMinutes = 15;

    /// <summary>Fragmenty niedopuszczalne w harmonogramie: niszczenie danych i zasilanie.
    /// To dokładnie te rzeczy, których Sentinel nie robi bez człowieka przy klawiaturze.</summary>
    private static readonly string[] Forbidden =
    [
        "usun wszystkie wspomnienia", "usun pamiec", "usun rozmowe", "usun archiwum", "usun duplikaty",
        "usun puste pliki", "usun do kosza", "usun zadanie", "usun snapshot", "usun przypomnienie",
        "wyczysc", "sformatuj", "zmien nazwy", "potwierdz",
        "zamknij komputer", "restart komputera", "restartuj", "uspij komputer", "zablokuj ekran",
        "wygasz ekran", "wyczysc pamiec",
    ];

    private readonly object syncRoot = new();
    private readonly string storePath;
    private readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private ScheduleState state = new();

    public string? LastStorageError { get; private set; }
    public string StoragePath => storePath;
    public event Action? Changed;

    public SchedulerService(string? directory = null)
    {
        storePath = Path.Combine(directory ?? AppPaths.MemoryDirectory, "schedules.json");
        Load();
    }

    public IReadOnlyList<ScheduleRecord> GetSchedules()
    {
        lock (syncRoot)
            return state.Schedules.OrderBy(x => x.Hour * 60 + x.Minute).Select(Clone).ToArray();
    }

    /// <summary>Dodaje wpis. Zwraca null + <see cref="LastStorageError"/>, gdy godzina albo polecenie są nie do przyjęcia.</summary>
    public ScheduleRecord? Add(string command, int hour, int minute, int daysMask = 127, DateTime? date = null)
    {
        string clean = (command ?? "").Trim();
        ScheduleRecord saved;
        bool fire = false;
        lock (syncRoot)
        {
            LastStorageError = null;
            if (hour is < 0 or > 23 || minute is < 0 or > 59) { LastStorageError = "Godzina musi być w formacie HH:MM, np. 7:30 albo 18:05."; return null; }
            if (clean.Length is < 2 or > MaxCommandLength) { LastStorageError = "Polecenie musi mieć 2–" + MaxCommandLength + " znaków, np. „zaplanuj: 7:30 dzień dobry”."; return null; }
            if (daysMask is < 1 or > 127) { LastStorageError = "Nie rozpoznałem dni tygodnia. Użyj: codziennie, dni robocze, weekend albo nazw dni (pon, śr, pt)."; return null; }
            string forbidden = ForbiddenFind(clean);
            if (forbidden.Length > 0)
            {
                LastStorageError = "Harmonogram nie uruchomi polecenia „" + forbidden + "”, bo działa bez pytania o zgodę. " +
                    "Takie rzeczy wykonuj samodzielnie — wtedy dostaniesz pytanie albo okno zgody.";
                return null;
            }
            if (state.Schedules.Count >= MaxSchedules) { LastStorageError = "Osiągnięto limit " + MaxSchedules + " wpisów. Usuń jeden: „usuń zaplanowane 1”."; return null; }
            saved = new ScheduleRecord
            {
                Id = Guid.NewGuid().ToString("N")[..10],
                Command = clean,
                Hour = hour,
                Minute = minute,
                Days = daysMask,
                Date = date?.Date,
                CreatedAt = DateTime.Now,
                Enabled = true,
            };
            state.Schedules.Add(saved);
            SaveLocked();
            fire = true;
        }
        if (fire) Changed?.Invoke();
        return LastStorageError == null ? Clone(saved) : null;
    }

    public bool Remove(string numberOrId)
    {
        bool fire = false;
        lock (syncRoot)
        {
            LastStorageError = null;
            string needle = (numberOrId ?? "").Trim();
            var ordered = state.Schedules.OrderBy(x => x.Hour * 60 + x.Minute).ToList();
            ScheduleRecord? target = int.TryParse(needle, out int index) && index >= 1 && index <= ordered.Count
                ? ordered[index - 1]
                : state.Schedules.FirstOrDefault(x => string.Equals(x.Id, needle, StringComparison.OrdinalIgnoreCase));
            if (target == null) { LastStorageError = "Nie mam takiego wpisu harmonogramu. „zaplanowane” pokazuje listę z numerami."; return false; }
            state.Schedules.Remove(target);
            SaveLocked();
            fire = true;
        }
        if (fire) Changed?.Invoke();
        return true;
    }

    public bool Clear()
    {
        bool fire = false;
        lock (syncRoot)
        {
            LastStorageError = null;
            if (state.Schedules.Count == 0) { LastStorageError = "Harmonogram jest już pusty."; return false; }
            state.Schedules.Clear();
            SaveLocked();
            fire = true;
        }
        if (fire) Changed?.Invoke();
        return true;
    }

    /// <summary>Zwraca wpisy, których pora właśnie nadeszła (okno 15 minut od umówionej godziny —
    /// Sentinel nie dogania starszych terminów po nocy z wyłączonym komputerem).</summary>
    public IReadOnlyList<ScheduleRecord> TakeDue(DateTime now)
    {
        var due = new List<ScheduleRecord>();
        lock (syncRoot)
        {
            LastStorageError = null;
            int todayBit = BitFor(now.DayOfWeek);
            bool skipped = false;
            foreach (ScheduleRecord record in state.Schedules)
            {
                if (!record.Enabled) continue;
                if (record.LastRun.HasValue && record.LastRun.Value.Date == now.Date) continue;
                if (record.Date.HasValue && record.Date.Value.Date != now.Date) continue;
                if ((record.Days & todayBit) == 0) continue;
                DateTime moment = now.Date.AddHours(record.Hour).AddMinutes(record.Minute);
                if (now < moment) continue;
                if (now - moment > TimeSpan.FromMinutes(CatchUpMinutes)) { record.Skipped++; record.LastRun = now; skipped = true; continue; }
                due.Add(Clone(record));
            }
            if (due.Count > 0)
            {
                foreach (ScheduleRecord record in state.Schedules)
                {
                    ScheduleRecord? fired = due.FirstOrDefault(x => string.Equals(x.Id, record.Id, StringComparison.Ordinal));
                    if (fired == null) continue;
                    record.LastRun = now;
                    if (record.Date.HasValue) record.Enabled = false; // jednorazowy: po wykonaniu nie wraca
                }
                SaveLocked();
            }
            else if (skipped) SaveLocked();
        }
        return due;
    }

    /// <summary>Najbliższe planowane uruchomienie danego wpisu (do podania w liście).</summary>
    public static DateTime? NextRun(ScheduleRecord record, DateTime now)
    {
        if (!record.Enabled) return null;
        if (record.Date.HasValue)
        {
            DateTime once = record.Date.Value.Date.AddHours(record.Hour).AddMinutes(record.Minute);
            return once >= now ? once : null;
        }
        for (int offset = 0; offset < 8; offset++)
        {
            DateTime day = now.Date.AddDays(offset);
            if ((record.Days & BitFor(day.DayOfWeek)) == 0) continue;
            DateTime moment = day.AddHours(record.Hour).AddMinutes(record.Minute);
            if (moment >= now) return moment;
        }
        return null;
    }

    public static string DaysText(int mask)
    {
        if (mask == 127) return "codziennie";
        if (mask == 31) return "dni robocze";
        if (mask == 96) return "weekend";
        string[] names = ["pon", "wt", "śr", "czw", "pt", "sb", "nd"];
        var chosen = new List<string>();
        for (int i = 0; i < 7; i++) if ((mask & (1 << i)) != 0) chosen.Add(names[i]);
        return chosen.Count == 0 ? "nigdy" : string.Join(", ", chosen);
    }

    public static int BitFor(DayOfWeek day) => 1 << ((int)day == 0 ? 6 : (int)day - 1);

    /// <summary>Zamienia polskie określenie dni na maskę. Zwraca 0, gdy nie rozpoznano.</summary>
    public static int ParseDays(string? text)
    {
        string value = ConversationMemoryService.Normalize(text ?? "");
        if (value.Length == 0) return 127;
        if (value.Contains("codzien", StringComparison.Ordinal) || value.Contains("kazd", StringComparison.Ordinal)) return 127;
        if (value.Contains("robocz", StringComparison.Ordinal) || value.Contains("prac", StringComparison.Ordinal)) return 31;
        if (value.Contains("weekend", StringComparison.Ordinal)) return 96;
        int mask = 0;
        string[] keys = ["pon", "wt", "sr", "czw", "pt", "sb", "nd"];
        for (int i = 0; i < keys.Length; i++) if (value.Contains(keys[i], StringComparison.Ordinal)) mask |= 1 << i;
        return mask;
    }

    public string Describe(DateTime? now = null)
    {
        DateTime moment = now ?? DateTime.Now;
        var schedules = GetSchedules();
        if (schedules.Count == 0)
            return "Harmonogram jest pusty. Dodaj: „zaplanuj: 7:30 dzień dobry” albo „zaplanuj w dni robocze 8:00 zadania”.";
        var lines = schedules.Select((x, i) =>
        {
            DateTime? next = NextRun(x, moment);
            string status = !x.Enabled ? " [wykonane jednorazowo]" : x.LastRun?.Date == moment.Date ? " [dziś już uruchomione]" : "";
            string when = next.HasValue ? "następne: " + next.Value.ToString("ddd d.MM HH:mm", new System.Globalization.CultureInfo("pl-PL")) : "brak najbliższego terminu";
            return $"{i + 1}. {x.Hour:00}:{x.Minute:00} · {DaysText(x.Days)} · „{x.Command}” — {when}{status}" +
                (x.Skipped > 0 ? " (pominięte: " + x.Skipped + ")" : "");
        });
        return "HARMONOGRAM (" + schedules.Count + "/" + MaxSchedules + ") · działa, gdy aplikacja jest uruchomiona\n" + string.Join("\n", lines) +
            "\nUsuń: „usuń zaplanowane 1” albo „usuń wszystkie zaplanowane”. " +
            "Polecenia niszczące i zasilanie harmonogram odrzuca z góry — wykonuj je sam.";
    }

    internal bool VerifyPersistedState(out string evidence)
    {
        lock (syncRoot)
        {
            try
            {
                if (LastStorageError != null) { evidence = LastStorageError; return false; }
                string expected = JsonSerializer.Serialize(state, jsonOptions);
                if (File.ReadAllText(storePath, Encoding.UTF8) != expected) { evidence = "Zapis harmonogramu nie odpowiada bieżącemu stanowi."; return false; }
                evidence = "Odczyt zwrotny: " + storePath + "; SHA-256: " + Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(storePath))) + "; wpisów: " + state.Schedules.Count + ".";
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { evidence = "Nie można sprawdzić zapisu harmonogramu: " + ex.Message; return false; }
        }
    }

    private static string ForbiddenFind(string command)
    {
        string normalized = ConversationMemoryService.Normalize(command);
        foreach (string marker in Forbidden)
            if (normalized.Contains(ConversationMemoryService.Normalize(marker), StringComparison.Ordinal)) return marker;
        return "";
    }

    private static ScheduleRecord Clone(ScheduleRecord source) => new()
    {
        Id = source.Id, Command = source.Command, Hour = source.Hour, Minute = source.Minute, Days = source.Days,
        Date = source.Date, CreatedAt = source.CreatedAt, LastRun = source.LastRun, Skipped = source.Skipped, Enabled = source.Enabled
    };

    private void Load()
    {
        lock (syncRoot)
        {
            try
            {
                if (!File.Exists(storePath)) { state = new(); SaveLocked(); return; }
                if (new FileInfo(storePath).Length > 2 * 1024 * 1024) throw new IOException("Plik harmonogramu przekracza 2 MB.");
                var loaded = JsonSerializer.Deserialize<ScheduleState>(File.ReadAllText(storePath, Encoding.UTF8), jsonOptions);
                if (loaded == null) throw new JsonException("Pusty plik harmonogramu.");
                state = loaded;
                state.Schedules ??= [];
                foreach (ScheduleRecord record in state.Schedules)
                {
                    if (record.Id.Length == 0) record.Id = Guid.NewGuid().ToString("N")[..10];
                    if (record.Days is < 1 or > 127) record.Days = 127;
                    record.Hour = Math.Clamp(record.Hour, 0, 23);
                    record.Minute = Math.Clamp(record.Minute, 0, 59);
                }
                LastStorageError = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                state = new();
                LastStorageError = "Nie udało się wczytać harmonogramu: " + ex.Message;
                try { if (File.Exists(storePath)) File.Copy(storePath, storePath + ".damaged-" + DateTime.Now.ToString("yyyyMMddHHmmss"), false); }
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastStorageError = "Nie udało się zapisać harmonogramu: " + ex.Message;
        }
    }
}
