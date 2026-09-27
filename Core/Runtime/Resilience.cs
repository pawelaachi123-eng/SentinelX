using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SentinelX.Core.Runtime;

/// <summary>SEKCJA 1 · pozycja 35 — Retry z wykładniczym backoffem. Polityka jest czystą funkcją czasu:
/// opóźnienie dla próby n liczy się jako BaseDelay · Multiplier^(n-1), ograniczone do MaxDelay.</summary>
public sealed class RetryPolicy
{
    public RetryPolicy(int maxAttempts = 3, TimeSpan? baseDelay = null, double multiplier = 2.0, TimeSpan? maxDelay = null)
    {
        MaxAttempts = Math.Clamp(maxAttempts, 1, 20);
        BaseDelay = baseDelay ?? TimeSpan.FromSeconds(2);
        Multiplier = multiplier < 1 ? 1 : multiplier;
        MaxDelay = maxDelay ?? TimeSpan.FromMinutes(5);
    }

    public int MaxAttempts { get; }
    public TimeSpan BaseDelay { get; }
    public double Multiplier { get; }
    public TimeSpan MaxDelay { get; }

    /// <summary>Opóźnienie przed próbą <paramref name="attempt"/> (1 = pierwsza próba, więc 0).</summary>
    public TimeSpan DelayFor(int attempt)
    {
        if (attempt <= 1) return TimeSpan.Zero;
        double factor = Math.Pow(Multiplier, attempt - 2);
        double milliseconds = BaseDelay.TotalMilliseconds * factor;
        if (!double.IsFinite(milliseconds) || milliseconds > MaxDelay.TotalMilliseconds) return MaxDelay;
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    public string Describe() => "Próby: " + MaxAttempts + " · bazowe opóźnienie " + Describe(BaseDelay) +
        " · mnożnik " + Multiplier.ToString("0.##", CultureInfo.InvariantCulture) + " · maksymalnie " + Describe(MaxDelay) + ".";

    internal static string Describe(TimeSpan span)
    {
        if (span.TotalSeconds < 60) return span.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture) + " s";
        if (span.TotalMinutes < 60) return span.TotalMinutes.ToString("0.##", CultureInfo.InvariantCulture) + " min";
        return span.TotalHours.ToString("0.##", CultureInfo.InvariantCulture) + " h";
    }
}

/// <summary>SEKCJA 1 · pozycja 34 — Circuit Breaker: chroni przed kaskadą błędów.
/// Stany: zamknięty (przepuszcza), otwarty (odrzuca do czasu schłodzenia), półotwarty (przepuszcza
/// dokładnie tyle prób, ile ustawiono). Bez wątków w tle — decyzja zapada przy wywołaniu.</summary>
public sealed class CircuitBreaker
{
    private readonly object gate = new();
    private readonly Func<DateTimeOffset> clock;

    public CircuitBreaker(string name, int failureThreshold = 3, TimeSpan? openFor = null, int halfOpenTrials = 1, Func<DateTimeOffset>? clock = null)
    {
        Name = name;
        FailureThreshold = Math.Max(1, failureThreshold);
        OpenFor = openFor ?? TimeSpan.FromSeconds(30);
        HalfOpenTrials = Math.Max(1, halfOpenTrials);
        this.clock = clock ?? (() => DateTimeOffset.Now);
    }

    public string Name { get; }
    public int FailureThreshold { get; }
    public TimeSpan OpenFor { get; }
    public int HalfOpenTrials { get; }

    public BreakerState State { get; private set; } = BreakerState.Closed;
    public int ConsecutiveFailures { get; private set; }
    public long Rejected { get; private set; }
    public long Trips { get; private set; }

    private DateTimeOffset openedAt;
    private int halfOpenInFlight;

    /// <summary>Czy wolno wykonać próbę? Gdy nie — powód jest w <paramref name="reason"/>.</summary>
    public bool TryEnter(out string reason)
    {
        lock (gate)
        {
            if (State == BreakerState.Open)
            {
                var elapsed = clock() - openedAt;
                if (elapsed < OpenFor)
                {
                    Rejected++;
                    reason = "obwód „" + Name + "” jest otwarty jeszcze przez " + RetryPolicy.Describe(OpenFor - elapsed) + ".";
                    return false;
                }
                State = BreakerState.HalfOpen;
                halfOpenInFlight = 0;
            }
            if (State == BreakerState.HalfOpen && halfOpenInFlight >= HalfOpenTrials)
            {
                Rejected++;
                reason = "obwód „" + Name + "” jest półotwarty i testuje już " + halfOpenInFlight + " prób(y).";
                return false;
            }
            if (State == BreakerState.HalfOpen) halfOpenInFlight++;
            reason = "";
            return true;
        }
    }

