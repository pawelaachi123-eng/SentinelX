using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SentinelX.Core.Runtime;

namespace SentinelX.Tests;

/// <summary>
/// SEKCJA 1 — regresja rdzenia: event bus, kolejka zadań z ponowieniami, bezpieczniki, cache,
/// cron, maszyna stanów, graf workflow, flagi, zdrowie, metryki, dziennik JSONL, manifest
/// integralności, kopie zapasowe, sejf AES-256-GCM i serializacja binarna.
/// <para>Wszystko działa na zegarze wstrzykiwanym i katalogach tymczasowych — żaden test nie
/// dotyka danych użytkownika ani nie czeka na upływ czasu.</para>
/// </summary>
internal static class CoreRuntimeRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TEST FAILED: " + message);
    }

    public static Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);

        // ---------------- Event bus ----------------
        var bus = new EventBus(maxHistory: 200);
        int exact = 0, wildcard = 0;
        using (bus.Subscribe("kolejka.zadanie", _ => exact++, "test-dokladny"))
        using (bus.Subscribe("kolejka.*", _ => wildcard++, "test-wzorzec"))
        {
            Check(bus.Publish("kolejka.zadanie", "1").Count == 0, "publikacja bez błędów subskrybentów");
            Check(bus.Publish("kolejka.blad", "2").Count == 0, "wzorzec gwiazdki łapie temat");
            bus.Publish("inny.temat", "3");
        }
        Check(exact == 1, "dokładny temat dostaje tylko swoje zdarzenie: " + exact);
        Check(wildcard == 2, "wzorzec „kolejka.*” dostaje dwa zdarzenia: " + wildcard);
        Check(bus.Published == 3 && bus.Delivered == 3, "liczniki publikacji i dostarczeń");
        Check(EventBus.Matches("kolejka.?adanie", "kolejka.zadanie"), "znak „?” dopasowuje jeden znak");
        Check(!EventBus.Matches("kolejka.*", "innakolejka.x"), "wzorzec bez gwiazdki na początku nie łapie prefiksu");
        var failing = new EventBus();
        failing.Subscribe("test", _ => throw new InvalidOperationException("celowo"), "psujacy");
        int after = 0;
        failing.Subscribe("test", _ => after++, "drugi");
        var errors = failing.Publish("test");
        Check(errors.Count == 1 && after == 1, "błąd jednego subskrybenta nie zatrzymuje pozostałych");
        Check(failing.HandlerErrors == 1, "błąd obsługi jest policzony");
        var bounded = new EventBus(maxHistory: 10);
        for (int i = 0; i < 50; i++) bounded.Publish("t", i.ToString());
        Check(bounded.Recent(100).Count == 10, "historia zdarzeń jest ograniczona");
        Check(bounded.Recent(3).Last().Payload == "49", "historia trzyma najnowsze zdarzenia");

        // ---------------- Retry i bezpiecznik ----------------
        var policy = new RetryPolicy(maxAttempts: 3, baseDelay: TimeSpan.FromSeconds(2), multiplier: 2, maxDelay: TimeSpan.FromSeconds(10));
        Check(policy.DelayFor(1) == TimeSpan.Zero, "pierwsza próba bez opóźnienia");
        Check(policy.DelayFor(2) == TimeSpan.FromSeconds(2), "backoff: 2 s");
        Check(policy.DelayFor(3) == TimeSpan.FromSeconds(4), "backoff: 4 s");
        Check(policy.DelayFor(6) == TimeSpan.FromSeconds(10), "opóźnienie ograniczone do maksimum");
        Check(new RetryPolicy(maxAttempts: 99).MaxAttempts == 20, "liczba prób jest ograniczona");

        var now = new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
        var breaker = new CircuitBreaker("test", failureThreshold: 2, openFor: TimeSpan.FromSeconds(30), halfOpenTrials: 1, clock: () => now);
        Check(breaker.TryEnter(out _), "zamknięty obwód przepuszcza");
        breaker.RecordFailure();
        Check(breaker.State == BreakerState.Closed, "jeden błąd nie otwiera obwodu przy progu 2");
        breaker.RecordFailure();
        Check(breaker.State == BreakerState.Open, "próg błędów otwiera obwód");
        Check(!breaker.TryEnter(out string openReason) && openReason.Contains("otwarty"), "otwarty obwód odrzuca z powodem");
        now = now.AddSeconds(31);
        Check(breaker.TryEnter(out _), "po czasie schłodzenia obwód wchodzi w stan półotwarty");
        Check(breaker.State == BreakerState.HalfOpen, "stan półotwarty");
        breaker.RecordSuccess();
        Check(breaker.State == BreakerState.Closed, "sukces zamyka obwód");

        // ---------------- Kolejka zadań ----------------
        var clock = new DateTimeOffset(2026, 9, 27, 8, 0, 0, TimeSpan.Zero);
        var dead = new DeadLetterQueue(capacity: 5);
        var queue = new DurableTaskQueue(new RetryPolicy(maxAttempts: 2, baseDelay: TimeSpan.FromSeconds(5)), dead, clock: () => clock);
        var low = queue.Enqueue("niskie", RuntimePriority.Low);
        var high = queue.Enqueue("wysokie", RuntimePriority.High);
        Check(queue.Next()!.Id == high.Id, "kolejka wybiera wyższy priorytet");
        Check(queue.RunDue(1).Contains("Brak zarejestrowanego wykonawcy"), "bez wykonawcy kolejka odmawia pracy");
        var executed = new List<string>();
        queue.RegisterWorker(task => { executed.Add(task.Name); return null; }, "test");
        var report = queue.RunDue(2);
        Check(executed.SequenceEqual(["wysokie", "niskie"]), "wykonanie po priorytetach: " + string.Join(",", executed));
        Check(queue.Count(RuntimeTaskState.Done) == 2, "dwa zadania wykonane");
        Check(report.Contains("wykonane"), "raport mówi o wykonaniu");

        int attempts = 0;
        queue.RegisterWorker(_ => { attempts++; return "błąd celowy"; }, "psujacy");
        var failingTask = queue.Enqueue("zadanie z błędem", RuntimePriority.Normal);
        string first = queue.RunDue(1);
        Check(attempts == 1 && first.Contains("ponowienie"), "nieudane zadanie wraca do kolejki z terminem: " + first);
        Check(queue.Pending == 1, "zadanie czeka na ponowienie");
        clock = clock.AddSeconds(6);
        queue.RunDue(1);
        Check(attempts == 2, "druga próba po upływie opóźnienia");
        Check(queue.Count(RuntimeTaskState.DeadLettered) == 1, "po wyczerpaniu prób zadanie trafia do kolejki zwrotów");
        Check(dead.Count == 1 && dead.Snapshot()[0].Id == failingTask.Id, "wpis w kolejce zwrotów ma identyfikator zadania");
        Check(dead.TryTake(failingTask.Id, out var letter) && letter!.Task == "zadanie z błędem", "wpis można wskrzesić");
        Check(queue.Cancel(queue.Enqueue("do anulowania").Id), "zadanie oczekujące można anulować");

        // ---------------- Cache LRU/LFU ----------------
        var cacheClock = new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);
        var cache = new LruLfuCache<string, string>(capacity: 2, clock: () => cacheClock);
        cache.Set("A", "1");
        cache.Set("B", "2");
        cache.TryGet("A", out _);
        cache.TryGet("A", out _);
        cache.Set("C", "3");
        Check(cache.TryGet("A", out _), "często używany wpis przetrwał wyparcie");
        Check(!cache.TryGet("B", out _), "wypadł wpis o najniższej liczbie trafień (LFU)");
        Check(cache.Evictions == 1, "jedno wyparcie");
        cache.Set("D", "4", TimeSpan.FromMinutes(5));
        cacheClock = cacheClock.AddMinutes(6);
        Check(!cache.TryGet("D", out _), "wpis wygasa po TTL");
        Check(cache.Expired == 1 && cache.Misses > 0, "wygasłe wpisy są liczone osobno");
        Check(cache.Describe().Contains("Cache LRU/LFU"), "opis cache");

        // ---------------- Cron ----------------
        Check(!CronSchedule.TryParse("", out _, out string emptyError) && emptyError.Length > 0, "puste wyrażenie crona jest odrzucane");
        Check(!CronSchedule.TryParse("* * *", out _, out string shortError) && shortError.Contains("5 pól"), "cron musi mieć 5 pól");
        Check(!CronSchedule.TryParse("99 * * * *", out _, out string rangeError) && rangeError.Contains("Minuty"), "minuty poza zakresem są odrzucane");
        Check(CronSchedule.TryParse("*/15 * * * *", out var quarter, out _), "co kwadrans się parsuje");
        Check(quarter!.Describe() == "co 15 minut", "opis „co 15 minut”: " + quarter.Describe());
        var cronStart = new DateTimeOffset(2026, 9, 27, 8, 7, 0, TimeSpan.Zero);
        var next = quarter.Next(cronStart);
        Check(next == new DateTimeOffset(2026, 9, 27, 8, 15, 0, TimeSpan.Zero), "następny kwadrans: " + next);
        Check(quarter.Matches(new DateTimeOffset(2026, 9, 27, 8, 30, 0, TimeSpan.Zero)), "8:30 pasuje do */15");
        Check(!quarter.Matches(new DateTimeOffset(2026, 9, 27, 8, 31, 0, TimeSpan.Zero)), "8:31 nie pasuje do */15");
        Check(CronSchedule.TryParse("0 8 * * 1-5", out var workdays, out _), "dni robocze się parsują");
        var monday = workdays!.Next(new DateTimeOffset(2026, 9, 27, 9, 0, 0, TimeSpan.Zero));
        Check(monday!.Value.DayOfWeek == DayOfWeek.Monday && monday.Value.Hour == 8, "poniedziałek 8:00 po niedzieli: " + monday);
        Check(workdays.Describe().Contains("poniedziałki"), "opis dni tygodnia: " + workdays.Describe());
        Check(CronSchedule.TryParse("@daily", out var daily, out _) && daily!.Expression == "0 0 * * *", "@daily rozwija się do 0 0 * * *");
        Check(CronSchedule.TryParse("0 0 1 * *", out var monthly, out _) && monthly!.Next(new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero))!.Value.Month == 10,
            "pierwszy dzień miesiąca");
        Check(!CronSchedule.TryParse("0 0 31 2 *", out var impossible, out _) || impossible!.Next(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)) == null,
            "31 lutego nie ma następnego terminu");

        // ---------------- Maszyna stanów ----------------
        var machine = new StateMachine("nowy");
        machine.Add("nowy", "gotowy", label: "start");
        machine.Add("gotowy", "zajety");
        Check(machine.TryGo("gotowy", out _), "dozwolone przejście przechodzi");
        Check(!machine.TryGo("nowy", out string machineReason) && machineReason.Contains("nie jest dozwolone"), "niedozwolone przejście jest odrzucane z powodem");
        Check(machine.Available().Contains("zajety"), "lista dostępnych stanów");
        Check(machine.History.Count >= 2, "historia przejść jest zapisywana");
        var lifecycle = StateMachine.Lifecycle();
        Check(lifecycle.TryGo("startuje", out _) && lifecycle.TryGo("gotowy", out _) && lifecycle.TryGo("zajety", out _), "cykl życia Sentinela");
        Check(lifecycle.TryGo("zatrzymywany", out _), "gwiazdka pozwala zatrzymać z każdego stanu");

        // ---------------- Workflow (DAG) ----------------
        var graph = WorkflowGraph.Parse("pobierz > sprawdz > zapisz, sprawdz > raport", out string parseError);
        Check(parseError.Length == 0, "graf parsuje się bez błędu: " + parseError);
        var order = graph.TopologicalOrder(out var graphErrors).ToList();
        Check(graphErrors.Count == 0, "graf bez błędów");
        Check(order.IndexOf("pobierz") < order.IndexOf("sprawdz") && order.IndexOf("sprawdz") < order.IndexOf("zapisz"), "kolejność topologiczna");
        Check(order.IndexOf("sprawdz") < order.IndexOf("raport"), "raport po sprawdzeniu");
        Check(graph.Layers().Count == 3, "trzy poziomy wykonania: " + graph.Layers().Count);
        var cyclic = WorkflowGraph.Parse("a > b, b > a", out _);
        cyclic.TopologicalOrder(out var cycleErrors);
        Check(cycleErrors.Any(x => x.Contains("cykl")), "cykl jest wykrywany: " + string.Join(" · ", cycleErrors));
        var missing = WorkflowGraph.Parse("a: nieistnieje", out _);
        missing.TopologicalOrder(out var missingErrors);
        Check(missingErrors.Any(x => x.Contains("nie został zdefiniowany")), "brakująca zależność jest zgłaszana");

        var runner = new WorkflowRunner(WorkflowGraph.Parse("a > b, b > c", out _), new RetryPolicy(maxAttempts: 2, baseDelay: TimeSpan.Zero));
        var visited = new List<string>();
        var workflow = runner.Run(step => { visited.Add(step); return step == "b" ? "błąd b" : null; });
        Check(visited.Count(x => x == "b") == 2, "krok „b” był ponowiony: " + visited.Count(x => x == "b"));
        Check(workflow.First(x => x.Name == "b").State == StepState.Failed, "nieudany krok jest oznaczony jako błąd");
        Check(workflow.First(x => x.Name == "c").State == StepState.Skipped, "zależny krok jest pominięty, nie „wykonany”");
        Check(WorkflowRunner.Format(workflow).Contains("POMINIĘTE"), "raport pokazuje pominięcie");

        // ---------------- Flagi ----------------
        string flagsPath = Path.Combine(directory, "FeatureFlags.json");
        var flags = new FeatureFlags(flagsPath);
        Check(!flags.IsEnabled("nieznana"), "brak flagi znaczy wyłączone");
        Check(flags.Set("eksperyment", true, 100, "test"), "ustawienie flagi");
        Check(flags.IsEnabled("eksperyment"), "flaga 100% jest aktywna");
        Check(flags.Set("polowa", true, 50), "flaga 50%");
        bool firstSubject = flags.IsEnabled("polowa", "sesja-1");
        bool secondRun = flags.IsEnabled("polowa", "sesja-1");
        Check(firstSubject == secondRun, "zasięg flagi jest deterministyczny dla tego samego podmiotu");
        Check(FeatureFlags.Bucket("polowa|sesja-1") is >= 0 and < 100, "przydział do grupy 0–99");
        Check(!flags.Set("zła nazwa", true), "nazwa flagi nie może mieć spacji");
        var reloadedFlags = new FeatureFlags(flagsPath);
        Check(reloadedFlags.IsEnabled("eksperyment"), "flagi przeżywają restart (zapis i odczyt)");
        Check(reloadedFlags.Get("polowa")!.Percentage == 50, "procent zasięgu jest zapisany");
        Check(reloadedFlags.Remove("polowa"), "usunięcie flagi");
        Check(reloadedFlags.Get("polowa") is null, "usuniętej flagi nie ma");

        // ---------------- Zdrowie i metryki ----------------
        var health = new HealthRegistry();
        health.RegisterSimple("dobre", () => "wszystko gra");
        health.Register("złe", () => throw new InvalidOperationException("celowy problem"));
        var results = health.RunAll();
        Check(results.Count == 2, "dwa sprawdzenia");
        Check(results.First(x => x.Name == "dobre").Healthy, "dobre sprawdzenie jest zdrowe");
        Check(!results.First(x => x.Name == "złe").Healthy && results.First(x => x.Name == "złe").Detail.Contains("celowy problem"),
            "wyjątek w sprawdzeniu jest raportowany jako problem z treścią");

        var metrics = new Metrics();
        metrics.Increment("licznik");
        metrics.Increment("licznik", 4);
        metrics.Set("wskaźnik", 42.5);
        foreach (double sample in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }) metrics.Observe("czas", sample);
        Check(metrics.Counter("licznik") == 5, "licznik sumuje");
        Check(metrics.Gauge("wskaźnik") == 42.5, "wskaźnik przechowuje wartość");
        var series = metrics.Snapshot().First(x => x.Name == "czas");
        Check(series.Count == 10 && Math.Abs(series.P50 - 5.5) < 0.001, "mediana p50 z próbek: " + series.P50);
        Check(Math.Abs(series.P95 - 9.55) < 0.1, "percentyl p95: " + series.P95);
        using (metrics.Time("blok")) { }
        Check(metrics.Snapshot().Any(x => x.Name == "blok"), "pomiar bloku trafia do histogramu");
        Check(metrics.Describe().Contains("Pomiary"), "opis metryk");

        // ---------------- Dziennik JSONL ----------------
        string logsDirectory = Path.Combine(directory, "logs");
        var log = new StructuredLog(logsDirectory, LogLevel.Info, maxBytes: 4096, keepFiles: 3);
        log.Info("test.start", "start dziennika", new Dictionary<string, string> { ["wersja"] = "0.97" });
        log.Debug("test.debug", "nie powinno się zapisać");
        Check(log.Read(10).Count == 1, "poziom debug nie trafia do dziennika przy minimum info");
        for (int i = 0; i < 200; i++) log.Info("test.loop", "wypełnienie " + i);
        Check(log.Rotations >= 1, "rotacja zadziałała przy przekroczeniu rozmiaru: " + log.Rotations);
        Check(File.Exists(Path.Combine(logsDirectory, "structured.jsonl.1")), "plik rotacyjny istnieje");
        Check(log.Describe(3).Contains("Dziennik JSONL"), "opis dziennika");
        Check(log.LastWriteError is null, "brak błędów zapisu dziennika");

        // ---------------- Manifest integralności ----------------
        string integrity = Path.Combine(directory, "integrity");
        Directory.CreateDirectory(integrity);
        File.WriteAllText(Path.Combine(integrity, "a.txt"), "pierwszy");
        File.WriteAllText(Path.Combine(integrity, "b.txt"), "drugi");
        var manifest = IntegrityManifest.Build(integrity);
        Check(manifest.Count == 2, "manifest obejmuje dwa pliki");
        Check(IntegrityManifest.Serialize(manifest).Contains("a.txt"), "manifest serializuje się do JSON");
        Check(IntegrityManifest.TryDeserialize(IntegrityManifest.Serialize(manifest), out var parsed, out _) && parsed.Count == 2,
            "manifest odczytuje się z powrotem");
        Check(IntegrityManifest.Compare(manifest, IntegrityManifest.Build(integrity)).Contains("Zgodne"), "bez zmian = zgodne");
        File.WriteAllText(Path.Combine(integrity, "a.txt"), "zmieniony");
        File.Delete(Path.Combine(integrity, "b.txt"));
        File.WriteAllText(Path.Combine(integrity, "c.txt"), "nowy");
        var comparison = IntegrityManifest.Compare(manifest, IntegrityManifest.Build(integrity));
        Check(comparison.Contains("zmienione: 1") && comparison.Contains("brakujące: 1") && comparison.Contains("nowe: 1"),
            "raport rozdziela zmienione, brakujące i nowe: " + comparison.Replace(Environment.NewLine, " | "));
        Check(IntegrityManifest.HashFile(Path.Combine(integrity, "c.txt")).Length == 64, "skrót SHA-256 ma 64 znaki");

        // ---------------- Kopie zapasowe ----------------
        string source = Path.Combine(directory, "source");
        string inside = Path.Combine(source, "podkatalog");
        Directory.CreateDirectory(inside);
        File.WriteAllText(Path.Combine(source, "dane.json"), "{\"a\":1}");
        File.WriteAllText(Path.Combine(inside, "notatka.md"), "# Notatka");
        File.WriteAllText(Path.Combine(source, "pomin.txt"), "nie kopiuj");
        string backups = Path.Combine(directory, "backups");
        var backups1 = new BackupManager(backups, keep: 1);
        string created = backups1.Create(source, "test", new[] { ".json", ".md" });
        Check(created.Contains("Kopia zapasowa utworzona"), "kopia utworzona: " + created);
        var list = backups1.List();
        Check(list.Count == 1 && list[0].Files == 2, "kopia ma dwa pliki (filtr rozszerzeń): " + list[0].Files);
        Check(list[0].Sha256.Length == 64, "skrót archiwum jest policzony");
        Check(backups1.Verify(list[0]).Contains("Zgodne"), "weryfikacja kopii: " + backups1.Verify(list[0]));
        var backups2 = new BackupManager(backups, keep: 1);
        backups2.Create(source, "test", new[] { ".json", ".md" });
        Check(backups2.List().Count == 1, "rotacja zostawia jedną kopię");
        Check(File.Exists(Path.Combine(source, "pomin.txt")), "rotacja nie rusza plików źródłowych");

        // ---------------- Sejf ----------------
        string vaultPath = Path.Combine(directory, "vault.json");
        var vault = new SecretVault(vaultPath);
        Check(!vault.Create("krótkie"), "hasło krótsze niż 8 znaków jest odrzucane");
        Check(vault.Create("hasło-testowe-123"), "sejf tworzy się z dobrym hasłem: " + vault.LastError);
        Check(!vault.Create("inne-hasło-12345"), "drugie utworzenie sejfu jest odrzucane");
        Check(vault.Add("api", "tajne-123", "klucz testowy"), "dodanie sekretu: " + vault.LastError);
        Check(vault.Get("api") == "tajne-123", "odczyt sekretu");
        Check(vault.Names().Count == 1 && vault.Names()[0].Name == "api", "lista nazw bez wartości");
        vault.Lock();
        Check(!vault.IsUnlocked && vault.Get("api") is null, "po zablokowaniu sekretu nie ma");
        var wrong = new SecretVault(vaultPath);
        Check(!wrong.Unlock("złe-hasło-12345") && wrong.LastError!.Contains("Hasło nie pasuje"), "złe hasło nie odblokowuje");
        var right = new SecretVault(vaultPath);
        Check(right.Unlock("hasło-testowe-123"), "właściwe hasło odblokowuje");
        Check(right.Get("api") == "tajne-123", "sekret odczytany po ponownym odblokowaniu");
        string raw = File.ReadAllText(vaultPath);
        Check(!raw.Contains("tajne-123"), "sekret nie występuje jawnie w pliku");
        Check(!raw.Contains("hasło-testowe-123"), "hasło nie występuje jawnie w pliku");
        int cipherStart = raw.IndexOf("\"Cipher\": \"", StringComparison.Ordinal) + "\"Cipher\": \"".Length;
        Check(cipherStart >= "\"Cipher\": \"".Length && cipherStart < raw.Length, "w pliku sejfu jest zaszyfrowany wpis do testu manipulacji");
        char originalCipherChar = raw[cipherStart];
        string tampered = raw[..cipherStart] + (originalCipherChar == 'A' ? 'B' : 'A') + raw[(cipherStart + 1)..];
        File.WriteAllText(vaultPath, tampered);
        var tamperedVault = new SecretVault(vaultPath);
        Check(tamperedVault.Unlock("hasło-testowe-123"), "hasło nadal odblokowuje plik");
        Check(tamperedVault.Get("api") is null && tamperedVault.LastError!.Contains("AES-GCM"),
            "zmanipulowany wpis nie jest zwracany: " + tamperedVault.LastError);
        File.WriteAllText(vaultPath, raw);
        var restored = new SecretVault(vaultPath);
        restored.Unlock("hasło-testowe-123");
        Check(restored.Remove("api") && restored.Names().Count == 0, "usunięcie wpisu z sejfu");

        // ---------------- Serializacja binarna ----------------
        var payload = new Dictionary<string, object?>
        {
            ["tekst"] = "Zażółć",
            ["liczba"] = 42L,
            ["ujemna"] = -7L,
            ["rzeczywista"] = 3.5,
            ["flaga"] = true,
            ["pusto"] = null,
            ["bajty"] = new byte[] { 1, 2, 3 },
            ["lista"] = new List<string> { "a", "b" }
        };
        byte[] encoded = BinarySerializer.Encode(payload);
        Check(BinarySerializer.TryDecode(encoded, out var decoded, out string decodeError), "dekodowanie komunikatu: " + decodeError);
        Check((string)decoded["tekst"]! == "Zażółć", "tekst z polskimi znakami przechodzi przez serializację");
        Check((long)decoded["liczba"]! == 42 && (long)decoded["ujemna"]! == -7, "liczby całkowite (także ujemne)");
        Check(Math.Abs((double)decoded["rzeczywista"]! - 3.5) < 0.0001, "liczba zmiennoprzecinkowa");
        Check((bool)decoded["flaga"]! && decoded["pusto"] is null, "bool i null");
        Check(((byte[])decoded["bajty"]!).SequenceEqual(new byte[] { 1, 2, 3 }), "bajty");
        Check(((List<string>)decoded["lista"]!).SequenceEqual(new[] { "a", "b" }), "lista tekstów");
        var reordered = new Dictionary<string, object?> { ["lista"] = new List<string> { "a", "b" }, ["tekst"] = "Zażółć",
            ["liczba"] = 42L, ["ujemna"] = -7L, ["rzeczywista"] = 3.5, ["flaga"] = true, ["pusto"] = null, ["bajty"] = new byte[] { 1, 2, 3 } };
        Check(BinarySerializer.Encode(reordered).SequenceEqual(encoded), "kolejność kluczy nie zmienia bajtów (determinizm)");
        Check(!BinarySerializer.TryDecode([1, 2, 3], out _, out string decodeError) && decodeError.Length > 0, "za krótkie dane są odrzucane");
        var corrupted = (byte[])encoded.Clone();
        corrupted[^1] = 0xFF;
        Check(!BinarySerializer.TryDecode(corrupted[..(corrupted.Length - 3)], out _, out string corruptError) && corruptError.Length > 0,
            "ucięty komunikat jest odrzucany: " + corruptError);
        Check(BinarySerializer.TryFromHex(BinarySerializer.ToHex(encoded), out var hexBack, out _) && hexBack.SequenceEqual(encoded), "hex w obie strony");
        Check(!BinarySerializer.TryFromHex("xyz", out _, out _), "niepoprawny hex jest odrzucany");

        // ---------------- Polecenia rdzenia ----------------
        var runtime = new SentinelRuntime(directory, Path.Combine(directory, "logs"), Path.Combine(directory, "backups"));
        var previous = RuntimeCommands.Runtime;
        try
        {
            RuntimeCommands.Runtime = runtime;
            Check(RuntimeCommands.TryHandle("rdzen", "rdzen")!.Contains("SENTINEL RDZEŃ"), "polecenie „rdzen”");
            Check(RuntimeCommands.TryHandle("kolejka", "kolejka")!.Contains("Kolejka zadań"), "polecenie „kolejka”");
            Check(RuntimeCommands.TryHandle("kolejka dodaj: log: info test", "kolejka dodaj: log: info test")!.Contains("T0001"), "dodanie zadania");
            Check(RuntimeCommands.TryHandle("kolejka przetworz", "kolejka przetworz")!.Contains("wykonane"), "wykonanie zadania przez polecenie");
            Check(runtime.Log.Read(10).Any(x => x.Contains("kolejka")), "zadanie naprawdę zapisało wpis do dziennika");
            Check(RuntimeCommands.TryHandle("kolejka przetworz", "kolejka przetworz")!.Contains("Nie ma zadań") ||
                RuntimeCommands.TryHandle("kolejka przetworz", "kolejka przetworz")!.Contains("wykonane"), "kolejka po wykonaniu");
            Check(RuntimeCommands.TryHandle("cron nastepne: */30 * * * *", "cron nastepne: */30 * * * *")!.Contains("Najbliższe terminy"), "cron przez polecenie");
            Check(RuntimeCommands.TryHandle("cron opis: nie-cron", "cron opis: nie-cron")!.Contains("Nie rozumiem"), "błędny cron jest odrzucany");
            Check(RuntimeCommands.TryHandle("ustaw flage: demo on", "ustaw flage: demo on")!.Contains("włączona"), "ustawianie flagi");
            Check(RuntimeCommands.TryHandle("flaga demo", "flaga demo")!.Contains("AKTYWNA"), "odczyt flagi");
            Check(RuntimeCommands.TryHandle("zdrowie", "zdrowie")!.Contains("Zdrowie"), "raport zdrowia");
            Check(RuntimeCommands.TryHandle("metryki", "metryki")!.Contains("Pomiary"), "raport metryk");
            Check(RuntimeCommands.TryHandle("serializuj: ala", "serializuj: ala")!.Contains("Komunikat SXB1"), "serializacja przez polecenie");
            string hexLine = RuntimeCommands.TryHandle("serializuj: ala", "serializuj: ala")!
                .Split("· hex: ")[1].Split(Environment.NewLine)[0].Trim();
            Check(RuntimeCommands.TryHandle("deserializuj: " + hexLine, "deserializuj: " + hexLine)!.Contains("ala"), "deserializacja przez polecenie");
            Check(RuntimeCommands.TryHandle("maszyna: startuje", "maszyna: startuje")!.Contains("Przejście wykonane"), "przejście maszyny stanów");
            Check(RuntimeCommands.TryHandle("maszyna: nonsense", "maszyna: nonsense")!.Contains("nie jest dozwolone"), "niedozwolone przejście jest opisane");
            Check(RuntimeCommands.TryHandle("workflow: a > b, b > a", "workflow: a > b, b > a")!.Contains("cykl"), "cykl w grafie przez polecenie");
            Check(RuntimeCommands.TryHandle("sejf odblokuj", "sejf odblokuj")!.Contains("historii"), "sejf nie przyjmuje hasła z czatu");
            Check(RuntimeCommands.TryHandle("cache zapisz: klucz = wartość", "cache zapisz: klucz = wartość")!.Contains("TTL"), "zapis do cache");
            Check(RuntimeCommands.TryHandle("cache pokaz: klucz", "cache pokaz: klucz")!.Contains("wartość"), "odczyt z cache");
            Check(RuntimeCommands.TryHandle("integralnosc zbuduj: " + integrity, "integralnosc zbuduj: " + integrity)!.Contains("Manifest zapisany"), "budowa manifestu");
            Check(RuntimeCommands.TryHandle("integralnosc sprawdz: " + integrity, "integralnosc sprawdz: " + integrity)!.Contains("Zgodne"), "sprawdzenie bez zmian = zgodne");
            File.WriteAllText(Path.Combine(integrity, "d.txt"), "po zapisie manifestu");
            Check(RuntimeCommands.TryHandle("integralnosc sprawdz: " + integrity, "integralnosc sprawdz: " + integrity)!.Contains("nowe: 1"), "nowy plik jest widoczny w porównaniu");
            Check(RuntimeCommands.TryHandle("kopia danych", "kopia danych")!.Contains("Kopia zapasowa"), "kopia danych przez polecenie");
            Check(RuntimeCommands.TryHandle("zwroty", "zwroty")!.Length > 0, "kolejka zwrotów odpowiada");
            Check(RuntimeCommands.TryHandle("zdarzenia", "zdarzenia")!.Contains("Event bus"), "historia zdarzeń");
            Check(RuntimeCommands.TryHandle("bezpieczniki", "bezpieczniki")!.Contains("·"), "stan bezpieczników");
            Check(RuntimeCommands.TryHandle("to nie jest polecenie rdzenia", "to nie jest polecenie rdzenia") is null, "obcy tekst nie jest przechwytywany");
        }
        finally
        {
            RuntimeCommands.Runtime = previous;
        }

        return Task.CompletedTask;
    }
}
