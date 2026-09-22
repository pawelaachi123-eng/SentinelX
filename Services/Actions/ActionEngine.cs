using System.Diagnostics;
using SentinelX.Core;
using SentinelX.Models;
using SentinelX.Services.Intent;
using SentinelX.Services.AI;
using SentinelX.Services.History;
namespace SentinelX.Services.Actions;

/// <summary>Single execution lane with latched stop, immutable request IDs and execution-scoped proof.</summary>
public sealed class ActionEngine(IIntentRouter router, SentinelToolboxService toolbox,
    ActionHistoryService history, ConversationMemoryService memory, IAiService ai, IUiDispatcher? dispatcher = null) : IActionEngine
{
    private readonly object gate = new();
    private CancellationTokenSource? active;
    private bool stopped;
    private ActionRecord? currentAction;
    private readonly List<ActionRecord> tracked = [];
    public bool IsStopped { get { lock (gate) return stopped; } }
    public bool IsBusy { get { lock (gate) return active != null; } }
    public ActionRecord? CurrentAction { get { lock (gate) return currentAction; } }
    public bool HasPendingPermission => toolbox.HasPendingAction;
    public string PermissionSummary => toolbox.PendingSummary;
    public event Action? Changed;
    public event Action<ActionRecord>? ActionStarted;

    public async Task<IntentResult> ExecuteAsync(string input, CancellationToken token = default, bool fromVoice = false)
    {
        input = input.Trim();
        if (input.Length == 0) return new("Wpisz polecenie.");
        if (input.Length > 16000) return new("Polecenie przekracza limit 16 000 znaków.");
        string normalized = CommandText.Normalize(input);
        if (normalized is "awaryjny stop" or "emergency stop") { EmergencyStop(); return new("STOP awaryjny aktywny."); }
        if (normalized is "anuluj" or "przerwij" or "anuluj akcje") { Cancel(); return new("Przerwano. Ukończone kroki nie są cofane."); }
        // Check after the SAME normalization used by routing: wake words must not bypass approval.
        if (fromVoice && CommandText.IsApproval(normalized))
            return new("Potwierdzenie jest możliwe wyłącznie przyciskiem lub klawiaturą.");
        CancellationTokenSource source;
        var record = new ActionRecord { UserRequest = input, Status = ActionStatus.Running, Phase = "Przygotowanie polecenia" };
        lock (gate)
        {
            if (stopped) return new("STOP blokuje nowe akcje. Użyj przycisku Wznów.");
            if (active != null) return new("Trwa zadanie. Poczekaj lub je anuluj.");
            source = active = CancellationTokenSource.CreateLinkedTokenSource(token);
            currentAction = record;
            tracked.Add(record); if (tracked.Count > 100) tracked.RemoveAt(0);
        }
        var clock = Stopwatch.StartNew();
        using var tickerStop = new CancellationTokenSource();
        var ticker = UpdateElapsedAsync(record, clock, tickerStop.Token);
        PublishChanged(); PublishStarted(record);
        string response = "";
        ActionEvidenceCapture? capture = null;
        try
        {
            record.Phase = "Wykonywanie polecenia · możesz je przerwać";
            response = await Task.Run(async () =>
            {
                history.AddRunning(record.ActionId, "REQUEST", AuditText(input));
                memory.AddUserMessage(input, fromVoice ? "voice" : "keyboard");
                using var approval = ApprovalContext.Begin(input, fromVoice);
                using var scope = ActionEvidenceCapture.Begin(record.ActionId);
                capture = scope;
                var text = await router.ProcessAsync(input, source.Token);
                source.Token.ThrowIfCancellationRequested();
                memory.AddAssistantMessage(text);
                return text;
            }, source.Token);
            record.Status = ActionStatus.Verifying;
            record.Phase = "Sprawdzanie dowodów z narzędzi";
            record.ToolResults = capture?.Snapshot() ?? [];
            ApplyProof(record);
            UpdateWaitingRequests(record.ToolResults);
            return new(response, record);
        }
        catch (OperationCanceledException)
        {
            record.ToolResults = capture?.Snapshot() ?? [];
            record.Status = ActionStatus.Cancelled;
            record.Evidence = "Przerwano oczekiwanie i dalsze kroki. Ukończone operacje NIE zostały cofnięte." + FormatProof(record.ToolResults);
            response = record.Evidence;
            return new(response, record);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex); record.Status = ActionStatus.Failed; record.Error = ex.Message;
            record.ToolResults = capture?.Snapshot() ?? [];
            record.Evidence = "Polecenie zakończone błędem. Sprawdź ukończone kroki przed ponowieniem." + FormatProof(record.ToolResults);
            response = "Nie udało się wykonać polecenia: " + ex.Message;
            return new(response, record);
        }
        finally
        {
            tickerStop.Cancel();
            await ticker;
            record.ElapsedMilliseconds = clock.ElapsedMilliseconds;
            if (record.Status != ActionStatus.WaitingPermission) record.FinishedAt = DateTime.Now;
            record.Phase = record.Status switch
            {
                ActionStatus.Verified => "Zakończono · dowód wykonania dostępny",
                ActionStatus.WaitingPermission => "Czeka na Twoją decyzję · nic więcej nie jest wykonywane",
                ActionStatus.Cancelled => "Przerwano · ukończone kroki nie są cofane",
                ActionStatus.Failed => "Błąd · szczegóły i dowody poniżej",
                _ => record.ToolResults.Count == 0 ? "Odpowiedź · bez potwierdzonej akcji systemowej" : "Zakończono · wynik wymaga sprawdzenia"
            };
            try
            {
                await Task.Run(() => Persist(record, response));
                record.StorageWarning = history.LastStorageError ?? memory.LastStorageError ?? "";
                if (IsStopped || record.Status == ActionStatus.Cancelled) toolbox.CancelPendingAction();
            }
            catch (Exception ex) { AppLog.Write(ex); record.StorageWarning = "Nie zapisano audytu: " + ex.Message; }
            finally
            {
                // Audit failures or UI observers must NEVER leave the execution lane locked.
                lock (gate) { active = null; source.Dispose(); }
                PublishChanged();
            }
        }
    }
    private async Task UpdateElapsedAsync(ActionRecord record, Stopwatch clock, CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(250, token).ConfigureAwait(false);
                void Update() { if (!token.IsCancellationRequested) record.ElapsedMilliseconds = clock.ElapsedMilliseconds; }
                if (dispatcher != null) dispatcher.Post(Update); else Update();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { AppLog.Write(ex); } // telemetry must not break the execution lane
    }
    private static void ApplyProof(ActionRecord task)
    {
        var results = task.ToolResults;
        task.ActionType = results.Count switch { 0 => "RESPONSE", 1 => results[0].ActionType, _ => "WORKFLOW" };
        task.Risk = results.Any(x => x.ActionType.StartsWith("MEMORY_", StringComparison.Ordinal)) ? RiskLevel.High
            : results.Any(x => x.ActionType == "CLOSE_APP") ? RiskLevel.Medium : RiskLevel.Low;
        task.Status = results.Count == 0 ? ActionStatus.Unverified
            : results.Any(x => x.Status == "FAILED") ? ActionStatus.Failed
            : results.Any(x => x.Status is "CANCELLED" or "EXPIRED" or "INTERRUPTED") ? ActionStatus.Cancelled
            : results.Any(x => x.Status is "PENDING" or "WAITING_PERMISSION") ? ActionStatus.WaitingPermission
            : results.All(x => x.Status == "VERIFIED") ? ActionStatus.Verified : ActionStatus.Unverified;
        task.Evidence = results.Count == 0 ? "Brak wyniku narzędzia. Tekst odpowiedzi nie potwierdza wykonania akcji." : FormatProof(results).Trim();
        if (task.Status == ActionStatus.Failed) task.Error = string.Join("\n", results.Where(x => x.Status == "FAILED").Select(x => x.Message));
    }
    private static string FormatProof(IReadOnlyList<ActionHistoryEntry> entries) => entries.Count == 0 ? "" :
        "\n\n" + string.Join("\n\n", entries.Select(x => $"{x.ActionId} [{x.Status}]\n{x.Message}\n{x.Evidence}"));
    private void Persist(ActionRecord record, string response)
    {
        if (record.Status == ActionStatus.WaitingPermission)
        { history.AddPending(record.ActionId, "REQUEST", AuditText(record.UserRequest), record.Evidence); return; }
        bool ephemeral = memory.IsEphemeral;
        var result = record.Status switch
        {
            // A private session must not persist the command text or the reply text anywhere, including the audit file.
            ActionStatus.Verified => ActionExecutionResult.VerifiedSuccess(ephemeral ? "[treść niezapisana]" : response, record.Evidence),
            ActionStatus.Cancelled => ActionExecutionResult.Cancelled(record.Evidence),
            ActionStatus.Failed => ActionExecutionResult.Failure(ephemeral ? "[treść niezapisana]" : record.Error, record.Evidence),
            _ => ActionExecutionResult.UnverifiedSuccess(ephemeral ? "[treść niezapisana]" : response, record.Evidence)
        };
        history.AddResult(record.ActionId, "REQUEST", AuditText(record.UserRequest), result, record.ElapsedMilliseconds);
    }
    private string AuditText(string text) => memory.IsEphemeral ? "[rozmowa prywatna lub zapis wyłączony — treść niezapisana]" : text;
    private void UpdateWaitingRequests(IReadOnlyList<ActionHistoryEntry> proof)
    {
        ActionRecord[] waiting;
        lock (gate) waiting = tracked.Where(x => x.Status == ActionStatus.WaitingPermission).ToArray();
        foreach (var task in waiting)
        {
            if (!task.ToolResults.Any(old => proof.Any(next => next.ActionId == old.ActionId))) continue;
            task.ToolResults = task.ToolResults.Select(old => proof.FirstOrDefault(next => next.ActionId == old.ActionId) ?? old).ToArray();
            ApplyProof(task);
            if (task.Status != ActionStatus.WaitingPermission)
            { task.FinishedAt = DateTime.Now; task.Phase = "Decyzja wykonana · sprawdź dowody"; Persist(task, task.Evidence); }
        }
    }
    public void Cancel()
    {
        lock (gate) { if (active != null && currentAction != null) currentAction.Phase = "Anulowanie · oczekiwanie na zatrzymanie narzędzia"; active?.Cancel(); }
        ai.Cancel(); toolbox.CancelAllTasks(); toolbox.CancelPendingAction();
        ActionRecord[] waiting;
        lock (gate) waiting = tracked.Where(x => x.Status == ActionStatus.WaitingPermission).ToArray();
        foreach (var task in waiting)
        {
            task.Status = ActionStatus.Cancelled; task.FinishedAt = DateTime.Now;
            task.Phase = "Oczekująca zgoda anulowana"; task.Evidence = "Anulowano oczekiwanie na zgodę. Wcześniej zatwierdzone lub ukończone kroki nie są cofane — sprawdź ich historię.";
            Persist(task, task.Evidence);
        }
        PublishChanged();
    }
    private void PublishChanged()
    {
        foreach (Action observer in Changed?.GetInvocationList() ?? [])
            try { observer(); } catch (Exception ex) { AppLog.Write(ex); }
    }
    private void PublishStarted(ActionRecord action)
    {
        foreach (Action<ActionRecord> observer in ActionStarted?.GetInvocationList() ?? [])
            try { observer(action); } catch (Exception ex) { AppLog.Write(ex); }
    }
    public void EmergencyStop() { lock (gate) stopped = true; Cancel(); }
    public void Resume() { lock (gate) stopped = false; PublishChanged(); }
}
