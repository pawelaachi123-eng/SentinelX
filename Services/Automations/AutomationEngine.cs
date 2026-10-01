using System.Collections.Concurrent;
using SentinelX.Models.Automations;

namespace SentinelX.Services.Automations;

/// <summary>
/// Prosty silnik automatyzacji WHEN-IF-THEN. Każda akcja i trigger jest rozpinana przez
/// zarejestrowane handlery — nic nie jest hardkodowane. Brak zewnętrznych zależności.
/// </summary>
public sealed class AutomationEngine
{
    private readonly ConcurrentDictionary<string, AutomationRule> rules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<AutomationAction, CancellationToken, Task<AutomationRunResult>>> actionHandlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<AutomationCondition, bool>> conditionEvaluate = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<AutomationRunRecord> history = new();

    public event Action<AutomationRunRecord>? RuleExecuted;

    public void RegisterAction(string id, Func<AutomationAction, CancellationToken, Task<AutomationRunResult>> handler) =>
        actionHandlers[id] = handler;

    public void RegisterCondition(string id, Func<AutomationCondition, bool> eval) =>
        conditionEvaluate[id] = eval;

    public void AddOrUpdate(AutomationRule rule) => rules[rule.Id] = rule;
    public bool Remove(string id) => rules.TryRemove(id, out _);
    public AutomationRule? Get(string id) => rules.TryGetValue(id, out var r) ? r : null;
    public IReadOnlyList<AutomationRule> All() => rules.Values.OrderBy(r => r.Name).ToList();

    public IEnumerable<AutomationRunRecord> RecentHistory(int count = 100) =>
        history.TakeLast(count).Reverse();

    public async Task FireTriggerAsync(string triggerId, Dictionary<string, string>? ctx, CancellationToken token)
    {
        ctx ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules.Values.Where(r => r.Enabled && r.When.Kind.ToString().Equals(triggerId, StringComparison.OrdinalIgnoreCase)))
        {
            if (rule.When.Parameters.Any(p => !ctx.TryGetValue(p.Key, out var v) || v != p.Value))
                continue;

            bool ok = true;
            foreach (var c in rule.If)
            {
                if (!conditionEvaluate.TryGetValue(c.Expression, out var eval) || !eval(c)) { ok = false; break; }
            }
            if (!ok) continue;

            foreach (var action in rule.Then)
            {
                var result = await RunAction(action, token).ConfigureAwait(false);
                var rec = new AutomationRunRecord(rule.Id, rule.Name, action.ActionId, result.Success, result.Message, DateTimeOffset.UtcNow);
                history.Enqueue(rec);
                TrimHistory();
                RuleExecuted?.Invoke(rec);
                if (!result.Success) break; // fail-fast per rule
            }
        }
    }

    private async Task<AutomationRunResult> RunAction(AutomationAction a, CancellationToken ct)
    {
        if (!actionHandlers.TryGetValue(a.ActionId, out var h))
            return AutomationRunResult.Fail($"Nieznana akcja: {a.ActionId}");
        try { return await h(a, ct).ConfigureAwait(false); }
        catch (Exception ex) { return AutomationRunResult.Fail(ex.Message); }
    }

    private void TrimHistory()
    {
        while (history.Count > 500 && history.TryDequeue(out _)) { }
    }
}

public sealed record AutomationRunResult(bool Success, string? Message)
{
    public static AutomationRunResult Ok(string? m = null) => new(true, m);
    public static AutomationRunResult Fail(string m) => new(false, m);
}

public sealed record AutomationRunRecord(string RuleId, string RuleName, string ActionId, bool Success, string? Message, DateTimeOffset At);
