using System.IO;
using System.Text.Json;

namespace SentinelX.Core;

/// <summary>0.95 · self-repair: bezpieczne naprawy DANYCH SentinelX (magazyny JSON, dzienniki JSONL,
/// nauczone wzorce). Nigdy nie zmienia kodu, nigdy nie kasuje treści użytkownika — uszkodzone pliki
/// są przywracane z kopii albo odkładane na bok z pełnym raportem. Wszystkie metody działają na
/// wskazanej ścieżce, więc są testowalne na plikach tymczasowych.</summary>
public static class SelfRepair
{
    /// <summary>Unikalna ścieżka parkingowa dla uszkodzonego pliku (data + licznik) —
    /// dwa zdarzenia w tej samej sekundzie nigdy nie walczą o tę samą nazwę.</summary>
    internal static string UniqueAsidePath(string path)
    {
        string candidate = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        int counter = 1;
        while (File.Exists(candidate) || Directory.Exists(candidate)) candidate = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + counter++;
        return candidate;
    }
    /// <summary>Dziennik JSONL (np. Lessons.jsonl): odrzuca nieczytelne linie, zachowuje dobre.
    /// Zwraca liczbę usuniętych linii.</summary>
    public static int RepairJsonl(string path, out string report)
    {
        string name = Path.GetFileName(path);
        if (!File.Exists(path)) { report = "· ✓ " + name + " — jeszcze nie istnieje"; return 0; }
        try
        {
            var kept = new List<string>();
            int dropped = 0;
            foreach (string line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try { JsonSerializer.Deserialize<JsonElement>(line); kept.Add(line); }
                catch (JsonException) { dropped++; }
            }
            if (dropped > 0)
            {
                string aside = UniqueAsidePath(path);
                File.Copy(path, aside, false);
                File.WriteAllLines(path, kept);
                report = "· ⚠ " + name + " — usunięto " + dropped + " nieczytelnych linii (zostało " + kept.Count +
                    "); oryginał leży obok jako " + Path.GetFileName(aside);
            }
            else report = "· ✓ " + name + " — " + kept.Count + " linii, wszystkie czytelne";
            return dropped;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { report = "· ⚠ " + name + " — nie udało się naprawić: " + ex.Message; return 0; }
    }

    /// <summary>Magazyn JSON: jeśli główny plik jest nieczytelny, przywraca najnowszą czytelną kopię
    /// (szuka „*.bak” i „*backup*” obok pliku); gdy kopii brak — odkłada uszkodzony plik na bok,
    /// żeby magazyn mógł odtworzyć się przy następnym zapisie. Zwraca 1, gdy cokolwiek naprawiono.</summary>
    public static int RepairJsonStore(string path, out string report)
    {
        string name = Path.GetFileName(path);
        if (!File.Exists(path)) { report = "· ✓ " + name + " — jeszcze nie istnieje (powstanie przy pierwszym zapisie)"; return 0; }
        try
        {
            string content = File.ReadAllText(path);
            if (content.Trim().Length == 0 || Parses(content))
            {
                // Pusty albo poprawny — zostaje jak jest.
                report = "· ✓ " + name + (content.Trim().Length == 0 ? " — pusty, ale czytelny" : " — czytelny, składnia JSON poprawna");
                return 0;
            }

            // Uszkodzony — szukaj czytelnej kopii: *.bak, *backup* (najnowsza pierwsza).
            string directory = Path.GetDirectoryName(path) ?? ".";
            string baseName = Path.GetFileName(path);
            var candidates = new List<string>();
            try
            {
                candidates.AddRange(Directory.EnumerateFiles(directory, baseName + "*.bak"));
                candidates.AddRange(Directory.EnumerateFiles(directory, baseName + "*backup*"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            foreach (string backup in candidates.OrderByDescending(x => x, StringComparer.Ordinal))
            {
                try
                {
                    string backupContent = File.ReadAllText(backup);
                    if (!Parses(backupContent)) continue;
                    string aside = UniqueAsidePath(path);
                    File.Copy(path, aside, false);
                    File.Copy(backup, path, true);
                    report = "· ⚠ " + name + " — był uszkodzony; przywrócono z kopii " + Path.GetFileName(backup) +
                        " (uszkodzony plik leży obok jako " + Path.GetFileName(aside) + ")";
                    return 1;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }

            // Brak kopii — odkładamy uszkodzony plik na bok (nigdy nie kasujemy).
            string parked = UniqueAsidePath(path);
            File.Move(path, parked);
            report = "· ⚠ " + name + " — był uszkodzony i nie miał czytelnej kopii; przeniosłem go na bok jako " +
                Path.GetFileName(parked) + " (magazyn odtworzy się przy następnym zapisie)";
            return 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { report = "· ⚠ " + name + " — nie udało się naprawić: " + ex.Message; return 0; }
    }

    private static bool Parses(string content)
    {
        try { JsonSerializer.Deserialize<JsonElement>(content); return true; }
        catch (JsonException) { return false; }
    }
}
