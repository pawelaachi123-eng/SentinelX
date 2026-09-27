using System;
using System.Collections.Generic;
using System.Linq;

namespace SentinelX.Core.Runtime;

/// <summary>Pojedyncze zdarzenie w rdzeniu. Tematy są tekstowe (np. „kolejka.zadanie”), więc
/// każdy moduł może publikować bez współdzielenia typów.</summary>
public sealed record RuntimeEvent(string Topic, string Payload, string Source, DateTimeOffset At)
{
    public override string ToString() => At.ToString("HH:mm:ss") + " · " + Topic + (Payload.Length == 0 ? "" : " · " + Payload);
}

/// <summary>
/// SEKCJA 1 · pozycja 1 — Centralny Event Bus (pub/sub między modułami).
/// <para>Zasady: publikacja nigdy nie rzuca wyjątkiem do wołającego (błąd subskrybenta wraca jako
/// wpis raportu), historia jest ograniczona (domyślnie 200 zdarzeń), a temat może być subskrybowany
/// wzorcem „*” (dowolny ciąg) i „?” (dokładnie jeden znak) — bez wyrażeń regularnych, więc nie ma
/// ryzyka katastrofalnego dopasowania.</para>
/// <para>To nie jest transport sieciowy: wszystko dzieje się w tym procesie i tylko w pamięci.</para>
/// </summary>
public sealed class EventBus
{
    private readonly object gate = new();
    private readonly List<Subscription> subscriptions = new();
    private readonly Queue<RuntimeEvent> history = new();
    private readonly int maxHistory;

    public EventBus(int maxHistory = 200)
    {
        this.maxHistory = Math.Max(1, maxHistory);
    }

    public long Published { get; private set; }
    public long Delivered { get; private set; }
    public long HandlerErrors { get; private set; }

    private sealed record Subscription(string Pattern, Action<RuntimeEvent> Handler, string Owner);

    /// <summary>Zwolnienie zwrotu subskrypcji. Bezpieczne do wywołania wielokrotnie.</summary>
    public IDisposable Subscribe(string topicPattern, Action<RuntimeEvent> handler, string owner = "runtime")
    {
        ArgumentNullException.ThrowIfNull(handler);
        var entry = new Subscription(NormalizePattern(topicPattern), handler, owner);
        lock (gate) subscriptions.Add(entry);
        return new Unsubscriber(() => { lock (gate) subscriptions.Remove(entry); });
    }

    /// <summary>Publikuje zdarzenie i zwraca listę błędów subskrybentów (pusta = wszystko dostarczone).
    /// Kolejność wywołań jest kolejnością rejestracji, więc zachowanie jest powtarzalne w testach.</summary>
    public IReadOnlyList<string> Publish(string topic, string payload = "", string source = "runtime")
    {
        string cleanTopic = (topic ?? "").Trim();
        if (cleanTopic.Length == 0) return ["tematu nie może brakować"];
        var runtimeEvent = new RuntimeEvent(cleanTopic, payload ?? "", source, DateTimeOffset.Now);
        List<Subscription> matched;
        lock (gate)
        {
            Published++;
            history.Enqueue(runtimeEvent);
            while (history.Count > maxHistory) history.Dequeue();
            matched = subscriptions.Where(x => Matches(x.Pattern, cleanTopic)).ToList();
        }
        var errors = new List<string>();
        foreach (var subscription in matched)
        {
            try
            {
                subscription.Handler(runtimeEvent);
                lock (gate) Delivered++;
            }
            catch (Exception ex)
            {
                lock (gate) HandlerErrors++;
                errors.Add(subscription.Owner + " (" + subscription.Pattern + "): " + ex.GetType().Name + " — " + ex.Message);
            }
        }
        return errors;
    }

    /// <summary>Ostatnie zdarzenia (najnowsze na końcu).</summary>
    public IReadOnlyList<RuntimeEvent> Recent(int count = 50)
    {
        lock (gate)
            return history.Reverse().Take(Math.Max(1, count)).Reverse().ToArray();
    }

    public int SubscriberCount
    {
        get { lock (gate) return subscriptions.Count; }
    }

    /// <summary>Wzorzec dopasowania tematu: „*” dowolny ciąg, „?” pojedynczy znak, bez wielkości liter.</summary>
    internal static bool Matches(string pattern, string topic)
    {
        if (pattern == "*") return true;
        return MatchAt(pattern, 0, topic, 0);
    }

    private static bool MatchAt(string pattern, int p, string topic, int t)
    {
        while (p < pattern.Length)
        {
            char pc = pattern[p];
            if (pc == '*')
            {
                for (int skip = t; skip <= topic.Length; skip++)
                    if (MatchAt(pattern, p + 1, topic, skip)) return true;
                return false;
            }
            if (t >= topic.Length) return false;
            if (pc == '?')
            {
                p++; t++;
                continue;
            }
            if (char.ToLowerInvariant(pc) != char.ToLowerInvariant(topic[t])) return false;
            p++; t++;
        }
        return t == topic.Length;
    }

    private static string NormalizePattern(string pattern) => string.IsNullOrWhiteSpace(pattern) ? "*" : pattern.Trim();

    public string Describe(int max = 10)
    {
        var lines = new List<string>
        {
            "Event bus: " + Published + " opublikowanych · " + Delivered + " dostarczonych · " + HandlerErrors + " błędów obsługi · " + SubscriberCount + " subskrypcji."
        };
        var recent = Recent(max);
        if (recent.Count == 0) lines.Add("Brak zdarzeń w tej sesji — nic jeszcze nie publikowało.");
        else
        {
            lines.Add("Ostatnie zdarzenia:");
            lines.AddRange(recent.Select(x => "· " + x));
        }
        return string.Join(Environment.NewLine, lines);
    }

    private sealed class Unsubscriber : IDisposable
    {
        private Action? dispose;

        public Unsubscriber(Action dispose)
        {
            this.dispose = dispose;
        }

        public void Dispose()
        {
            Action? action = dispose;
            dispose = null;
            action?.Invoke();
        }
    }
}
