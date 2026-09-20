using SentinelX.Models;
namespace SentinelX.Services.Actions;
public interface IActionEngine
{
    bool IsStopped { get; }
    bool IsBusy { get; }
    ActionRecord? CurrentAction { get; }
    bool HasPendingPermission { get; }
    string PermissionSummary { get; }
    event Action? Changed;
    event Action<ActionRecord>? ActionStarted;
    Task<IntentResult> ExecuteAsync(string input, CancellationToken token = default, bool fromVoice = false);
    void Cancel();
    void EmergencyStop();
    void Resume();
}