    public void RecordSuccess()
    {
        lock (gate)
        {
            ConsecutiveFailures = 0;
            halfOpenInFlight = 0;
            State = BreakerState.Closed;
        }
    }

    public void RecordFailure()
    {
        lock (gate)
        {
            ConsecutiveFailures++;
            halfOpenInFlight = 0;
            if (State == BreakerState.HalfOpen || ConsecutiveFailures >= FailureThreshold)
            {
                State = BreakerState.Open;
                openedAt = clock();
                Trips++;
            }
        }
    }

    /// <summary>Ręczne otwarcie (np. przez użytkownika albo test).</summary>
    public void Trip()
    {
        lock (gate)
        {
            State = BreakerState.Open;
            openedAt = clock();
            Trips++;
        }
    }

    public void Reset()
    {
        lock (gate)
        {
            State = BreakerState.Closed;
            ConsecutiveFailures = 0;
            halfOpenInFlight = 0;
        }
    }

    public string Describe()
    {
        lock (gate)
        {
            string state = State switch
            {
                BreakerState.Closed => "zamknięty (przepuszcza)",
                BreakerState.Open => "otwarty (odrzuca do " + openedAt.Add(OpenFor).ToString("HH:mm:ss") + ")",
                _ => "półotwarty (próbuje " + halfOpenInFlight + "/" + HalfOpenTrials + ")"
            };
            return "· " + Name + ": " + state + " · serie błędów: " + ConsecutiveFailures + "/" + FailureThreshold +
                " · otwarć: " + Trips + " · odrzuconych: " + Rejected;
        }
    }
}

public enum BreakerState
{
    Closed,
    Open,
    HalfOpen
}

/// <summary>SEKCJA 1 · pozycja 33 — Dead Letter Queue: zadania, które wyczerpały próby, trafiają tutaj
/// z powodem i liczbą prób. Nic nie jest kasowane po cichu; wpisy można wskrzesić (ponowić) ręcznie.</summary>
public sealed record DeadLetter(string Id, string Task, string Reason, int Attempts, RuntimePriority Priority, DateTimeOffset At);

public sealed class DeadLetterQueue
{
    private readonly object gate = new();
    private readonly List<DeadLetter> items = new();

    public DeadLetterQueue(int capacity = 100)
    {
        Capacity = Math.Max(1, capacity);
    }

    public int Capacity { get; }

    public int Count
    {
        get { lock (gate) return items.Count; }
    }

    public IReadOnlyList<DeadLetter> Snapshot()
    {
        lock (gate) return items.ToArray();
    }

    public void Add(DeadLetter letter)
    {
        lock (gate)
        {
            items.Add(letter);
            while (items.Count > Capacity) items.RemoveAt(0);
        }
    }

    /// <summary>Zabiera wpis z kolejki (do ponowienia). Zwraca false, gdy nie ma takiego identyfikatora.</summary>
    public bool TryTake(string id, out DeadLetter? letter)
    {
        lock (gate)
        {
            int index = items.FindIndex(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (index < 0) { letter = null; return false; }
            letter = items[index];
            items.RemoveAt(index);
            return true;
        }
    }

    public void Clear()
    {
        lock (gate) items.Clear();
    }

    public string Describe(int max = 10)
    {
        var snapshot = Snapshot();
        if (snapshot.Count == 0) return "Kolejka zwrotów (dead letter) jest pusta — żadne zadanie nie wyczerpało prób.";
        var lines = snapshot.OrderByDescending(x => x.At).Take(max)
            .Select(x => "· " + x.Id + " [" + x.Priority + "] " + x.Task + " — " + x.Attempts + " prób(y): " + x.Reason);
        return "Kolejka zwrotów (" + snapshot.Count + "/" + Capacity + "):" + Environment.NewLine + string.Join(Environment.NewLine, lines) +
            (snapshot.Count > max ? Environment.NewLine + "… i " + (snapshot.Count - max) + " więcej." : "");
    }
}
