namespace SentinelX.Services.History;

/// <summary>
/// Async execution-local evidence. Wall clocks, old JSON rows and other concurrent requests are
/// never a source of authority. Detached child work cannot write into a completed capture.
/// </summary>
public sealed class ActionEvidenceCapture : IDisposable
{
    private static readonly AsyncLocal<ActionEvidenceCapture?> ambient = new();
    private readonly ActionEvidenceCapture? parent;
    private readonly object gate = new();
    private readonly Dictionary<string, ActionHistoryEntry> entries = new(StringComparer.Ordinal);
    private bool closed, overflow;
    public string RequestId { get; }
    private ActionEvidenceCapture(string requestId)
    { RequestId = requestId; parent = ambient.Value; ambient.Value = this; }
    public static ActionEvidenceCapture Begin(string requestId) => new(requestId);
    public static void Record(ActionHistoryEntry entry)
    {
        var capture = ambient.Value;
        if (capture == null) return;
        lock (capture.gate)
        {
            if (capture.closed) return;
            entry.RequestId = capture.RequestId;
            if (capture.entries.Count < 256 || capture.entries.ContainsKey(entry.ActionId))
                capture.entries[entry.ActionId] = Copy(entry);
            else capture.overflow = true;
        }
    }
    public IReadOnlyList<ActionHistoryEntry> Snapshot()
    {
        lock (gate)
        {
            var result = entries.Values.Select(Copy).ToList();
            if (overflow) result.Add(new() { ActionId = RequestId + "-LIMIT", RequestId = RequestId,
                Status = "UNVERIFIED", ActionType = "AUDIT_LIMIT", Message = "Przekroczono limit 256 dowodów. Wynik wymaga ręcznego sprawdzenia." });
            return result;
        }
    }
    private static ActionHistoryEntry Copy(ActionHistoryEntry e) => new()
    {
        ActionId = e.ActionId, RequestId = e.RequestId, Timestamp = e.Timestamp,
        ActionType = e.ActionType, Command = e.Command, Status = e.Status, Message = e.Message,
        Evidence = e.Evidence, ParentActionId = e.ParentActionId, SessionId = e.SessionId,
        DurationMilliseconds = e.DurationMilliseconds, RecoveryAdvice = e.RecoveryAdvice
    };
    public void Dispose()
    {
        lock (gate) closed = true;
        if (ReferenceEquals(ambient.Value, this)) ambient.Value = parent;
    }
}
