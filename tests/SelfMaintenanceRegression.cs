using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SentinelX.Tests;

/// <summary>0.95 · self-repair i self-improve: naprawa uszkodzonych danych (przywracanie z kopii,
/// odkładanie na bok, czyszczenie JSONL) i bezpieczna pętla uczenia się (tylko pary, które już
/// przeszły filtry; wpisy destrukcyjne odrzucane także z pliku). Wszystko na plikach tymczasowych.</summary>
internal static class SelfMaintenanceRegression
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    public static Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);

        // --- LearnedPatterns: nauka, walidacja, trwałość, samooczyszczenie ---
        string learnedPath = Path.Combine(directory, "LearnedPatterns.json");
        var learned = new SentinelX.Core.LearnedPatterns(learnedPath);
        Check(learned.Count == 0, "a fresh learned store is empty");
        Check(!learned.TryGet("ile mam ramuu", out _), "nothing is learned yet");
        Check(learned.Describe().Contains("Nie mam jeszcze"), "an empty store says so honestly");

        Check(learned.Record("ile mam ramuu", "ile mam ramu"), "a repaired typo is learnable");
        Check(learned.TryGet("ile mam ramuu", out string target) && target == "ile mam ramu", "the learned pair resolves exactly");
        Check(learned.Count == 1, "one learned pair");

        Check(!learned.Record("zle polecenie", "usun wszystko"), "a destructive target must never be learned");
        Check(!learned.Record("zle polecenie", "zamknij notatnik"), "closing commands are rejected too");
        Check(!learned.Record("x", "ile mam ramu"), "too-short keys are rejected");
        Check(!learned.Record("dobra fraza", "cel\nz druga linia"), "multiline targets are rejected");
        Check(learned.Count == 1, "rejected records leave no trace");

        learned.Record("ststus pamieci", "status pamieci");
        learned.Record("ststus pamieci", "status pamieci");
        Check(learned.Count == 2 && learned.Snapshot().Any(x => x.From == "ststus pamieci" && x.Count == 2), "repeat learning counts up");

        // Trwałość: nowa instancja czyta ten sam plik.
        var reloaded = new SentinelX.Core.LearnedPatterns(learnedPath);
        Check(reloaded.Count == 2 && reloaded.TryGet("ile mam ramuu", out string again) && again == "ile mam ramu", "learning survives a reload");

        // Ręcznie uszkodzony plik (użytkownik może go edytować) — niebezpieczne wpisy nigdy nie zadziałają.
        File.WriteAllText(learnedPath, "{\"version\":1,\"patterns\":[{\"from\":\"cos\",\"to\":\"zamknij notatnik\",\"count\":1},{\"from\":\"inne\",\"to\":\"status pamieci\",\"count\":1}]}");
        var tampered = new SentinelX.Core.LearnedPatterns(learnedPath);
        Check(!tampered.TryGet("cos", out _), "a hand-edited destructive entry must never fire");
        Check(tampered.TryGet("inne", out string safeTarget) && safeTarget == "status pamieci", "safe entries survive tamper filtering");
        int dropped = tampered.RepairFile(out string repairReport);
        Check(dropped == 1 && repairReport.Contains("odrzucono"), "repair drops the unsafe entry and reports it: " + repairReport);

        // --- SelfRepair.RepairJsonl: dziennik z nieczytelnymi liniami ---
        string jsonl = Path.Combine(directory, "Lessons.jsonl");
        File.WriteAllLines(jsonl, ["{\"a\":1}", "NOT JSON AT ALL", "{\"b\":2}", "{broken"]);
        int fixedLines = SentinelX.Core.SelfRepair.RepairJsonl(jsonl, out string jsonlReport);
        Check(fixedLines == 2, "two broken lines are removed, got " + fixedLines);
        Check(File.ReadAllLines(jsonl).Length == 2, "the good lines survive");
        Check(Directory.EnumerateFiles(directory, "Lessons.jsonl.corrupt-*").Any(), "the original is parked, never deleted");
        Check(jsonlReport.Contains("usunięto 2"), "the report counts the fixes: " + jsonlReport);
        Check(SentinelX.Core.SelfRepair.RepairJsonl(jsonl, out string cleanReport) == 0 && cleanReport.Contains("czytelne"), "a clean journal is left alone");
        Check(SentinelX.Core.SelfRepair.RepairJsonl(Path.Combine(directory, "missing.jsonl"), out _) == 0, "a missing file is not an error");

        // --- SelfRepair.RepairJsonStore: przywracanie z kopii i odkładanie na bok ---
        string store = Path.Combine(directory, "store.json");
        File.WriteAllText(store, "{\"ok\":true}");
        Check(SentinelX.Core.SelfRepair.RepairJsonStore(store, out string okReport) == 0 && okReport.Contains("czytelny"), "a valid store is left alone");

        File.WriteAllText(store, "{\"broken\": ");
        File.WriteAllText(store + ".bak", "{\"restored\":true}");
        Check(SentinelX.Core.SelfRepair.RepairJsonStore(store, out string restoredReport) == 1, "a corrupt store with a backup is repaired");
        Check(restoredReport.Contains("przywrócono z kopii"), "the report names the recovery: " + restoredReport);
        Check(File.ReadAllText(store).Contains("restored"), "the backup content is back in place");
        Check(Directory.EnumerateFiles(directory, "store.json.corrupt-*").Any(), "the corrupt original is parked");

        File.WriteAllText(store, "TOTALLY BROKEN");
        File.Delete(store + ".bak"); // w tym scenariuszu nie ma już czytelnej kopii
        Check(SentinelX.Core.SelfRepair.RepairJsonStore(store, out string parkedReport) == 1, "a corrupt store without a readable backup is parked");
        Check(parkedReport.Contains("przeniosłem") && !File.Exists(store), "the broken file is moved aside so the store can re-create itself");
        Check(Directory.EnumerateFiles(directory, "store.json.corrupt-*").Count() >= 2, "both corrupt originals are kept");

        // --- TryParseDuration: czasy dla timerów ---
        Check(UtilityToolbox.TryParseDuration("5 minut", out TimeSpan five, out string fiveDesc) && five == TimeSpan.FromMinutes(5) && fiveDesc == "5 minut", "five minutes parse");
        Check(UtilityToolbox.TryParseDuration("30 sekund", out TimeSpan thirty, out _) && thirty == TimeSpan.FromSeconds(30), "thirty seconds parse");
        Check(UtilityToolbox.TryParseDuration("1 godzina", out TimeSpan one, out _) && one == TimeSpan.FromHours(1), "one hour parses");
        Check(UtilityToolbox.TryParseDuration("2 h", out TimeSpan two, out _) && two == TimeSpan.FromHours(2), "the h shortcut parses");
        Check(!UtilityToolbox.TryParseDuration("duzo", out _, out _), "words alone never parse");
        Check(!UtilityToolbox.TryParseDuration("0 minut", out _, out _), "zero is refused");
        Check(!UtilityToolbox.TryParseDuration("-5 minut", out _, out _), "negative is refused");

        // --- Stopwatch: start, odczyt, stop ---
        Check(!SentinelX.Core.StopwatchRegistry.IsRunning, "the stopwatch starts idle");
        Check(SentinelX.Core.StopwatchRegistry.Status().Contains("nie działa"), "idle status says so");
        Check(SentinelX.Core.StopwatchRegistry.Start().Contains("Stoper wystartował"), "start confirms");
        Check(SentinelX.Core.StopwatchRegistry.IsRunning, "it is running now");
        Check(SentinelX.Core.StopwatchRegistry.Start().Contains("już działa"), "a second start does not reset");
        Check(SentinelX.Core.StopwatchRegistry.Status().Contains("leci"), "status reports elapsed time");
        Check(SentinelX.Core.StopwatchRegistry.Stop().Contains("Stoper zatrzymany"), "stop reports the result");
        Check(!SentinelX.Core.StopwatchRegistry.IsRunning && SentinelX.Core.StopwatchRegistry.Stop().Contains("nie działa"), "stopping twice is honest");

        return Task.CompletedTask;
    }
}
