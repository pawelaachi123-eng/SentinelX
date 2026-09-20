using SentinelX.Models;
using SentinelX.Services.Intent;
using SentinelX.Services.AI;
namespace SentinelX.Services.Actions;

/// <summary>Single execution lane. Stop stays latched until an explicit UI resume.</summary>
public sealed class ActionEngine(IIntentRouter router, SentinelToolboxService toolbox,
    ActionHistoryService history, ConversationMemoryService memory, IAiService ai) : IActionEngine
{
    private readonly object gate = new();
    private CancellationTokenSource? active;
    private bool stopped;
    private readonly List<ActionRecord> tracked = [];
    public bool IsStopped { get { lock (gate) return stopped; } }
    public bool IsBusy { get { lock (gate) return active != null; } }
    public bool HasPendingPermission => toolbox.HasPendingAction;
    public string PermissionSummary => toolbox.PendingSummary;
    public event Action? Changed;
    public event Action<ActionRecord>? ActionStarted;

    public async Task<IntentResult> ExecuteAsync(string input, CancellationToken token = default, bool fromVoice = false)
    {
        input = input.Trim();
        if (input.Length == 0) return new("Wpisz polecenie.");
        if (input.Length > 16000) return new("Polecenie przekracza limit 16 000 znaków.");
        string normalized = ConversationMemoryService.Normalize(input);
        if (normalized is "awaryjny stop" or "emergency stop") { EmergencyStop(); return new("STOP awaryjny aktywny."); }
        if (normalized is "anuluj" or "przerwij" or "anuluj akcje") { Cancel(); return new("Przerwano. Ukończone kroki nie są cofane."); }
        if (fromVoice && normalized is "potwierdz" or "potwierdz akcje" or "confirm")
            return new("Potwierdzenie jest możliwe wyłącznie przyciskiem lub klawiaturą.");
        CancellationTokenSource source;
        lock (gate)
        {
            if (stopped) return new("STOP blokuje nowe akcje. Użyj przycisku Wznów.");
            if (active != null) return new("Trwa zadanie. Poczekaj lub je anuluj.");
            source = active = CancellationTokenSource.CreateLinkedTokenSource(token);
        }
        var record = new ActionRecord { UserRequest = input, Status = ActionStatus.Running };
        lock (gate) { tracked.Add(record); if (tracked.Count > 100) tracked.RemoveAt(0); }
        Changed?.Invoke(); ActionStarted?.Invoke(record);
        string response = "";
        bool hasToolProof = false;
        try
        {
            // File/process queries and JSON I/O never block the dispatcher.
            response = await Task.Run(async () =>
            {
                memory.AddUserMessage(input, fromVoice ? "voice" : "keyboard");
                var text = await router.ProcessAsync(input, source.Token);
                source.Token.ThrowIfCancellationRequested();
                memory.AddAssistantMessage(text);
                return text;
            }, source.Token);
            record.Status = ActionStatus.Unverified;
            record.Evidence = "Odpowiedź nie stanowi dowodu wykonania akcji systemowej. Sprawdź szczegółowy wpis w Historii.";
            // Never infer verification by searching the text of an LLM response.
            var entries = await Task.Run(() => history.GetRecentEntries(20), source.Token);
            var proof = entries.FirstOrDefault(x => x.Timestamp >= record.StartedAt);
            if (proof != null)
            {
                hasToolProof = true;
                record.ActionId = proof.ActionId;
                lock (gate)
                    foreach (var task in tracked.Where(x => x.ActionId == proof.ActionId)) ApplyProof(task, proof);
            }
            return new(response, record);
        }
        catch (OperationCanceledException)
        {
            record.Status = ActionStatus.Cancelled;
            record.Evidence = "Przerwano oczekiwanie i dalsze kroki. Zakończone operacje nie zostały cofnięte.";
            return new(record.Evidence, record);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex); record.Status = ActionStatus.Failed; record.Error = ex.Message;
            return new("Nie udało się wykonać polecenia: " + ex.Message, record);
        }
        finally
        {
            if (record.Status != ActionStatus.WaitingPermission) record.FinishedAt = DateTime.Now;
            if (!hasToolProof)
            {
                var result = record.Status switch
                {
                    ActionStatus.Cancelled => ActionExecutionResult.Cancelled(record.Evidence),
                    ActionStatus.Failed => ActionExecutionResult.Failure(record.Error, record.Evidence),
                    _ => ActionExecutionResult.UnverifiedSuccess(response, record.Evidence)
                };
                await Task.Run(() => history.AddResult(record.ActionId, "REQUEST", input, result,
                    (long)(DateTime.Now - record.StartedAt).TotalMilliseconds));
            }
            if (IsStopped) toolbox.CancelPendingAction();
            lock (gate) { active = null; source.Dispose(); }
            Changed?.Invoke();
        }
    }
    private static void ApplyProof(ActionRecord task, ActionHistoryEntry proof)
    {
        task.ActionType = proof.ActionType;
        task.Risk = proof.ActionType == "CLOSE_APP" ? RiskLevel.Medium : RiskLevel.Low;
        task.Status = proof.Status switch
        {
            "VERIFIED" => ActionStatus.Verified, "FAILED" => ActionStatus.Failed,
            "CANCELLED" or "EXPIRED" => ActionStatus.Cancelled, "PENDING" or "WAITING_PERMISSION" => ActionStatus.WaitingPermission,
            _ => ActionStatus.Unverified
        };
        task.Evidence = proof.Evidence;
        if (task.Status != ActionStatus.WaitingPermission) task.FinishedAt = proof.Timestamp;
    }
    public void Cancel()
    {
        lock (gate) active?.Cancel();
        ai.Cancel(); toolbox.CancelAllTasks(); toolbox.CancelPendingAction();
        lock (gate)
            foreach (var task in tracked.Where(x => x.Status == ActionStatus.WaitingPermission))
            { task.Status = ActionStatus.Cancelled; task.FinishedAt = DateTime.Now; task.Evidence = "Użytkownik anulował oczekującą zgodę."; }
        Changed?.Invoke();
    }
    public void EmergencyStop() { lock (gate) stopped = true; Cancel(); }
    public void Resume() { lock (gate) stopped = false; Changed?.Invoke(); }
}
