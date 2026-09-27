using System.IO;
using System.Linq;

namespace SentinelX.Tests;

/// <summary>0.96 · JARVIS — regresje warstwy sterowania pulpitem, rutyn, briefingu i narzędzi pomocniczych.
/// <para>Zasada testów: nic, co mogłoby zmienić stan maszyny CI (blokada ekranu, zamykanie, multimedia,
/// głośność) nie jest tu wywoływane. Sprawdzam księgowość uzbrajania akcji (Arm/Abort), czyste funkcje
/// (briefing, historia schowka, szukanie plików), magazyn rutyn (katalog tymczasowy) i to, że
/// niedostępna maszyna dostaje uczciwą odpowiedź zamiast ciszy.</para></summary>
internal static class JarvisRegression
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);

        // ============================ rutyny (magazyn) ============================
        string store = Path.Combine(directory, "routines");
        var routines = new RoutineService(store);
        Check(routines.GetRoutines().Count == 3, "the first run seeds three read-only starter routines, got " + routines.GetRoutines().Count);
        Check(routines.Find("poranek") != null, "the morning routine exists by name");
        Check(routines.Find("nie-ma-takiej") == null, "an unknown routine is not invented");
        Check(routines.Find("PORANEK") != null, "routine names are case-insensitive");
        Check(routines.GetRoutines().All(x => x.Steps.Count > 0), "every starter routine has steps");

        var saved = routines.Save("testowa", ["która godzina", "zadania"]);
        Check(saved != null && saved.Steps.Count == 2, "a routine saves its steps");
        Check(routines.Find("testowa")!.Steps.SequenceEqual(new[] { "która godzina", "zadania" }), "steps survive the round trip");

        // Persistence: a fresh service over the same file must see the same routines.
        Check(new RoutineService(store).Find("testowa") != null, "routines survive a reload from disk");

        // Safety: a routine runs without asking, so it must never carry destructive commands.
        Check(routines.Save("zla", ["usuń wszystkie wspomnienia"]) == null, "a destructive step is refused");
        Check((routines.LastStorageError ?? "").Contains("niszczących"), "the refusal explains itself: " + routines.LastStorageError);
        Check(routines.Save("zla2", ["potwierdz"]) == null, "a step that would confirm a pending action is refused");
        Check(routines.Save("pusta", []) == null, "an empty routine is refused");
        Check(routines.Save("", ["zadania"]) == null, "an unnamed routine is refused");
        Check(routines.Save("za dluga", Enumerable.Range(1, RoutineService.MaxSteps + 1).Select(i => "krok " + i)) == null,
            "the step limit is enforced");
        Check(routines.Describe().Contains("testowa"), "„rutyny” lists the routine");

        Check(routines.Remove("testowa"), "a routine is removed on request");
        Check(routines.Find("testowa") == null, "a removed routine is gone");
        Check(!routines.Remove("testowa"), "removing twice reports it honestly");
        Check((routines.LastStorageError ?? "").Contains("Nie mam rutyny"), "the second removal explains itself");

        Check(routines.VerifyPersistedState(out string evidence), "the routine store verifies against its own file: " + evidence);
        Check(evidence.Contains("SHA-256"), "the verification carries a hash as evidence");

        // A damaged file must be set aside, never overwritten with an empty state.
        string damagedDir = Path.Combine(directory, "routines-damaged");
        Directory.CreateDirectory(damagedDir);
        File.WriteAllText(Path.Combine(damagedDir, "routines.json"), "{ to nie jest json");
        var damaged = new RoutineService(damagedDir);
        Check(damaged.LastStorageError != null, "a damaged store is reported, not silently swallowed");
        Check(Directory.GetFiles(damagedDir).Any(x => x.Contains(".damaged-")), "a damaged store is copied aside before anything else happens");

        // ============================ rutyny (komendy) ============================
        var runRoutines = new RoutineService(Path.Combine(directory, "routines-commands"));
        string added = await RoutineCommands.TryHandleAsync("dodaj rutynę: test = która godzina | zadania",
            ConversationMemoryService.Normalize("dodaj rutynę: test = która godzina | zadania"), runRoutines, Run, default)
            ?? throw new InvalidOperationException("adding a routine must be handled");
        Check(added.Contains("test") && added.Contains("która godzina"), "the add reply shows the routine: " + added);

        var executed = new List<string>();
        string ran = await RoutineCommands.TryHandleAsync("uruchom rutynę: test", "uruchom rutyne: test", runRoutines,
            (step, _) => { executed.Add(step); return Task.FromResult("wynik: " + step); }, default)
            ?? throw new InvalidOperationException("running a routine must be handled");
        Check(executed.SequenceEqual(new[] { "która godzina", "zadania" }), "steps run in order, got: " + string.Join(" | ", executed));
        Check(ran.Contains("która godzina") && ran.Contains("Koniec rutyny"), "the run reply reports every step");

        string unknown = await RoutineCommands.TryHandleAsync("uruchom rutynę: nie-ma-takiej", "uruchom rutyne: nie-ma-takiej",
            runRoutines, Run, default) ?? throw new InvalidOperationException("an unknown routine must be handled");
        Check(unknown.Contains("Nie mam rutyny"), "an unknown routine is refused by name");

        Check(await RoutineCommands.TryHandleAsync("coś zupełnie innego", "cos zupelnie innego", runRoutines, Run, default) == null,
            "an unrelated sentence is not a routine command");
        Check(await RoutineCommands.TryHandleAsync("rutyny", "rutyny", runRoutines, Run, default) != null, "„rutyny” is a routine command");

        // ============================ briefing ============================
        var now = new DateTime(2026, 9, 27, 8, 15, 0);
        var data = new Core.BriefingData(now, "Paweł", ["‼ raport (termin minął)"], ["kupić mleko"], ["⏰ 18:00 telefon"], [],
            ["CPU: 12% · RAM: 8,0 z 16,0 GB (50%)"], ["ulubiony kolor: cyjan"]);
        string morning = Core.JarvisBriefing.Morning(data);
        Check(morning.StartsWith("Dzień dobry, Paweł."), "the morning briefing greets by name: " + morning.Split('\n')[0]);
        Check(morning.Contains("27 września 2026"), "the briefing carries a concrete date");
        Check(morning.Contains("kupić mleko") && morning.Contains("raport") && morning.Contains("18:00"), "the briefing lists tasks and reminders");
        Check(morning.Contains("cyjan"), "the briefing includes pinned memory");
        Check(morning.Contains("CPU"), "the briefing includes system readings");

        string evening = Core.JarvisBriefing.Evening(data);
        Check(evening.StartsWith("Dobranoc, Paweł."), "the evening briefing greets by name");
        Check(evening.Contains("ZROBIONE DZISIAJ") && evening.Contains("ZOSTAJE NA JUTRO"), "the evening briefing closes the day");
        Check(evening.Contains("anuluj zamknięcie"), "the evening briefing points at the escape hatch");

        var empty = Core.BriefingData.Empty(now);
        string emptyMorning = Core.JarvisBriefing.Morning(empty);
        Check(!emptyMorning.Contains(", ."), "an unknown name does not leave an empty greeting");
        Check(emptyMorning.Contains("nic nie zaplanowałeś"), "an empty day is reported as empty, never invented");

        // ============================ historia schowka ============================
        Core.ClipboardHistory.Clear();
        Check(Core.ClipboardHistory.Entries().Count == 0, "the clipboard history starts empty");
        Check(Core.ClipboardHistory.Describe().Contains("pusta"), "an empty history says so");
        Core.ClipboardHistory.Record("pierwszy");
        Core.ClipboardHistory.Record("drugi");
        Core.ClipboardHistory.Record("pierwszy");
        Check(Core.ClipboardHistory.Entries().Count == 2, "a duplicate does not take a second slot");
        Check(Core.ClipboardHistory.Get(1) == "pierwszy", "the newest entry is first");
        Check(Core.ClipboardHistory.Get(2) == "drugi", "the older entry is second");
        Check(Core.ClipboardHistory.Get(3) == null, "a missing entry is null");
        for (int i = 0; i < 20; i++) Core.ClipboardHistory.Record("wpis " + i);
        Check(Core.ClipboardHistory.Entries().Count <= 12, "the history is a bounded ring buffer");
        Check(Core.ClipboardHistory.Describe().Contains("wpis 19"), "the newest copy is described first");
        Core.ClipboardHistory.Clear();

        // ============================ szukanie plików ============================
        string tree = Path.Combine(directory, "drzewo");
        Directory.CreateDirectory(Path.Combine(tree, "głęboko", "dalej", "jeszcze"));
        File.WriteAllText(Path.Combine(tree, "raport-2026.txt"), "x");
        File.WriteAllText(Path.Combine(tree, "notatka.txt"), "x");
        File.WriteAllText(Path.Combine(tree, "głęboko", "raport-wewnetrzny.txt"), "x");
        File.WriteAllText(Path.Combine(tree, "głęboko", "dalej", "jeszcze", "raport-gleboki.txt"), "x");

        var shallow = Core.FileFinder.Find([tree], "raport", maxDepth: 0);
        Check(shallow.Paths.Count == 1, "depth 0 stays in the root folder only, got " + shallow.Paths.Count);
        var deep = Core.FileFinder.Find([tree], "raport", maxDepth: 3);
        Check(deep.Paths.Count == 3, "depth 3 reaches the nested copies, got " + deep.Paths.Count);
        Check(Core.FileFinder.Find([tree], "RAPORT").Paths.Count > 0, "searching ignores letter case");
        Check(Core.FileFinder.Find([tree], "nie-ma-takiego-pliku").Paths.Count == 0, "a miss is an empty result, not an error");
        Check(Core.FileFinder.Find([Path.Combine(directory, "nie-istnieje")], "raport").Paths.Count == 0, "a missing root is tolerated");
        var limited = Core.FileFinder.Find([tree], "raport", maxResults: 1);
        Check(limited.Paths.Count == 1 && limited.Truncated, "the result limit is reported honestly");
        string described = Core.FileFinder.Describe([tree], "raport");
        Check(described.Contains("raport-2026.txt"), "the description lists the found path");

        // ============================ zasilanie: księgowość, nie wykonanie ============================
        Check(Core.PowerManager.Pending.Length == 0, "nothing is armed at start");
        Check(!Core.PowerManager.Abort(), "aborting nothing reports false instead of pretending");
        Check(Core.PowerManager.Arm("nieznane", 60).Length == 0, "an unknown power action is refused");
        string armed = Core.PowerManager.Arm("shutdown", 600);
        Check(armed.Contains("zamknięcie komputera za 600 s"), "arming describes the countdown: " + armed);
        Check(Core.PowerManager.Pending.Contains("zamknięcie"), "the armed action is visible until it is cancelled");
        Check(Core.PowerManager.Abort(), "an armed action can be cancelled");
        Check(Core.PowerManager.Pending.Length == 0, "after cancelling nothing is pending");

        // ============================ okna i narzędzia: uczciwe odpowiedzi ============================
        var windows = Core.WindowManager.ListWindows();
        Check(windows.All(x => x.Title.Length > 0), "a listed window always carries a title");
        string? listed = JarvisToolkit.TryHandle("okna");
        Check(listed != null, "„okna” is handled by the Jarvis layer");
        Check(listed!.Contains("Otwarte okna") || listed.Contains("Nie widzę"), "the window list is honest about an empty desktop: " + listed);
        Check(JarvisToolkit.TryHandle("historia schowka") != null, "„historia schowka” is handled");
        Check(JarvisToolkit.TryHandle("znajdź plik: raport") != null, "„znajdź plik” is handled");
        Check(JarvisToolkit.TryHandle("napisz wiersz o morzu") == null, "a free sentence is not a Jarvis tool");
        Check(JarvisToolkit.TryHandle("") == null, "an empty command is not a Jarvis tool");
        // These must never execute during a test run — parsing only.
        Check(JarvisToolkit.TryHandle("anuluj zamkniecie") != null, "„anuluj zamknięcie” is handled (nothing was armed)");
    }

    private static Task<string> Run(string command, System.Threading.CancellationToken token) => Task.FromResult("ok: " + command);
}
