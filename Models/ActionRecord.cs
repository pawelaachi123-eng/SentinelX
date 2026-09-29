using CommunityToolkit.Mvvm.ComponentModel;
namespace SentinelX.Models;

public enum ActionStatus { Queued, WaitingPermission, Running, Verifying, Verified, Unverified, Failed, Cancelled, RolledBack }
public enum RiskLevel { Low, Medium, High, Critical }
public enum VoiceState { Off, Standby, Active }
public partial class ActionRecord : ObservableObject
{
    [ObservableProperty] private string actionId = $"SX-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
    [ObservableProperty] private string actionType = "REQUEST";
    [ObservableProperty] private RiskLevel risk;
    public string UserRequest { get; init; } = "";
    public DateTime StartedAt { get; init; } = DateTime.Now;
    [ObservableProperty] private ActionStatus status = ActionStatus.Queued;
    [ObservableProperty] private string evidence = "";
    [ObservableProperty] private string error = "";
    [ObservableProperty] private DateTime? finishedAt;
    [ObservableProperty] private string phase = "W kolejce";
    [ObservableProperty] private long elapsedMilliseconds;
    [ObservableProperty] private IReadOnlyList<ActionHistoryEntry> toolResults = [];
    [ObservableProperty] private string storageWarning = "";
    [ObservableProperty] private string recoveryAdvice = "";   // R1: wykonywalna rada po porażce
    [ObservableProperty] private string reasoningTrace = "";  // R2: PLAN/PRÓBA/CHECK/NAPRAWA
}
public sealed record IntentResult(string Text, ActionRecord? Action = null);
public sealed record ConversationMessage(string Role, string Content, DateTime Timestamp, ActionRecord? ActionRecord = null);
