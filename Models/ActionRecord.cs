using CommunityToolkit.Mvvm.ComponentModel;
namespace SentinelX.Models;

public enum ActionStatus { Queued, WaitingPermission, Running, Verifying, Verified, Unverified, Failed, Cancelled, RolledBack }
public enum RiskLevel { Low, Medium, High, Critical }
public enum VoiceState { Off, Standby, Active }
public partial class ActionRecord : ObservableObject
{
    public string ActionId { get; init; } = $"SX-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
    public string UserRequest { get; init; } = "";
    public DateTime StartedAt { get; init; } = DateTime.Now;
    [ObservableProperty] private ActionStatus status = ActionStatus.Queued;
    [ObservableProperty] private string evidence = "";
    [ObservableProperty] private string error = "";
    [ObservableProperty] private DateTime? finishedAt;
}
public sealed record IntentResult(string Text, ActionRecord? Action = null);
public sealed record ConversationMessage(string Role, string Content, DateTime Timestamp, ActionRecord? ActionRecord = null);
