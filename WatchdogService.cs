using System.Text;
using System.IO;

namespace SentinelX;

/// <summary>Jedno zdarzenie wykryte w obserwowanym folderze.</summary>
public sealed record WatchEvent(DateTime At, string Path, string Kind)
{
    public override string ToString() => At.ToString("HH:mm:ss") + " · " + Kind + " · " + Path;
}

/// <summary>0.97 · watchdog folderów (#008): „obserwuj: Dokumenty” — Sentinel co jakiś czas porównuje
/// zawartość folderu z poprzednim obrazem i mówi, co się pojawiło, zmieniło albo zniknęło.
/// <para>Świadomie <b>tylko obserwuje</b>: nie przenosi, nie kasuje i nie uruchamia akcji po
/// wykryciu zmiany — decyzja należy do Ciebie. Działa tylko, gdy aplikacja jest uruchomiona
/// (nie rejestruję się w systemie plików na stałe i nie wstawiam nic do autostartu).</para></summary>
public sealed class WatchdogService
{
    public const int MaxFolders = 6;
    public const int MaxEvents = 200;
    public const int MaxFilesPerFolder = 800;
    public const int MaxEventsPerPoll = 12;
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(20);

    private readonly object syncRoot = new();
    private readonly List<string> folders = [];
    private readonly Dictionary<string, Dictionary<string, (long Bytes, long Ticks)>> snapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<WatchEvent> events = [];
    private DateTime lastPoll = DateTime.MinValue;

    public string? LastError { get; private set; }

    public IReadOnlyList<string> Folders()
    {
        lock (syncRoot) return folders.ToArray();
    }

    /// <summary>Dodaje folder do obserwacji. Zwraca false + <see cref="LastError"/>, gdy folder nie istnieje albo limit.</summary>
    public bool Add(string path)
    {
        lock (syncRoot)
        {
            LastError = null;
            string clean = (path ?? "").Trim().Trim('"');
            if (!Directory.Exists(clean)) { LastError = "Nie ma takiego folderu: „" + clean + "”. Podaj pełną ścieżkę, np. obserwuj: C:\\Users\\Twój-login\\Dokumenty"; return false; }
            if (folders.Contains(clean, StringComparer.OrdinalIgnoreCase)) { LastError = "Ten folder już obserwuję: " + clean; return false; }
            if (folders.Count >= MaxFolders) { LastError = "Obserwuję już " + MaxFolders + " folderów (limit). Usuń któryś: „przestań obserwować 1”."; return false; }
            folders.Add(clean);
            snapshots[clean] = Snapshot(clean, out _);
            JsonLog.Write("watchdog", "Dodano folder do obserwacji.", clean);
            return true;
        }
    }

    public bool Remove(string numberOrPath)
    {
        lock (syncRoot)
        {
            LastError = null;
            string needle = (numberOrPath ?? "").Trim().Trim('"');
            string? target = int.TryParse(needle, out int index) && index >= 1 && index <= folders.Count
                ? folders[index - 1]
                : folders.FirstOrDefault(x => x.Equals(needle, StringComparison.OrdinalIgnoreCase));
            if (target == null) { LastError = "Nie obserwuję takiego folderu. „obserwowane” pokazuje listę z numerami."; return false; }
            folders.Remove(target);
            snapshots.Remove(target);
            return true;
        }
    }

    public bool Clear()
    {
        lock (syncRoot)
        {
            LastError = null;
            if (folders.Count == 0 && events.Count == 0) { LastError = "Lista obserwowanych folderów jest już pusta."; return false; }
            folders.Clear(); snapshots.Clear(); events.Clear();
            return true;
        }
    }

