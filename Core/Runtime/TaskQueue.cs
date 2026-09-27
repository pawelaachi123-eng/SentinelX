using System;
using System.Collections.Generic;
using System.Linq;

namespace SentinelX.Core.Runtime;

/// <summary>Priorytet zadania. Liczba rośnie razem z ważnością, więc sortowanie jest naturalne.</summary>
public enum RuntimePriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

public enum RuntimeTaskState
{
    Queued,
    Running,
    Done,
    Failed,
    DeadLettered,
    Cancelled
}

/// <summary>Zadanie w kolejce rdzenia. Stan jest zmienny wyłącznie przez kolejkę.</summary>
public sealed class QueuedTask
{
    internal QueuedTask(string id, string name, RuntimePriority priority, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        Priority = priority;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public string Id { get; }
    public string Name { get; }
    public RuntimePriority Priority { get; }
    public RuntimeTaskState State { get; internal set; } = RuntimeTaskState.Queued;
    public int Attempts { get; internal set; }
    public string LastError { get; internal set; } = "";
    public string Result { get; internal set; } = "";
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? NotBefore { get; internal set; }
    public DateTimeOffset UpdatedAt { get; internal set; }

    public override string ToString() => Id + " [" + Priority + "/" + State + "] " + Name +
        (Attempts > 0 ? " (próby: " + Attempts + ")" : "") + (LastError.Length > 0 ? " — " + LastError : "");
}

/// <summary>
/// SEKCJA 1 · pozycje 3–4 i 33–35 — Task Queue z priorytetami, ponowieniami i backoffem.
/// <para>Wykonawcę rejestruje host (albo test) przez <see cref="RegisterWorker"/>. Kolejka sama nie
/// udaje pracy: gdy nie ma wykonawcy, <see cref="RunDue"/> odmawia i mówi to wprost.</para>
/// <para>Zegar jest wstrzykiwany, więc cała logika (opóźnienia, ponowienia, dead letter) jest
/// deterministyczna i testowalna bez czekania.</para>
/// </summary>
public sealed class DurableTaskQueue
{
    private readonly object gate = new();
    private readonly List<QueuedTask> tasks = new();
    private readonly Func<DateTimeOffset> clock;
    private readonly EventBus? events;
    private readonly CircuitBreaker? breaker;
    private int sequence;

    public DurableTaskQueue(RetryPolicy? policy = null, DeadLetterQueue? deadLetters = null,
        EventBus? events = null, CircuitBreaker? breaker = null, Func<DateTimeOffset>? clock = null)
    {
        Policy = policy ?? new RetryPolicy();
        DeadLetters = deadLetters ?? new DeadLetterQueue();
        this.events = events;
        this.breaker = breaker;
        this.clock = clock ?? (() => DateTimeOffset.Now);
    }

    public RetryPolicy Policy { get; }
    public DeadLetterQueue DeadLetters { get; }
    public Func<QueuedTask, string?>? Worker { get; private set; }
    public string WorkerOwner { get; private set; } = "";
    public long Processed { get; private set; }
    public long Failed { get; private set; }
    public long Retried { get; private set; }

    public void RegisterWorker(Func<QueuedTask, string?> worker, string owner = "runtime")
    {
        Worker = worker ?? throw new ArgumentNullException(nameof(worker));
        WorkerOwner = owner;
    }

    public QueuedTask Enqueue(string name, RuntimePriority priority = RuntimePriority.Normal, TimeSpan? delay = null)
    {
        string clean = (name ?? "").Trim();
        if (clean.Length == 0) clean = "(bez nazwy)";
        if (clean.Length > 300) clean = clean[..300];
        var task = new QueuedTask("T" + (++sequence).ToString("D4"), clean, priority, clock());
        if (delay is { } wait && wait > TimeSpan.Zero) task.NotBefore = clock() + wait;
        lock (gate) tasks.Add(task);
        events?.Publish("kolejka.dodano", task.Id + " " + clean, "taskqueue");
        return task;
    }

    /// <summary>Najpierw priorytet, potem kolejność zgłoszenia. Zadania odłożone w czasie są pomijane.</summary>
    public QueuedTask? Next()
    {
        lock (gate)
        {
            var now = clock();
            return tasks
                .Where(x => x.State == RuntimeTaskState.Queued && (x.NotBefore is null || x.NotBefore <= now))
                .OrderByDescending(x => x.Priority)
                .ThenBy(x => x.CreatedAt)
                .FirstOrDefault();
        }
    }

    public IReadOnlyList<QueuedTask> Snapshot()
    {
        lock (gate) return tasks.ToArray();
    }

