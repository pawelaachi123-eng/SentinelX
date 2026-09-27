using System;
using System.Collections.Generic;
using System.Linq;

namespace SentinelX.Core.Runtime;

/// <summary>Status jednego kroku workflow.</summary>
public enum StepState
{
    Pending,
    Running,
    Done,
    Failed,
    Skipped
}

public sealed record StepReport(string Name, StepState State, int Attempts, string Detail, TimeSpan Duration);

/// <summary>
/// SEKCJA 1 · pozycje 16–17 — Workflow Engine (DAG) i Pipeline Builder.
/// <para>Graf jest walidowany przed uruchomieniem: brakujące zależności i cykle są zgłaszane jako błędy,
/// a nie „wykonane po cichu w złej kolejności”. Kroki, których zależność padła, są pomijane (nie udają
/// sukcesu), a kolejność wykonania wynika wyłącznie z grafu.</para>
/// </summary>
public sealed class WorkflowGraph
{
    private readonly Dictionary<string, List<string>> dependencies = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> order = new();

    public WorkflowGraph Add(string node, params string[] dependsOn)
    {
        string name = (node ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException("Nazwa kroku nie może być pusta.", nameof(node));
        if (!dependencies.ContainsKey(name)) order.Add(name);
        if (!dependencies.TryGetValue(name, out var list))
        {
            list = new List<string>();
            dependencies[name] = list;
        }
        foreach (string dependency in dependsOn)
        {
            string clean = (dependency ?? "").Trim();
            if (clean.Length == 0 || string.Equals(clean, name, StringComparison.OrdinalIgnoreCase)) continue;
            if (!list.Contains(clean, StringComparer.OrdinalIgnoreCase)) list.Add(clean);
        }
        return this;
    }

    public IReadOnlyList<string> Nodes => order.ToArray();

    public IReadOnlyList<string> DependenciesOf(string node) =>
        dependencies.TryGetValue(node, out var list) ? list.ToArray() : [];

    /// <summary>Kolejność topologiczna (Kahn). Przy błędzie zwraca częściową kolejność i listę problemów.</summary>
    public IReadOnlyList<string> TopologicalOrder(out IReadOnlyList<string> errors)
    {
        var problems = new List<string>();
        var missing = dependencies.SelectMany(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(x => !dependencies.ContainsKey(x)).ToArray();
        foreach (string name in missing) problems.Add("krok „" + name + "” jest zależnością, ale nie został zdefiniowany");

        var remaining = dependencies.Keys.ToDictionary(x => x, x => dependencies[x].Count, StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        var ready = remaining.Where(x => x.Value == 0).Select(x => x.Key).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        while (ready.Count > 0)
        {
            string node = ready[0];
            ready.RemoveAt(0);
            result.Add(node);
            foreach (var candidate in dependencies.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                if (!dependencies[candidate].Contains(node, StringComparer.OrdinalIgnoreCase)) continue;
                remaining[candidate]--;
                if (remaining[candidate] == 0 && !result.Contains(candidate, StringComparer.OrdinalIgnoreCase)) ready.Add(candidate);
            }
        }
        if (result.Count != dependencies.Count)
        {
            var cycle = dependencies.Keys.Where(x => !result.Contains(x, StringComparer.OrdinalIgnoreCase)).ToArray();
            problems.Add("cykl zależności obejmuje: " + string.Join(" → ", cycle) + " — takiego grafu nie da się wykonać");
        }
        errors = problems;
        return result;
    }

    /// <summary>Poziomy równoległości: kroki w jednym poziomie nie zależą od siebie.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Layers()
    {
        var layers = new List<IReadOnlyList<string>>();
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = dependencies.Keys.ToList();
        while (pending.Count > 0)
        {
            var layer = pending.Where(x => dependencies[x].All(d => placed.Contains(d) || !dependencies.ContainsKey(d)))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            if (layer.Length == 0) break;
            layers.Add(layer);
            foreach (string node in layer) placed.Add(node);
            pending = pending.Where(x => !placed.Contains(x)).ToList();
        }
        return layers;
    }

    /// <summary>
    /// Składnia tekstowa: „a &gt; b” znaczy „b zależy od a”, „b &lt;- a” to samo, „b: a, c” znaczy, że b
    /// zależy od a i c. Instrukcje rozdziela przecinek, średnik lub nowa linia.
    /// </summary>
    public static WorkflowGraph Parse(string spec, out string error)
    {
        error = "";
        var graph = new WorkflowGraph();
        string text = (spec ?? "").Replace(";", ",").Replace("\n", ",");
        foreach (string rawStatement in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string statement = rawStatement.Trim();
            if (statement.Length == 0) continue;
            int colon = statement.IndexOf(':');
            if (colon > 0 && !statement.Contains('>') && !statement.Contains("<-"))
            {
                string node = statement[..colon].Trim();
                graph.Add(node);
                foreach (string dependency in statement[(colon + 1)..].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
                    graph.Add(node, dependency);
                continue;
            }
            if (statement.Contains("<-"))
            {
                string[] halves = statement.Split("<-", 2, StringSplitOptions.None);
                string child = halves[0].Trim();
                graph.Add(child);
                foreach (string parent in halves[1].Split([' ', '\t', '+', '&'], StringSplitOptions.RemoveEmptyEntries))
                    graph.Add(child, parent);
                continue;
            }
            if (statement.Contains('>'))
            {
                // Łańcuch czyta się po ludzku: „a > b > c” to b zależy od a, a c od b.
                string[][] segments = statement.Split('>')
                    .Select(segment => segment.Split([' ', '\t', '+', '&'], StringSplitOptions.RemoveEmptyEntries))
                    .Where(segment => segment.Length > 0)
                    .ToArray();
                for (int index = 0; index < segments.Length; index++)
                {
                    foreach (string node in segments[index])
                    {
                        graph.Add(node);
                        if (index > 0)
                            foreach (string parent in segments[index - 1])
                                graph.Add(node, parent);
                    }
                }
                continue;
            }
            graph.Add(statement);
        }
        if (graph.Nodes.Count == 0) error = "Nie rozpoznałem żadnego kroku. Użyj np. „pobierz > sprawdz > zapisz”.";
        return graph;
    }

    public string Describe()
    {
        var order = TopologicalOrder(out var errors);
        var lines = new List<string> { "Graf: " + Nodes.Count + " kroków · zależności: " + dependencies.Sum(x => x.Value.Count) };
        var layers = Layers();
        for (int i = 0; i < layers.Count; i++)
            lines.Add("Poziom " + (i + 1) + " (można równolegle): " + string.Join(", ", layers[i]));
        lines.Add("Kolejność wykonania: " + string.Join(" → ", order));
        if (errors.Count > 0) lines.AddRange(errors.Select(x => "PROBLEM: " + x));
        else lines.Add("Graf jest poprawny: brak cykli i brakujących zależności.");
        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>Wykonawca workflow: wykonuje pojedynczy krok i zwraca null (sukces) albo powód błędu.</summary>
public sealed class WorkflowRunner
{
    public WorkflowRunner(WorkflowGraph graph, RetryPolicy? policy = null)
    {
        Graph = graph;
        Policy = policy ?? new RetryPolicy(maxAttempts: 1);
    }

    public WorkflowGraph Graph { get; }
    public RetryPolicy Policy { get; }

    /// <summary>Wykonuje graf. Krok z nieudaną zależnością jest pomijany, nie „wykonany”.</summary>
    public IReadOnlyList<StepReport> Run(Func<string, string?> step, Func<string, TimeSpan>? wait = null)
    {
        var order = Graph.TopologicalOrder(out var errors);
        var reports = new List<StepReport>();
        if (errors.Count > 0)
            return [new StepReport("(walidacja)", StepState.Failed, 0, string.Join(" · ", errors), TimeSpan.Zero)];

        var states = new Dictionary<string, StepState>(StringComparer.OrdinalIgnoreCase);
        foreach (string node in order)
        {
            var dependencies = Graph.DependenciesOf(node);
            if (dependencies.Any(d => states.TryGetValue(d, out var state) && state != StepState.Done))
            {
                states[node] = StepState.Skipped;
                reports.Add(new StepReport(node, StepState.Skipped, 0, "pominięte: zależność nie zakończyła się sukcesem", TimeSpan.Zero));
                continue;
            }

            int attempts = 0;
            string lastError = "";
            var started = DateTimeOffset.Now;
            bool done = false;
            while (attempts < Policy.MaxAttempts && !done)
            {
                attempts++;
                string? error;
                try { error = step(node); }
                catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; }
                if (error == null) { done = true; break; }
                lastError = error;
                if (attempts < Policy.MaxAttempts) wait?.Invoke(node);
            }
            var duration = DateTimeOffset.Now - started;
            states[node] = done ? StepState.Done : StepState.Failed;
            reports.Add(new StepReport(node, done ? StepState.Done : StepState.Failed, attempts,
                done ? "wykonane" : lastError, duration));
        }
        return reports;
    }

    public static string Format(IReadOnlyList<StepReport> reports)
    {
        if (reports.Count == 0) return "Workflow nie ma kroków.";
        var lines = new List<string> { "Workflow: " + reports.Count(x => x.State == StepState.Done) + "/" + reports.Count + " kroków wykonanych" };
        foreach (var report in reports)
        {
            string marker = report.State switch
            {
                StepState.Done => "OK",
                StepState.Failed => "BŁĄD",
                StepState.Skipped => "POMINIĘTE",
                _ => report.State.ToString()
            };
            lines.Add("· [" + marker + "] " + report.Name + " (próby: " + report.Attempts + ") — " + report.Detail +
                (report.Duration > TimeSpan.Zero ? " · " + RetryPolicy.Describe(report.Duration) : ""));
        }
        return string.Join(Environment.NewLine, lines);
    }
}
