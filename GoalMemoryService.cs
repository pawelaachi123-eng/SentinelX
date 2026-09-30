using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SentinelX;

public sealed record GoalStepState(string Name, string Status, string Result, DateTimeOffset UpdatedAt)
{
    public string[] DependsOn { get; set; } = [];
}
public sealed record AutopilotGoalSnapshot(string Id, string Goal, string Status, string CurrentStep, string NextStep,
    DateTimeOffset StartedAt, DateTimeOffset UpdatedAt, IReadOnlyList<GoalStepState> Steps);

/// <summary>Small local goal checkpoint store. Private-mode goals stay memory-only; no tool output or raw transcript is stored.</summary>
public sealed class GoalMemoryService
{
    private const int MaximumGoals = 20;
    private const int MaximumSteps = 12;
    private readonly object gate = new();
    private readonly string path;
    private readonly List<StoredGoal> goals = [];
    private bool persistenceRemovalPending;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string? LastStorageError { get; private set; }
    public Func<bool>? PersistenceAllowedProvider { get; set; }

    public GoalMemoryService(string? path = null, Func<bool>? persistenceAllowedProvider = null)
    {
        this.path = path ?? Path.Combine(AppPaths.Root, "Autopilot", "goals.json");
        PersistenceAllowedProvider = persistenceAllowedProvider;
        // A private session must not read durable goal text into process memory.
        if (CanPersistNow()) Load();
    }

    public AutopilotGoalSnapshot Start(string goal, IReadOnlyList<string> plannedSteps, bool persist,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? dependencies = null)
    {
        string[] planNames = plannedSteps.Take(MaximumSteps).ToArray();
        ValidateDependencies(planNames, dependencies);
        string safeGoal = SensitiveDataRedactor.Redact((goal ?? "").Trim());
        if (safeGoal.Length > 180) safeGoal = safeGoal[..177] + "…";
        var entry = new StoredGoal
        {
            Id = "G-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
            Goal = safeGoal,
            Status = "RUNNING",
            CurrentStep = "",
            NextStep = planNames.FirstOrDefault() ?? "",
            StartedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now,
            PersistAllowed = persist && CanPersistNow(),
            Steps = planNames.Select(x => new GoalStepState(x, "PENDING", "", DateTimeOffset.Now)
            {
                DependsOn = dependencies != null && dependencies.TryGetValue(x, out var required)
                    ? required.Distinct(StringComparer.Ordinal).ToArray()
                    : []
            }).ToList()
        };
        lock (gate)
        {
            goals.Add(entry);
            while (goals.Count > MaximumGoals) goals.RemoveAt(0);
            SaveLocked();
            return Snapshot(entry);
        }
    }

    public bool SetStep(string id, string stepName, string status, string safeResult, string nextStep)
    {
        lock (gate)
        {
            var goal = Find(id);
            if (goal == null) return false;
            if (!CanPersistNow()) DisableGoalPersistenceLocked(goal);
            string result = SensitiveDataRedactor.Redact(safeResult ?? "");
            if (result.Length > 220) result = result[..217] + "…";
            DateTimeOffset now = DateTimeOffset.Now;
            int index = goal.Steps.FindIndex(x => x.Name == stepName);
            var step = new GoalStepState(stepName, status, result, now);
            if (index >= 0) goal.Steps[index] = step with { DependsOn = goal.Steps[index].DependsOn };
            else if (goal.Steps.Count < MaximumSteps) goal.Steps.Add(step);
            goal.CurrentStep = status is "RUNNING" or "WAITING" ? stepName : goal.CurrentStep;
            goal.NextStep = nextStep ?? "";
            goal.UpdatedAt = now;
            goal.Status = status == "FAILED" ? "RUNNING" : goal.Status;
            SaveLocked();
            return true;
        }
    }

    public bool MarkWaiting(string id, string stepName, string safeResult, string nextStep)
    {
        lock (gate)
        {
            var goal = Find(id);
            if (goal == null) return false;
            if (!CanPersistNow()) DisableGoalPersistenceLocked(goal);
            string result = SensitiveDataRedactor.Redact(safeResult ?? "");
            if (result.Length > 220) result = result[..217] + "…";
            DateTimeOffset now = DateTimeOffset.Now;
            int index = goal.Steps.FindIndex(x => x.Name == stepName);
            var step = new GoalStepState(stepName, "WAITING", result, now);
            if (index >= 0) goal.Steps[index] = step with { DependsOn = goal.Steps[index].DependsOn };
            else if (goal.Steps.Count < MaximumSteps) goal.Steps.Add(step);
            goal.Status = "WAITING";
            goal.CurrentStep = stepName;
            goal.NextStep = nextStep ?? "";
            goal.UpdatedAt = now;
            SaveLocked();
            return true;
        }
    }

