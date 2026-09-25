using SentinelX.Models;
namespace SentinelX.Services.Actions;
public interface IActionEngine
{
    bool IsStopped { get; }
    bool IsBusy { get; }
    ActionRecord? CurrentAction { get; }
    bool HasPendingPermission { get; }
    /// <summary>True while the language model is streaming an answer.</summary>
    bool IsStreaming { get; }
    /// <summary>Every visible chunk of a streamed answer, in arrival order.</summary>
    event Action<string>? StreamDelta;
    string PermissionSummary { get; }
    event Action? Changed;
    event Action<ActionRecord>? ActionStarted;
    Task<IntentResult> ExecuteAsync(string input, CancellationToken token = default, bool fromVoice = false, Action<string>? onDelta = null);
    void Cancel();
    void EmergencyStop();
    void Resume();
}
