using System.Text.Json;

namespace SentinelX.Core;

/// <summary>0.95 · bezpieczna pętla uczenia się: zapamiętuje NAUCZONE pary „Twoje wejście → polecenie”,
/// kiedy (a) naprawię literówkę albo (b) zaakceptujesz propozycję „Czy chodziło Ci o…”. Dzięki temu
/// drugi raz to samo wejście rozumiem od razu. To są dane w Memory, NIE kod — aplikacja nie
/// modyfikuje własnych źródeł. Twarde reguły bezpieczeństwa:
/// 1) zapisuję wyłącznie pary, które już przeszły przez filtry (naprawa katalogowa albo propozycja),
/// 2) każdy zapisany cel musi być wolny od słów destrukcyjnych (plik można ręcznie edytować,
///    więc walidacja jest też przy odczycie),
/// 3) niepoprawne wpisy przy odczycie/naprawie są odrzucane i raportowane, nigdy cicho wykonywane.</summary>
public sealed class LearnedPatterns
{
    public const int MaxPatterns = 200;

    private static readonly string[] DestructiveStems =
        ["usun", "zamknij", "wylacz", "czysc", "kill", "sformatuj", "potwierdz", "zatrzymaj", "skasuj"];

    private readonly string filePath;
    private readonly object gate = new();
    private readonly Dictionary<string, (string To, int Count, DateTime At)> patterns = new(StringComparer.Ordinal);

    public LearnedPatterns(string? filePath = null)
    {
        this.filePath = filePath ?? Path.Combine(AppPaths.MemoryDirectory, "LearnedPatterns.json");
        Load();
    }

    /// <summary>Dokładne dopasowanie znormalizowanego wejścia. Bez zgadywania — tylko nauczone pary.</summary>
    public bool TryGet(string input, out string target)
    {
        string key = ConversationMemoryService.Normalize(input ?? "").Trim().TrimEnd('.', '!', '?', ',');
        lock (gate)
        {
            if (key.Length >= 2 && patterns.TryGetValue(key, out var entry)) { target = entry.To; return true; }
        }
        target = "";
        return false;
    }

    /// <summary>Zapisuje parę. Zwraca false, gdy cel jest niebezpieczny/za długi — wtedy nic nie uczy.</summary>
    public bool Record(string from, string to)
    {
        string key = ConversationMemoryService.Normalize(from ?? "").Trim().TrimEnd('.', '!', '?', ',');
        string value = (to ?? "").Trim();
        if (key.Length < 2 || value.Length < 2 || value.Length > 120 || value.Contains('\n')) return false;
        if (!IsSafeTarget(value)) return false;
        lock (gate)
        {
            if (patterns.TryGetValue(key, out var existing)) patterns[key] = (value, existing.Count + 1, DateTime.Now);
            else patterns[key] = (value, 1, DateTime.Now);
            while (patterns.Count > MaxPatterns)
            {
                string oldest = patterns.OrderBy(x => x.Value.Count).ThenBy(x => x.Value.At).First().Key;
                patterns.Remove(oldest);
            }
            SaveLocked();
        }
        return true;
    }

    public int Count { get { lock (gate) return patterns.Count; } }

    public IReadOnlyList<(string From, string To, int Count)> Snapshot()
    {
        lock (gate)
            return patterns.OrderByDescending(x => x.Value.Count).ThenByDescending(x => x.Value.At)
                .Select(x => (x.Key, x.Value.To, x.Value.Count)).ToArray();
    }

    /// <summary>Ludzki opis dla „ulepsz sie”.</summary>
    public string Describe(int max = 8)
    {
        var snapshot = Snapshot();
        if (snapshot.Count == 0) return "Nie mam jeszcze nauczonych wzorców — pierwsza poprawka albo akceptacja propozycji „Czy chodziło Ci o…” mnie czegoś nauczy.";
        var lines = snapshot.Take(max).Select(x => "· „" + x.From + "” → „" + x.To + "”" + (x.Count > 1 ? " (×" + x.Count + ")" : ""));
        return "Nauczone wzorce (" + snapshot.Count + "):\n" + string.Join("\n", lines) +
            (snapshot.Count > max ? "\n… i " + (snapshot.Count - max) + " więcej." : "");
    }

    /// <summary>Self-repair: usuwa niepoprawne wpisy z pliku i zapisuje czystą wersję.
    /// Zwraca liczbę odrzuconych wpisów; raport mówi, co się stało.</summary>
    public int RepairFile(out string report)
    {
        lock (gate)
        {
            int dropped = 0;
            var invalid = patterns.Where(x => !IsSafeTarget(x.Value.To) || x.Key.Length < 2).Select(x => x.Key).ToArray();
            foreach (string key in invalid) { patterns.Remove(key); dropped++; }
            SaveLocked();
            report = dropped == 0
                ? "· ✓ " + Path.GetFileName(filePath) + " — " + patterns.Count + " nauczonych wzorców, wszystkie bezpieczne"
                : "· ⚠ " + Path.GetFileName(filePath) + " — odrzucono " + dropped + " niebezpiecznych wpisów, zostało " + patterns.Count;
            return dropped;
        }
    }

    /// <summary>Twardy filtr bezpieczeństwa: cel nie może zawierać słów destrukcyjnych
    /// (plik danych jest edytowalny ręcznie, więc ufamy walidacji, nie zawartości).</summary>
    public static bool IsSafeTarget(string target)
    {
        string normalized = ConversationMemoryService.Normalize(target ?? "");
        if (normalized.Length == 0) return false;
        return !DestructiveStems.Any(stem => normalized.Contains(stem, StringComparison.Ordinal));
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(filePath)) return;
            string json = File.ReadAllText(filePath);
            if (json.Trim().Length == 0) return;
            using var doc = JsonDocument.Parse(json);
            foreach (var item in doc.RootElement.GetProperty("patterns").EnumerateArray())
            {
                try
                {
                    string from = item.GetProperty("from").GetString() ?? "";
                    string to = item.GetProperty("to").GetString() ?? "";
                    int count = item.TryGetProperty("count", out var c) ? c.GetInt32() : 1;
                    string key = ConversationMemoryService.Normalize(from).Trim().TrimEnd('.', '!', '?', ',');
                    if (key.Length < 2 || !IsSafeTarget(to)) continue; // niepoprawny wpis nigdy nie trafi do użycia
                    patterns[key] = (to, Math.Max(1, count), DateTime.Now);
                }
                catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException) { /* wpis pomijany */ }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
        { /* uszkodzony plik = brak nauczonych wzorców; „napraw sie” zajmie się plikiem */ }
    }

    private void SaveLocked()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            string json = JsonSerializer.Serialize(new
            {
                version = 1,
                patterns = patterns.Select(x => new { from = x.Key, to = x.Value.To, count = x.Value.Count, at = x.Value.At.ToString("O") }).ToArray()
            }, new JsonSerializerOptions { WriteIndented = true });
            string tmp = filePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, filePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { /* zapis najlepszej woli */ }
    }
}
