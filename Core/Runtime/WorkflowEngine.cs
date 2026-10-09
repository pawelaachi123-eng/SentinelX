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
/// SEKCJA 1 ┬Ě pozycje 16ÔÇô17 ÔÇö Workflow Engine (DAG) i Pipeline Builder.
/// <para>Graf jest walidowany przed uruchomieniem: brakuj─ůce zale┼╝no┼Ťci i cykle s─ů zg┼éaszane jako b┼é─Ödy,
/// a nie ÔÇ×wykonane po cichu w z┼éej kolejno┼ŤciÔÇŁ. Kroki, kt├│rych zale┼╝no┼Ť─ç pad┼éa, s─ů pomijane (nie udaj─ů
/// sukcesu), a kolejno┼Ť─ç wykonania wynika wy┼é─ůcznie z grafu.</para>
/// </summary>
public sealed class WorkflowGraph
{
    private readonly Dictionary<string, List<string>> dependencies = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> order = new();

    public WorkflowGraph Add(string node, params string[] dependsOn)
    {
        string name = (node ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException("Nazwa kroku nie mo┼╝e by─ç pusta.", nameof(node));
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

    /// <summary>Kolejno┼Ť─ç topologiczna (Kahn). Przy b┼é─Ödzie zwraca cz─Ö┼Ťciow─ů kolejno┼Ť─ç i list─Ö problem├│w.</summary>
    public IReadOnlyList<string> TopologicalOrder(out IReadOnlyList<string> errors)
    {
        var problems = new List<string>();
        var missing = dependencies.SelectMany(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(x => !dependencies.ContainsKey(x)).ToArray();
        foreach (string name in missing) problems.Add("krok ÔÇ×" + name + "ÔÇŁ jest zale┼╝no┼Ťci─ů, ale nie zosta┼é zdefiniowany");

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
            problems.Add("cykl zale┼╝no┼Ťci obejmuje: " + string.Join(" Ôćĺ ", cycle) + " ÔÇö takiego grafu nie da si─Ö wykona─ç");
        }
        errors = problems;
        return result;
    }

    /// <summary>Poziomy r├│wnoleg┼éo┼Ťci: kroki w jednym poziomie nie zale┼╝─ů od siebie.</summary>
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
    /// Sk┼éadnia tekstowa: ÔÇ×a &gt; bÔÇŁ znaczy ÔÇ×b zale┼╝y od aÔÇŁ, ÔÇ×b &lt;- aÔÇŁ to samo, ÔÇ×b: a, cÔÇŁ znaczy, ┼╝e b
    /// zale┼╝y od a i c. Instrukcje rozdziela przecinek, ┼Ťrednik lub nowa linia.
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
                // ┼üa┼äcuch czyta si─Ö po ludzku: ÔÇ×a > b > cÔÇŁ to b zale┼╝y od a, a c od b.
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
        if (graph.Nodes.Count == 0) error = "Nie rozpozna┼éem ┼╝adnego kroku. U┼╝yj np. ÔÇ×pobierz > sprawdz > zapiszÔÇŁ.";
        return graph;
    }

    public string Describe()
    {
        var order = TopologicalOrder(out var errors);
        var lines = new List<string> { "Graf: " + Nodes.Count + " krok├│w ┬Ě zale┼╝no┼Ťci: " + dependencies.Sum(x => x.Value.Count) };
        var layers = Layers();
        for (int i = 0; i < layers.Count; i++)
            lines.Add("Poziom " + (i + 1) + " (mo┼╝na r├│wnolegle): " + string.Join(", ", layers[i]));
        lines.Add("Kolejno┼Ť─ç wykonania: " + string.Join(" Ôćĺ ", order));
        if (errors.Count > 0) lines.AddRange(errors.Select(x => "PROBLEM: " + x));
        else lines.Add("Graf jest poprawny: brak cykli i brakuj─ůcych zale┼╝no┼Ťci.");
        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>Wykonawca workflow: wykonuje pojedynczy krok i zwraca null (sukces) albo pow├│d b┼é─Ödu.</summary>
public sealed class WorkflowRunner
{
    public WorkflowRunner(WorkflowGraph graph, RetryPolicy? policy = null)
    {
        Graph = graph;
        Policy = policy ?? new RetryPolicy(maxAttempts: 1);
    }

    public WorkflowGraph Graph { get; }
    public RetryPolicy Policy { get; }

    /// <summary>Wykonuje graf. Krok z nieudan─ů zale┼╝no┼Ťci─ů jest pomijany, nie ÔÇ×wykonanyÔÇŁ.</summary>
    public IReadOnlyList<StepReport> Run(Func<string, string?> step, Func<string, TimeSpan>? wait = null)
    {
        var order = Graph.TopologicalOrder(out var errors);
        var reports = new List<StepReport>();
        if (errors.Count > 0)
            return [new StepReport("(walidacja)", StepState.Failed, 0, string.Join(" ┬Ě ", errors), TimeSpan.Zero)];

        var states = new Dictionary<string, StepState>(StringComparer.OrdinalIgnoreCase);
        foreach (string node in order)
        {
            var dependencies = Graph.DependenciesOf(node);
            if (dependencies.Any(d => states.TryGetValue(d, out var state) && state != StepState.Done))
            {
                states[node] = StepState.Skipped;
                reports.Add(new StepReport(node, StepState.Skipped, 0, "pomini─Öte: zale┼╝no┼Ť─ç nie zako┼äczy┼éa si─Ö sukcesem", TimeSpan.Zero));
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
        if (reports.Count == 0) return "Workflow nie ma krok├│w.";
        var lines = new List<string> { "Workflow: " + reports.Count(x => x.State == StepState.Done) + "/" + reports.Count + " krok├│w wykonanych" };
        foreach (var report in reports)
        {
            string marker = report.State switch
            {
                StepState.Done => "OK",
                StepState.Failed => "B┼ü─äD",
                StepState.Skipped => "POMINI─śTE",
                _ => report.State.ToString()
            };
            lines.Add("┬Ě [" + marker + "] " + report.Name + " (pr├│by: " + report.Attempts + ") ÔÇö " + report.Detail +
                (report.Duration > TimeSpan.Zero ? " ┬Ě " + RetryPolicy.Describe(report.Duration) : ""));
        }
        return string.Join(Environment.NewLine, lines);
    }
}