    public int Count(RuntimeTaskState state)
    {
        lock (gate) return tasks.Count(x => x.State == state);
    }

    public int Pending
    {
        get { lock (gate) return tasks.Count(x => x.State == RuntimeTaskState.Queued); }
    }

    public bool Cancel(string id)
    {
        lock (gate)
        {
            var task = tasks.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (task == null || task.State is RuntimeTaskState.Done or RuntimeTaskState.Cancelled) return false;
            task.State = RuntimeTaskState.Cancelled;
            task.UpdatedAt = clock();
            return true;
        }
    }

    /// <summary>Uruchamia zadania, których czas nadszedł. Wykonawca zwraca null = sukces, tekst = powód błędu.</summary>
    public string RunDue(int max = 5)
    {
        if (Worker == null)
            return "Brak zarejestrowanego wykonawcy — kolejka nie udaje wykonania. Wykonawcę rejestruje host " +
                   "(aplikacja albo test), pojedynczo na raz, i tylko on decyduje, co znaczy sukces.";

        var reports = new List<string>();
        for (int i = 0; i < Math.Max(1, max); i++)
        {
            if (breaker != null && !breaker.TryEnter(out string breakerReason))
            {
                reports.Add("Wstrzymane: " + breakerReason);
                break;
            }
            QueuedTask? task;
            lock (gate)
            {
                var now = clock();
                task = tasks.Where(x => x.State == RuntimeTaskState.Queued && (x.NotBefore is null || x.NotBefore <= now))
                    .OrderByDescending(x => x.Priority).ThenBy(x => x.CreatedAt).FirstOrDefault();
                if (task == null)
                {
                    breaker?.RecordSuccess();
                    break;
                }
                task.State = RuntimeTaskState.Running;
                task.Attempts++;
                task.UpdatedAt = now;
            }

            string? error;
            try
            {
                error = Worker(task);
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
            }

            if (error == null)
            {
                lock (gate)
                {
                    task.State = RuntimeTaskState.Done;
                    task.LastError = "";
                    task.UpdatedAt = clock();
                    Processed++;
                }
                breaker?.RecordSuccess();
                events?.Publish("kolejka.sukces", task.Id + " " + task.Name, "taskqueue");
                reports.Add("· " + task.Id + " wykonane (" + WorkerOwner + ").");
                continue;
            }

            lock (gate)
            {
                task.LastError = error.Length > 300 ? error[..300] : error;
                task.UpdatedAt = clock();
                if (task.Attempts >= Policy.MaxAttempts)
                {
                    task.State = RuntimeTaskState.DeadLettered;
                    Failed++;
                    DeadLetters.Add(new DeadLetter(task.Id, task.Name, task.LastError, task.Attempts, task.Priority, task.UpdatedAt));
                    reports.Add("· " + task.Id + " odłożone do kolejki zwrotów po " + task.Attempts + " próbach: " + task.LastError);
                }
                else
                {
                    task.State = RuntimeTaskState.Failed;
                    task.NotBefore = clock() + Policy.DelayFor(task.Attempts + 1);
                    task.State = RuntimeTaskState.Queued;
                    Retried++;
                    reports.Add("· " + task.Id + " nieudane (próba " + task.Attempts + "/" + Policy.MaxAttempts + "), ponowienie za " +
                        RetryPolicy.Describe(Policy.DelayFor(task.Attempts + 1)) + ": " + task.LastError);
                }
            }
            breaker?.RecordFailure();
            events?.Publish("kolejka.blad", task.Id + " " + task.LastError, "taskqueue");
        }

        if (reports.Count == 0) return "Nie ma zadań, których czas już nadszedł (opóźnione czekają na swój termin).";
        return string.Join(Environment.NewLine, reports);
    }

    public string Describe(int max = 10)
    {
        var snapshot = Snapshot();
        var lines = new List<string>
        {
            "Kolejka zadań: " + snapshot.Count + " łącznie · oczekujące " + Count(RuntimeTaskState.Queued) +
            " · wykonane " + Count(RuntimeTaskState.Done) + " · zwroty " + DeadLetters.Count + " · wykonawca: " +
            (Worker == null ? "brak (kolejka nie wykona nic do rejestracji)" : WorkerOwner)
        };
        lines.Add(Policy.Describe());
        if (breaker != null) lines.Add(breaker.Describe());
        var shown = snapshot.OrderByDescending(x => x.State == RuntimeTaskState.Queued).ThenByDescending(x => x.Priority).Take(max);
        foreach (var task in shown) lines.Add("· " + task);
        if (snapshot.Count > max) lines.Add("… i " + (snapshot.Count - max) + " więcej.");
        return string.Join(Environment.NewLine, lines);
    }
}