    public bool Finish(string id, string status, string result, string nextStep = "")
    {
        lock (gate)
        {
            var goal = Find(id);
            if (goal == null) return false;
            if (!CanPersistNow()) DisableGoalPersistenceLocked(goal);
            string safe = SensitiveDataRedactor.Redact(result ?? "");
            if (safe.Length > 300) safe = safe[..297] + "…";
            goal.Status = status;
            goal.NextStep = nextStep;
            goal.UpdatedAt = DateTimeOffset.Now;
            if (goal.CurrentStep.Length > 0)
            {
                int index = goal.Steps.FindIndex(x => x.Name == goal.CurrentStep);
                if (index >= 0) goal.Steps[index] = goal.Steps[index] with { Status = status, Result = safe, UpdatedAt = goal.UpdatedAt };
            }
            else if (goal.Steps.Count < MaximumSteps)
                goal.Steps.Add(new GoalStepState("report", status, safe, goal.UpdatedAt));
            SaveLocked();
            return true;
        }
    }

    public IReadOnlyList<AutopilotGoalSnapshot> GetRecent(int count = 5)
    {
        lock (gate) return goals.TakeLast(Math.Clamp(count, 1, MaximumGoals)).Reverse().Select(Snapshot).ToArray();
    }

    public void DisableActivePersistence()
    {
        lock (gate)
        {
            bool changed = false;
            foreach (var goal in goals.Where(x => x.Status is "RUNNING" or "WAITING"))
            {
                if (!goal.PersistAllowed) continue;
                DisableGoalPersistenceLocked(goal);
                changed = true;
            }
            if (changed) SaveLocked();
        }
    }

    public string FormatLatest()
    {
        lock (gate)
        {
            var goal = goals.LastOrDefault();
            if (goal == null) return "Brak zapisanego celu Autopilota w tej sesji.";
            var lines = new List<string>
            {
                $"GOAL {goal.Id}: {goal.Goal}",
                $"STATUS: {goal.Status}",
                $"CURRENT STEP: {(goal.CurrentStep.Length == 0 ? "—" : goal.CurrentStep)}",
                $"NEXT STEP: {(goal.NextStep.Length == 0 ? "—" : goal.NextStep)}"
            };
            lines.AddRange(goal.Steps.Select(x => $"· {x.Name} [{x.Status}]" +
                (x.DependsOn.Length == 0 ? "" : " po: " + string.Join(", ", x.DependsOn)) +
                (x.Result.Length == 0 ? "" : " — " + x.Result)));
            if (LastStorageError != null) lines.Add("Checkpoint lokalny nie został w pełni zapisany: " + LastStorageError);
            return string.Join("\n", lines);
        }
    }

    private static void ValidateDependencies(IReadOnlyList<string> steps, IReadOnlyDictionary<string, IReadOnlyList<string>>? dependencies)
    {
        if (steps.Any(string.IsNullOrWhiteSpace) || steps.Distinct(StringComparer.Ordinal).Count() != steps.Count)
            throw new ArgumentException("Graph steps must have unique, non-empty names.", nameof(steps));
        if (dependencies == null) return;
        var known = steps.ToHashSet(StringComparer.Ordinal);
        foreach (var pair in dependencies)
        {
            if (!known.Contains(pair.Key) || pair.Value.Any(dep => !known.Contains(dep) || dep == pair.Key))
                throw new ArgumentException("Every dependency must name a different step in this plan.", nameof(dependencies));
        }
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        bool Visit(string node)
        {
            if (visited.Contains(node)) return true;
            if (!visiting.Add(node)) return false;
            if (dependencies.TryGetValue(node, out var required))
                foreach (string dependency in required)
                    if (!Visit(dependency)) return false;
            visiting.Remove(node); visited.Add(node); return true;
        }
        if (steps.Any(node => !Visit(node))) throw new ArgumentException("Goal step dependencies must form an acyclic graph.", nameof(dependencies));
    }

