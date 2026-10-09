using System;
using System.Collections.Generic;
using System.Linq;

namespace SentinelX.Core.Runtime;

/// <summary>Pojedyncze zdarzenie w rdzeniu. Tematy s─ů tekstowe (np. ÔÇ×kolejka.zadanieÔÇŁ), wi─Öc
/// ka┼╝dy modu┼é mo┼╝e publikowa─ç bez wsp├│┼édzielenia typ├│w.</summary>
public sealed record RuntimeEvent(string Topic, string Payload, string Source, DateTimeOffset At)
{
    public override string ToString() => At.ToString("HH:mm:ss") + " ┬Ě " + Topic + (Payload.Length == 0 ? "" : " ┬Ě " + Payload);
}

/// <summary>
/// SEKCJA 1 ┬Ě pozycja 1 ÔÇö Centralny Event Bus (pub/sub mi─Ödzy modu┼éami).
/// <para>Zasady: publikacja nigdy nie rzuca wyj─ůtkiem do wo┼éaj─ůcego (b┼é─ůd subskrybenta wraca jako
/// wpis raportu), historia jest ograniczona (domy┼Ťlnie 200 zdarze┼ä), a temat mo┼╝e by─ç subskrybowany
/// wzorcem ÔÇ×*ÔÇŁ (dowolny ci─ůg) i ÔÇ×?ÔÇŁ (dok┼éadnie jeden znak) ÔÇö bez wyra┼╝e┼ä regularnych, wi─Öc nie ma
/// ryzyka katastrofalnego dopasowania.</para>
/// <para>To nie jest transport sieciowy: wszystko dzieje si─Ö w tym procesie i tylko w pami─Öci.</para>
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

    /// <summary>Zwolnienie zwrotu subskrypcji. Bezpieczne do wywo┼éania wielokrotnie.</summary>
    public IDisposable Subscribe(string topicPattern, Action<RuntimeEvent> handler, string owner = "runtime")
    {
        ArgumentNullException.ThrowIfNull(handler);
        var entry = new Subscription(NormalizePattern(topicPattern), handler, owner);
        lock (gate) subscriptions.Add(entry);
        return new Unsubscriber(() => { lock (gate) subscriptions.Remove(entry); });
    }

    /// <summary>Publikuje zdarzenie i zwraca list─Ö b┼é─Öd├│w subskrybent├│w (pusta = wszystko dostarczone).
    /// Kolejno┼Ť─ç wywo┼éa┼ä jest kolejno┼Ťci─ů rejestracji, wi─Öc zachowanie jest powtarzalne w testach.</summary>
    public IReadOnlyList<string> Publish(string topic, string payload = "", string source = "runtime")
    {
        string cleanTopic = (topic ?? "").Trim();
        if (cleanTopic.Length == 0) return ["tematu nie mo┼╝e brakowa─ç"];
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
                errors.Add(subscription.Owner + " (" + subscription.Pattern + "): " + ex.GetType().Name + " ÔÇö " + ex.Message);
            }
        }
        return errors;
    }

    /// <summary>Ostatnie zdarzenia (najnowsze na ko┼äcu).</summary>
    public IReadOnlyList<RuntimeEvent> Recent(int count = 50)
    {
        lock (gate)
            return history.Reverse().Take(Math.Max(1, count)).Reverse().ToArray();
    }

    public int SubscriberCount
    {
        get { lock (gate) return subscriptions.Count; }
    }

    /// <summary>Wzorzec dopasowania tematu: ÔÇ×*ÔÇŁ dowolny ci─ůg, ÔÇ×?ÔÇŁ pojedynczy znak, bez wielko┼Ťci liter.</summary>
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
            "Event bus: " + Published + " opublikowanych ┬Ě " + Delivered + " dostarczonych ┬Ě " + HandlerErrors + " b┼é─Öd├│w obs┼éugi ┬Ě " + SubscriberCount + " subskrypcji."
        };
        var recent = Recent(max);
        if (recent.Count == 0) lines.Add("Brak zdarze┼ä w tej sesji ÔÇö nic jeszcze nie publikowa┼éo.");
        else
        {
            lines.Add("Ostatnie zdarzenia:");
            lines.AddRange(recent.Select(x => "┬Ě " + x));
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
