using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SentinelX.Core.Runtime;

/// <summary>
/// SEKCJA 1 — spięcie modułów rdzenia w jeden obiekt z jednolitym stanem i raportem.
/// <para>Wszystkie moduły są tworzone leniwie (środowisko testowe podaje własne katalogi, żeby nie
/// dotykać danych użytkownika), a rejestracja sprawdzeń zdrowia i flag zdarzeń jest jawna —
/// nic nie startuje w tle bez pytania.</para>
/// </summary>
public sealed class SentinelRuntime
{
    private static readonly Lazy<SentinelRuntime> Shared = new(() => new SentinelRuntime(), isThreadSafe: true);

    public SentinelRuntime(string? dataDirectory = null, string? logsDirectory = null, string? backupDirectory = null)
    {
        DataDirectory = dataDirectory ?? AppPaths.Root;
        LogsDirectory = logsDirectory ?? AppPaths.LogsDirectory;
        BackupDirectory = backupDirectory ?? AppPaths.BackupsDirectory;

        Events = new EventBus();
        Metrics = new Metrics();
        Log = new StructuredLog(LogsDirectory, LogLevel.Info, maxBytes: 512 * 1024, keepFiles: 5);
        DeadLetters = new DeadLetterQueue();
        Breakers = new Dictionary<string, CircuitBreaker>(StringComparer.OrdinalIgnoreCase);
        Cache = new LruLfuCache<string, string>(capacity: 256);
        Queue = new DurableTaskQueue(new RetryPolicy(maxAttempts: 3, baseDelay: TimeSpan.FromSeconds(2)),
            DeadLetters, Events, Breaker("kolejka"));
        Flags = new FeatureFlags(System.IO.Path.Combine(DataDirectory, "Memory", "FeatureFlags.json"));
        Vault = new SecretVault(System.IO.Path.Combine(DataDirectory, "Memory", "Vault.json"));
        Backups = new BackupManager(BackupDirectory);
        Health = new HealthRegistry();
        Lifecycle = StateMachine.Lifecycle();

        // Wykonawcy kolejki celowo tu nie ma: rejestruje go warstwa poleceń (RuntimeJobs) albo host,
        // a bez wykonawcy kolejka odmawia pracy zamiast udawać, że zadanie zostało zrobione.
        RegisterCoreHealthChecks();
        Events.Subscribe("kolejka.*", e => Metrics.Increment("kolejka.zdarzenia"), "metryki");
    }

    public static SentinelRuntime Instance => Shared.Value;

    public string DataDirectory { get; }
    public string LogsDirectory { get; }
    public string BackupDirectory { get; }

    public EventBus Events { get; }
    public DurableTaskQueue Queue { get; }
    public DeadLetterQueue DeadLetters { get; }
    public LruLfuCache<string, string> Cache { get; }
    public FeatureFlags Flags { get; }
    public HealthRegistry Health { get; }
    public Metrics Metrics { get; }
    public StructuredLog Log { get; }
    public BackupManager Backups { get; }
    public SecretVault Vault { get; }
    public StateMachine Lifecycle { get; }
    public Dictionary<string, CircuitBreaker> Breakers { get; }

    /// <summary>Bezpiecznik o danej nazwie — tworzony przy pierwszym użyciu, potem ten sam.</summary>
    public CircuitBreaker Breaker(string name)
    {
        lock (Breakers)
        {
            string clean = string.IsNullOrWhiteSpace(name) ? "domyślny" : name.Trim();
            if (Breakers.TryGetValue(clean, out var existing)) return existing;
            var created = new CircuitBreaker(clean, failureThreshold: 3, openFor: TimeSpan.FromSeconds(30), halfOpenTrials: 1);
            Breakers[clean] = created;
            return created;
        }
    }

    private void RegisterCoreHealthChecks()
    {
        Health.RegisterSimple("katalog danych", () =>
        {
            System.IO.Directory.CreateDirectory(DataDirectory);
            return System.IO.Directory.Exists(DataDirectory) ? "dostępny: " + DataDirectory : "nie mogę utworzyć " + DataDirectory;
        });
        Health.RegisterSimple("dziennik JSONL", () => Log.LastWriteError ?? "zapisany " + Log.Written + " wpisów w tej sesji");
        Health.RegisterSimple("kolejka zadań", () => Queue.Pending + " oczekujących, " + DeadLetters.Count + " w kolejce zwrotów");
        Health.RegisterSimple("flagi funkcji", () => Flags.Count + " zdefiniowanych" + (Flags.LastStorageError is null ? "" : " · " + Flags.LastStorageError));
        Health.RegisterSimple("sejf", () => Vault.Exists ? (Vault.IsUnlocked ? "istnieje, odblokowany" : "istnieje, zablokowany") : "brak (nie używany)");
        Health.RegisterSimple("cache", () => Cache.Describe());
        Health.Register("bezpieczniki", () =>
        {
            var open = Breakers.Values.Where(x => x.State == BreakerState.Open).ToArray();
            return open.Length == 0
                ? (true, Breakers.Count + " obwodów, wszystkie zamknięte")
                : (false, open.Length + " obwodów otwartych: " + string.Join(", ", open.Select(x => x.Name)));
        });
    }

    public string Describe()
    {
        bool vaultNote = Vault.Exists;
        var lines = new List<string>
        {
            "SENTINEL RDZEŃ · katalog danych: " + DataDirectory,
            "· event bus: " + Events.Published + " zdarzeń, " + Events.SubscriberCount + " subskrypcji, " + Events.HandlerErrors + " błędów obsługi",
            "· kolejka: " + Queue.Pending + " oczekujących · wykonane " + Queue.Count(RuntimeTaskState.Done) + " · zwroty " + DeadLetters.Count +
                " · wykonawca: " + (Queue.WorkerOwner.Length == 0 ? "brak" : Queue.WorkerOwner),
            "· bezpieczniki: " + Breakers.Count + " (otwarte: " + Breakers.Values.Count(x => x.State == BreakerState.Open) + ")",
            "· cache: " + Cache.Count + "/" + Cache.Capacity + " · flagi: " + Flags.Count + " · sejf: " + (vaultNote ? (Vault.IsUnlocked ? "odblokowany" : "zablokowany") : "brak"),
            "· backupy: " + Backups.List().Count + "/" + Backups.Keep + " w " + BackupDirectory,
            "· dziennik: " + Log.Path + " (rotacji: " + Log.Rotations + ")",
            "· metryki: " + (Metrics.Snapshot().Count == 0 ? "brak próbek" : Metrics.Snapshot().Count + " serii"),
            "· cykl życia: " + Lifecycle.Current,
            "· polityka ponowień: " + Queue.Policy.Describe()
        };
        return string.Join(Environment.NewLine, lines);
    }
}
