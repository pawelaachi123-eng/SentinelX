using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SentinelX.Core.Runtime;

/// <summary>
/// SEKCJA 1 ÔÇö spi─Öcie modu┼é├│w rdzenia w jeden obiekt z jednolitym stanem i raportem.
/// <para>Wszystkie modu┼éy s─ů tworzone leniwie (┼Ťrodowisko testowe podaje w┼éasne katalogi, ┼╝eby nie
/// dotyka─ç danych u┼╝ytkownika), a rejestracja sprawdze┼ä zdrowia i flag zdarze┼ä jest jawna ÔÇö
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

        // Wykonawcy kolejki celowo tu nie ma: rejestruje go warstwa polece┼ä (RuntimeJobs) albo host,
        // a bez wykonawcy kolejka odmawia pracy zamiast udawa─ç, ┼╝e zadanie zosta┼éo zrobione.
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

    /// <summary>Bezpiecznik o danej nazwie ÔÇö tworzony przy pierwszym u┼╝yciu, potem ten sam.</summary>
    public CircuitBreaker Breaker(string name)
    {
        lock (Breakers)
        {
            string clean = string.IsNullOrWhiteSpace(name) ? "domy┼Ťlny" : name.Trim();
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
            return System.IO.Directory.Exists(DataDirectory) ? "dost─Öpny: " + DataDirectory : "nie mog─Ö utworzy─ç " + DataDirectory;
        });
        Health.RegisterSimple("dziennik JSONL", () => Log.LastWriteError ?? "zapisany " + Log.Written + " wpis├│w w tej sesji");
        Health.RegisterSimple("kolejka zada┼ä", () => Queue.Pending + " oczekuj─ůcych, " + DeadLetters.Count + " w kolejce zwrot├│w");
        Health.RegisterSimple("flagi funkcji", () => Flags.Count + " zdefiniowanych" + (Flags.LastStorageError is null ? "" : " ┬Ě " + Flags.LastStorageError));
        Health.RegisterSimple("sejf", () => Vault.Exists ? (Vault.IsUnlocked ? "istnieje, odblokowany" : "istnieje, zablokowany") : "brak (nie u┼╝ywany)");
        Health.RegisterSimple("cache", () => Cache.Describe());
        Health.Register("bezpieczniki", () =>
        {
            var open = Breakers.Values.Where(x => x.State == BreakerState.Open).ToArray();
            return open.Length == 0
                ? (true, Breakers.Count + " obwod├│w, wszystkie zamkni─Öte")
                : (false, open.Length + " obwod├│w otwartych: " + string.Join(", ", open.Select(x => x.Name)));
        });
    }

    public string Describe()
    {
        bool vaultNote = Vault.Exists;
        var lines = new List<string>
        {
            "SENTINEL RDZE┼â ┬Ě katalog danych: " + DataDirectory,
            "┬Ě event bus: " + Events.Published + " zdarze┼ä, " + Events.SubscriberCount + " subskrypcji, " + Events.HandlerErrors + " b┼é─Öd├│w obs┼éugi",
            "┬Ě kolejka: " + Queue.Pending + " oczekuj─ůcych ┬Ě wykonane " + Queue.Count(RuntimeTaskState.Done) + " ┬Ě zwroty " + DeadLetters.Count +
                " ┬Ě wykonawca: " + (Queue.WorkerOwner.Length == 0 ? "brak" : Queue.WorkerOwner),
            "┬Ě bezpieczniki: " + Breakers.Count + " (otwarte: " + Breakers.Values.Count(x => x.State == BreakerState.Open) + ")",
            "┬Ě cache: " + Cache.Count + "/" + Cache.Capacity + " ┬Ě flagi: " + Flags.Count + " ┬Ě sejf: " + (vaultNote ? (Vault.IsUnlocked ? "odblokowany" : "zablokowany") : "brak"),
            "┬Ě backupy: " + Backups.List().Count + "/" + Backups.Keep + " w " + BackupDirectory,
            "┬Ě dziennik: " + Log.Path + " (rotacji: " + Log.Rotations + ")",
            "┬Ě metryki: " + (Metrics.Snapshot().Count == 0 ? "brak pr├│bek" : Metrics.Snapshot().Count + " serii"),
            "┬Ě cykl ┼╝ycia: " + Lifecycle.Current,
            "┬Ě polityka ponowie┼ä: " + Queue.Policy.Describe()
        };
        return string.Join(Environment.NewLine, lines);
    }
}
