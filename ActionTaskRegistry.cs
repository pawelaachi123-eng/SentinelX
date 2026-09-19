using System.Threading;

namespace SentinelX;

public sealed record ActionTaskSnapshot(string Id, string Description, string Status, DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt, int CompletedSteps, int TotalSteps, string CurrentStep);

/// <summary>Tracks active work and a bounded recent history. Cancellation stops future steps, never rolls back launches.</summary>
public sealed class ActionTaskRegistry
{
    private readonly object gate = new();
    private readonly Dictionary<string, ActionTaskHandle> active = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<ActionTaskSnapshot> recent = new();

    public ActionTaskHandle Begin(string id, string description, int totalSteps, CancellationToken token)
    {
        var task = new ActionTaskHandle(this, id, description, Math.Max(1, totalSteps), token);
        lock (gate) active.Add(id, task);
        return task;
    }

    public IReadOnlyList<ActionTaskSnapshot> GetTasks()
    {
        lock (gate) return active.Values.Select(x => x.Snapshot()).Concat(recent.Reverse()).ToArray();
    }

    public int CancelAll()
    {
        ActionTaskHandle[] tasks;
        lock (gate) tasks = active.Values.ToArray();
        foreach (var task in tasks) task.Cancel();
        return tasks.Length;
    }

    internal void Finish(ActionTaskHandle task)
    {
        lock (gate)
        {
            if (!active.Remove(task.Id)) return;
            recent.Enqueue(task.Snapshot());
            while (recent.Count > 50) recent.Dequeue();
        }
    }
}

public sealed class ActionTaskHandle : IDisposable
{
    private readonly ActionTaskRegistry owner;
    private readonly CancellationTokenSource cancellation;
    private readonly object gate = new();
    private readonly DateTimeOffset startedAt = DateTimeOffset.Now;
    private DateTimeOffset? finishedAt;
    private string status = "RUNNING", currentStep = "";
    private int completedSteps;
    public string Id { get; }
    public string Description { get; }
    public int TotalSteps { get; }
    public CancellationToken Token => cancellation.Token;

    internal ActionTaskHandle(ActionTaskRegistry owner, string id, string description, int totalSteps, CancellationToken token)
    {
        this.owner = owner; Id = id; Description = description; TotalSteps = totalSteps;
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
    }

    public void SetStep(string description) { lock (gate) currentStep = description; }
    public void CompleteStep() { lock (gate) completedSteps = Math.Min(TotalSteps, completedSteps + 1); }
    public void Cancel()
    {
        lock (gate)
        {
            if (finishedAt.HasValue) return;
            status = "CANCELLING";
            cancellation.Cancel();
        }
    }
    public void Complete(string finalStatus)
    {
        lock (gate)
        {
            if (finishedAt.HasValue) return;
            finishedAt = DateTimeOffset.Now; status = finalStatus;
        }
        owner.Finish(this);
    }
    public ActionTaskSnapshot Snapshot()
    { lock (gate) return new(Id, Description, status, startedAt, finishedAt, completedSteps, TotalSteps, currentStep); }
    public void Dispose()
    {
        Complete(cancellation.IsCancellationRequested ? "CANCELLED" : "INTERRUPTED");
        cancellation.Dispose();
    }
}
