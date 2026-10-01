namespace SentinelX.Services.History;

public enum HistoryKind
{
    Command,
    Tool,
    Skill,
    Task,
    Automation
}

public sealed record HistoryEntry(
    string Id,
    HistoryKind Kind,
    string Name,
    bool Success,
    string? Summary,
    DateTimeOffset At,
    long? DurationMs = null,
    string? Error = null,
    Dictionary<string, string>? Labels = null);
