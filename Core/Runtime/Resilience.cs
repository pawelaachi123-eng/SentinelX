using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SentinelX.Core.Runtime;

/// <summary>SEKCJA 1 ┬Ě pozycja 35 ÔÇö Retry z wyk┼éadniczym backoffem. Polityka jest czyst─ů funkcj─ů czasu:
/// op├│┼║nienie dla pr├│by n liczy si─Ö jako BaseDelay ┬Ě Multiplier^(n-1), ograniczone do MaxDelay.</summary>
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

    /// <summary>Op├│┼║nienie przed pr├│b─ů <paramref name="attempt"/> (1 = pierwsza pr├│ba, wi─Öc 0).</summary>
    public TimeSpan DelayFor(int attempt)
    {
        if (attempt <= 1) return TimeSpan.Zero;
        double factor = Math.Pow(Multiplier, attempt - 2);
        double milliseconds = BaseDelay.TotalMilliseconds * factor;
        if (!double.IsFinite(milliseconds) || milliseconds > MaxDelay.TotalMilliseconds) return MaxDelay;
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    public string Describe() => "Pr├│by: " + MaxAttempts + " ┬Ě bazowe op├│┼║nienie " + Describe(BaseDelay) +
        " ┬Ě mno┼╝nik " + Multiplier.ToString("0.##", CultureInfo.InvariantCulture) + " ┬Ě maksymalnie " + Describe(MaxDelay) + ".";

    internal static string Describe(TimeSpan span)
    {
        if (span.TotalSeconds < 60) return span.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture) + " s";
        if (span.TotalMinutes < 60) return span.TotalMinutes.ToString("0.##", CultureInfo.InvariantCulture) + " min";
        return span.TotalHours.ToString("0.##", CultureInfo.InvariantCulture) + " h";
    }
}

/// <summary>SEKCJA 1 ┬Ě pozycja 34 ÔÇö Circuit Breaker: chroni przed kaskad─ů b┼é─Öd├│w.
/// Stany: zamkni─Öty (przepuszcza), otwarty (odrzuca do czasu sch┼éodzenia), p├│┼éotwarty (przepuszcza
/// dok┼éadnie tyle pr├│b, ile ustawiono). Bez w─ůtk├│w w tle ÔÇö decyzja zapada przy wywo┼éaniu.</summary>
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

    /// <summary>Czy wolno wykona─ç pr├│b─Ö? Gdy nie ÔÇö pow├│d jest w <paramref name="reason"/>.</summary>
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
                    reason = "obw├│d ÔÇ×" + Name + "ÔÇŁ jest otwarty jeszcze przez " + RetryPolicy.Describe(OpenFor - elapsed) + ".";
                    return false;
                }
                State = BreakerState.HalfOpen;
                halfOpenInFlight = 0;
            }
            if (State == BreakerState.HalfOpen && halfOpenInFlight >= HalfOpenTrials)
            {
                Rejected++;
                reason = "obw├│d ÔÇ×" + Name + "ÔÇŁ jest p├│┼éotwarty i testuje ju┼╝ " + halfOpenInFlight + " pr├│b(y).";
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

    /// <summary>R─Öczne otwarcie (np. przez u┼╝ytkownika albo test).</summary>
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
                BreakerState.Closed => "zamkni─Öty (przepuszcza)",
                BreakerState.Open => "otwarty (odrzuca do " + openedAt.Add(OpenFor).ToString("HH:mm:ss") + ")",
                _ => "p├│┼éotwarty (pr├│buje " + halfOpenInFlight + "/" + HalfOpenTrials + ")"
            };
            return "┬Ě " + Name + ": " + state + " ┬Ě serie b┼é─Öd├│w: " + ConsecutiveFailures + "/" + FailureThreshold +
                " ┬Ě otwar─ç: " + Trips + " ┬Ě odrzuconych: " + Rejected;
        }
    }
}

public enum BreakerState
{
    Closed,
    Open,
    HalfOpen
}

/// <summary>SEKCJA 1 ┬Ě pozycja 33 ÔÇö Dead Letter Queue: zadania, kt├│re wyczerpa┼éy pr├│by, trafiaj─ů tutaj
/// z powodem i liczb─ů pr├│b. Nic nie jest kasowane po cichu; wpisy mo┼╝na wskrzesi─ç (ponowi─ç) r─Öcznie.</summary>
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
        if (snapshot.Count == 0) return "Kolejka zwrot├│w (dead letter) jest pusta ÔÇö ┼╝adne zadanie nie wyczerpa┼éo pr├│b.";
        var lines = snapshot.OrderByDescending(x => x.At).Take(max)
            .Select(x => "┬Ě " + x.Id + " [" + x.Priority + "] " + x.Task + " ÔÇö " + x.Attempts + " pr├│b(y): " + x.Reason);
        return "Kolejka zwrot├│w (" + snapshot.Count + "/" + Capacity + "):" + Environment.NewLine + string.Join(Environment.NewLine, lines) +
            (snapshot.Count > max ? Environment.NewLine + "ÔÇŽ i " + (snapshot.Count - max) + " wi─Öcej." : "");
    }
}
