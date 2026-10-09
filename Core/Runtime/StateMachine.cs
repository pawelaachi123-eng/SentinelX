using System;
using System.Collections.Generic;
using System.Linq;

namespace SentinelX.Core.Runtime;

/// <summary>Przej┼Ťcie mi─Ödzy stanami. Opcjonalny warunek (<c>Guard</c>) i etykieta opisuj─ů pow├│d.</summary>
public sealed record StateTransition(string From, string To, Func<bool>? Guard = null, string Label = "");

/// <summary>
/// SEKCJA 1 ┬Ě pozycja 12 ÔÇö State Machine Engine. Deterministyczna maszyna stan├│w na nazwach tekstowych:
/// dozwolone przej┼Ťcia s─ů zadeklarowane z g├│ry, nieudane przej┼Ťcie nic nie zmienia i zwraca pow├│d,
/// a historia (kto, kiedy, dlaczego) jest ograniczona i widoczna.
/// </summary>
public sealed class StateMachine
{
    private readonly object gate = new();
    private readonly List<StateTransition> transitions = new();
    private readonly List<(string State, string Reason, DateTimeOffset At)> history = new();
    private readonly Func<DateTimeOffset> clock;
    private readonly int maxHistory;

    public StateMachine(string initial, IEnumerable<StateTransition>? allowed = null, Func<DateTimeOffset>? clock = null, int maxHistory = 100)
    {
        Initial = initial;
        Current = initial;
        this.clock = clock ?? (() => DateTimeOffset.Now);
        this.maxHistory = Math.Max(1, maxHistory);
        if (allowed != null) transitions.AddRange(allowed);
        history.Add((initial, "stan pocz─ůtkowy", this.clock()));
    }

    public string Initial { get; }
    public string Current { get; private set; }
    public long Rejected { get; private set; }

    public StateMachine Add(string from, string to, Func<bool>? guard = null, string label = "")
    {
        lock (gate) transitions.Add(new StateTransition(from, to, guard, label));
        return this;
    }

    public bool CanGo(string target)
    {
        lock (gate)
            return transitions.Any(x => Matches(x.From, Current) && Matches(x.To, target) && (x.Guard is null || Safe(x.Guard)));
    }

    public IReadOnlyList<string> Available()
    {
        lock (gate)
            return transitions.Where(x => Matches(x.From, Current) && (x.Guard is null || Safe(x.Guard)))
                .Select(x => x.To).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public bool TryGo(string target, out string reason, string why = "")
    {
        lock (gate)
        {
            var transition = transitions.FirstOrDefault(x => Matches(x.From, Current) && Matches(x.To, target));
            if (transition is null)
            {
                Rejected++;
                reason = "Przej┼Ťcie ÔÇ×" + Current + "ÔÇŁ Ôćĺ ÔÇ×" + target + "ÔÇŁ nie jest dozwolone. Dost─Öpne: " +
                    (Available().Count == 0 ? "brak (stan ko┼äcowy)" : string.Join(", ", Available())) + ".";
                return false;
            }
            if (transition.Guard is not null && !Safe(transition.Guard))
            {
                Rejected++;
                reason = "Warunek przej┼Ťcia ÔÇ×" + Current + "ÔÇŁ Ôćĺ ÔÇ×" + target + "ÔÇŁ nie jest spe┼éniony" +
                    (transition.Label.Length > 0 ? " (" + transition.Label + ")" : "") + ".";
                return false;
            }
            string previous = Current;
            Current = transition.To;
            history.Add((Current, why.Length > 0 ? why : previous + " Ôćĺ " + Current, clock()));
            while (history.Count > maxHistory) history.RemoveAt(0);
            reason = "";
            return true;
        }
    }

    /// <summary>Ustawia stan bez walidacji przej┼Ťcia ÔÇö wy┼é─ůcznie dla hosta i test├│w (np. odtworzenie sesji).</summary>
    public void Restore(string state, string why = "odtworzenie stanu")
    {
        lock (gate)
        {
            Current = state;
            history.Add((state, why, clock()));
            while (history.Count > maxHistory) history.RemoveAt(0);
        }
    }

    public IReadOnlyList<(string State, string Reason, DateTimeOffset At)> History
    {
        get { lock (gate) return history.ToArray(); }
    }

    private static bool Matches(string pattern, string value) =>
        pattern == "*" || string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase);

    private static bool Safe(Func<bool> guard)
    {
        try { return guard(); }
        catch { return false; }
    }

    public string Describe()
    {
        lock (gate)
        {
            var available = transitions.Where(x => Matches(x.From, Current) && (x.Guard is null || Safe(x.Guard)))
                .Select(x => x.To + (x.Label.Length > 0 ? " (" + x.Label + ")" : ""))
                .Distinct().ToArray();
            var lines = new List<string>
            {
                "Stan: " + Current + " ┬Ě odrzuconych przej┼Ť─ç: " + Rejected,
                "Dost─Öpne przej┼Ťcia: " + (available.Length == 0 ? "brak (stan ko┼äcowy)" : string.Join(" ┬Ě ", available))
            };
            var recent = history.TakeLast(5).ToArray();
            if (recent.Length > 0)
            {
                lines.Add("Historia (ostatnie " + recent.Length + "):");
                lines.AddRange(recent.Select(x => "┬Ě " + x.At.ToString("HH:mm:ss") + " " + x.State + " ÔÇö " + x.Reason));
            }
            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>Maszyna cyklu ┼╝ycia Sentinela ÔÇö jedna, wsp├│lna definicja dla polece┼ä i hosta.</summary>
    public static StateMachine Lifecycle()
    {
        var machine = new StateMachine("zatrzymany");
        machine.Add("zatrzymany", "startuje", label: "proces wystartowa┼é");
        machine.Add("startuje", "gotowy", label: "us┼éugi wczytane");
        machine.Add("startuje", "zatrzymany", label: "b┼é─ůd startu");
        machine.Add("gotowy", "zajety", label: "przyj─Öto polecenie");
        machine.Add("zajety", "gotowy", label: "polecenie zako┼äczone");
        machine.Add("gotowy", "pauza", label: "u┼╝ytkownik wstrzyma┼é");
        machine.Add("pauza", "gotowy", label: "wznowienie");
        machine.Add("*", "zatrzymywany", label: "zamkni─Öcie");
        machine.Add("zatrzymywany", "zatrzymany", label: "us┼éugi zamkni─Öte");
        return machine;
    }
}