    /// <summary>Porównuje foldery z poprzednim obrazem. Wołana z tiku interfejsu (nie częściej niż co 20 s).
    /// Zwraca nowo wykryte zdarzenia; bez zmian zwraca pustą listę.</summary>
    public IReadOnlyList<WatchEvent> Poll(DateTime? now = null)
    {
        DateTime moment = now ?? DateTime.Now;
        lock (syncRoot)
        {
            LastError = null;
            if (folders.Count == 0) return [];
            if (lastPoll != DateTime.MinValue && moment - lastPoll < MinimumInterval) return [];
            lastPoll = moment;

            var fresh = new List<WatchEvent>();
            foreach (string folder in folders)
            {
                Dictionary<string, (long Bytes, long Ticks)> current = Snapshot(folder, out _);
                Dictionary<string, (long Bytes, long Ticks)> previous = snapshots.TryGetValue(folder, out var old) ? old : [];
                foreach ((string path, (long bytes, long ticks)) in current)
                {
                    if (!previous.TryGetValue(path, out var before)) fresh.Add(new WatchEvent(moment, path, "nowy"));
                    else if (before.Bytes != bytes || before.Ticks != ticks) fresh.Add(new WatchEvent(moment, path, "zmieniony"));
                }
                foreach (string path in previous.Keys)
                    if (!current.ContainsKey(path)) fresh.Add(new WatchEvent(moment, path, "usunięty"));
                snapshots[folder] = current;
            }
            if (fresh.Count > MaxEventsPerPoll)
            {
                fresh = fresh.Take(MaxEventsPerPoll).ToList();
                fresh.Add(new WatchEvent(moment, "(więcej zmian niż pokazuję)", "ograniczenie"));
            }
            events.InsertRange(0, fresh.AsEnumerable().Reverse());
            if (events.Count > MaxEvents) events.RemoveRange(MaxEvents, events.Count - MaxEvents);
            return fresh;
        }
    }

    public IReadOnlyList<WatchEvent> Recent(int count = 20)
    {
        lock (syncRoot) return events.Take(Math.Max(1, count)).ToArray();
    }

    public string Describe()
    {
        lock (syncRoot)
        {
            var builder = new StringBuilder();
            if (folders.Count == 0)
            {
                builder.AppendLine("Nie obserwuję żadnego folderu. Dodaj: „obserwuj: C:\\Users\\Twój-login\\Pobrane”.");
                builder.AppendLine("Obserwacja to tylko podgląd zmian — nic nie przenoszę, nie kasuję i nie uruchamiam akcji.");
                return builder.ToString().TrimEnd();
            }
            builder.AppendLine("OBSERWOWANE FOLDERY (" + folders.Count + "/" + MaxFolders + ") · sprawdzam co " +
                (int)MinimumInterval.TotalSeconds + " s, tylko gdy aplikacja działa");
            foreach ((string folder, int index) in folders.Select((x, i) => (x, i)))
            {
                int files = snapshots.TryGetValue(folder, out var snap) ? snap.Count : 0;
                builder.AppendLine($"{index + 1}. {folder} — {files} plików w obrazie");
            }
            IReadOnlyList<WatchEvent> recent = events.Take(12).ToArray();
            builder.AppendLine();
            builder.AppendLine(recent.Count == 0 ? "Od dodania folderów nic się nie zmieniło."
                : "OSTATNIE ZMIANY (od najnowszej):");
            foreach (WatchEvent item in recent) builder.AppendLine("· " + item);
            builder.AppendLine();
            builder.AppendLine("Zapytaj: „co nowego w folderze” · przestań: „przestań obserwować 1”. Zdarzenia żyją tylko w tej sesji.");
            return builder.ToString().TrimEnd();
        }
    }

    private static Dictionary<string, (long Bytes, long Ticks)> Snapshot(string folder, out bool truncated)
    {
        var map = new Dictionary<string, (long, long)>(StringComparer.OrdinalIgnoreCase);
        truncated = false;
        try
        {
            foreach (string file in Directory.EnumerateFiles(folder))
            {
                if (map.Count >= MaxFilesPerFolder) { truncated = true; break; }
                try
                {
                    var info = new FileInfo(file);
                    map[file] = (info.Length, info.LastWriteTimeUtc.Ticks);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            foreach (string child in Directory.EnumerateDirectories(folder))
            {
                if (map.Count >= MaxFilesPerFolder) { truncated = true; break; }
                try
                {
                    foreach (string file in Directory.EnumerateFiles(child))
                    {
                        if (map.Count >= MaxFilesPerFolder) { truncated = true; break; }
                        try { var info = new FileInfo(file); map[file] = (info.Length, info.LastWriteTimeUtc.Ticks); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException) { }
        return map;
    }
}