    private StoredGoal? Find(string id) => goals.LastOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static AutopilotGoalSnapshot Snapshot(StoredGoal item) => new(item.Id, item.Goal, item.Status,
        item.CurrentStep, item.NextStep, item.StartedAt, item.UpdatedAt,
        item.Steps.Select(step => step with { DependsOn = (step.DependsOn ?? []).ToArray() }).ToArray());

    private void Load()
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 256_000) return;
            var loaded = JsonSerializer.Deserialize<List<StoredGoal>>(File.ReadAllText(path), JsonOptions) ?? [];
            foreach (var item in loaded.TakeLast(MaximumGoals))
            {
                if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Goal) || item.Goal.Length > 180) continue;
                item.Steps = (item.Steps ?? []).Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name))
                    .GroupBy(x => x.Name, StringComparer.Ordinal).Select(group => group.First()).Take(MaximumSteps).ToList();
                var stepNames = item.Steps.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
                foreach (var step in item.Steps)
                    step.DependsOn = (step.DependsOn ?? []).Where(name => stepNames.Contains(name) && name != step.Name)
                        .Distinct(StringComparer.Ordinal).Take(MaximumSteps).ToArray();
                try
                {
                    var dependencyMap = item.Steps.ToDictionary(x => x.Name, x => (IReadOnlyList<string>)x.DependsOn, StringComparer.Ordinal);
                    ValidateDependencies(item.Steps.Select(x => x.Name).ToArray(), dependencyMap);
                }
                catch (ArgumentException) { foreach (var step in item.Steps) step.DependsOn = []; }
                item.CurrentStep ??= "";
                item.NextStep ??= "";
                item.Status ??= "INTERRUPTED";
                item.PersistAllowed = true;
                item.PersistedOnDisk = true;
                if (item.Status == "RUNNING")
                {
                    item.Status = "INTERRUPTED";
                    item.NextStep = string.IsNullOrEmpty(item.CurrentStep) ? item.NextStep : item.CurrentStep;
                }
                goals.Add(item);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { LastStorageError = ex.Message; }
    }

    private void SaveLocked()
    {
        string temp = path + ".tmp";
        try
        {
            bool allowPersistence = CanPersistNow();
            if (!allowPersistence)
            {
                foreach (var goal in goals.Where(x => x.Status is "RUNNING" or "WAITING"))
                    DisableGoalPersistenceLocked(goal);
                if (!persistenceRemovalPending)
                {
                    if (LastStorageError == null || !LastStorageError.StartsWith("Nie udało się sprawdzić ustawień prywatności", StringComparison.Ordinal))
                        LastStorageError = null;
                    return;
                }
            }
            string? directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory)) { LastStorageError = "Nieprawidłowa ścieżka checkpointu."; return; }
            Directory.CreateDirectory(directory);
            var durable = goals.Where(x => x.PersistAllowed).TakeLast(MaximumGoals).ToList();
            File.WriteAllText(temp, JsonSerializer.Serialize(durable, JsonOptions));
            File.Move(temp, path, true);
            foreach (var goal in goals) goal.PersistedOnDisk = goal.PersistAllowed;
            persistenceRemovalPending = false;
            LastStorageError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            try { File.Delete(temp); } catch { }
            LastStorageError = ex.Message;
        }
    }

    private bool CanPersistNow()
    {
        try { return PersistenceAllowedProvider?.Invoke() ?? true; }
        catch (Exception ex)
        {
            LastStorageError = "Nie udało się sprawdzić ustawień prywatności: " + ex.Message;
            return false;
        }
    }

    private void DisableGoalPersistenceLocked(StoredGoal goal)
    {
        if (!goal.PersistAllowed) return;
        goal.PersistAllowed = false;
        if (goal.PersistedOnDisk) persistenceRemovalPending = true;
    }

    private sealed class StoredGoal
    {
        public StoredGoal() { }
        public string Id { get; set; } = "";
        public string Goal { get; set; } = "";
        public string Status { get; set; } = "RUNNING";
        public string CurrentStep { get; set; } = "";
        public string NextStep { get; set; } = "";
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public bool PersistAllowed { get; set; }
        [JsonIgnore] public bool PersistedOnDisk { get; set; }
        public List<GoalStepState> Steps { get; set; } = [];
    }
}
