using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using SentinelX.Core;
using SentinelX.Models;
using SentinelX.Services.Actions;
using SentinelX.Services.AI;
using SentinelX.Services.History;
using SentinelX.Services.Intent;
using SentinelX.Services.Monitoring;
namespace SentinelX.Tests;

internal static class ReleaseRegression
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    public static async Task RunAsync(string root)
    {
        Directory.CreateDirectory(root);
        var history = new ActionHistoryService(root);
        var memory = new ConversationMemoryService(Path.Combine(root, "Memory"));
        var permissions = new PermissionCenterService();
        var exporter = new HistoryExportService(history, root);
        var toolbox = new SentinelToolboxService(history: history, permissions: permissions, memory: memory, historyExport: exporter);
        using var monitor = new SystemMonitor();
        using var localAi = new LocalAiService(new GamingModeService());
        var oldRouter = new CommandRouter(monitor, new SystemInfoService(), localAi, memory);
        var reads = new ReadOnlyCommandService(monitor, history);
        var router = new IntentRouter(toolbox, new FileWorkspaceService(root, root, history), oldRouter, reads);
        var engine = new ActionEngine(router, toolbox, history, memory, new NoAi());
        memory.AddNote("prywatne wspomnienie testowe");
        var pending = await engine.ExecuteAsync("usuń wszystkie wspomnienia", fromVoice: true);
        Check(pending.Action?.Status == ActionStatus.WaitingPermission && pending.Action.Risk == RiskLevel.High && memory.NoteCount == 1, "Memory mutation must only request HIGH permission.");
        foreach (var command in new[] { "potwierdź usunięcie wspomnień", "Hej Sentinel, potwierdź usunięcie wspomnień!", "confirm", "potwierdź akcję" })
            Check((await engine.ExecuteAsync(command, fromVoice: true)).Action == null && memory.NoteCount == 1, "Voice must not approve memory deletion.");
        var direct = await permissions.ConfirmAsync();
        Check(direct.ApprovalRejected && memory.NoteCount == 1 && permissions.HasPendingAction, "No execution context must fail closed and preserve permission.");
        engine.EmergencyStop(); engine.Resume();
        await engine.ExecuteAsync("potwierdź usunięcie wspomnień");
        Check(memory.NoteCount == 1 && !permissions.HasPendingAction, "STOP/resume must not revive a pending memory deletion.");
        await oldRouter.ProcessAsync("usuń wszystkie wspomnienia", default);
        await oldRouter.ProcessAsync("potwierdź usunięcie wspomnień", default);
        Check(memory.NoteCount == 1, "Legacy low-level router must not bypass the permission service.");
        await engine.ExecuteAsync("usuń wszystkie wspomnienia");
        var approved = await engine.ExecuteAsync("potwierdź usunięcie wspomnień");
        Check(approved.Action?.Status == ActionStatus.Verified && memory.NoteCount == 0, "Keyboard approval must execute and verify persisted memory state.");
        Check(new ConversationMemoryService(Path.Combine(root, "Memory")).NoteCount == 0, "Deletion must survive reload.");
        Check(approved.Action!.ToolResults.Any(x => x.ActionType == "MEMORY_CLEAR_ALL" && x.Evidence.Contains("SHA-256")), "Memory deletion needs read-back proof.");
        await engine.ExecuteAsync("potwierdź");
        Check(!permissions.HasPendingAction, "Approval must be single-use.");
        await engine.ExecuteAsync("zamknij notatnik"); // request only, never close a real process
        await engine.ExecuteAsync("potwierdź usunięcie wspomnień");
        Check(permissions.HasPendingAction, "A memory-specific approval must not approve an unrelated close action.");
        engine.Cancel();
        memory.AddNote("unikalny fragment do zapomnienia");
        await engine.ExecuteAsync("zapomnij unikalny fragment");
        Check(memory.NoteCount == 1, "Forget must wait for consent.");
        await engine.ExecuteAsync("potwierdź");
        Check(memory.NoteCount == 0, "Approved scoped forget must work.");

        int executions = 0;
        PendingPermissionAction Fake(DateTime? created = null) => new()
        { ActionId = Guid.NewGuid().ToString("N"), ActionType = "TEST", CreatedAt = created ?? DateTime.Now,
            CancellableExecutor = _ => { executions++; return Task.FromResult(ActionExecutionResult.VerifiedSuccess("ok", "fake executor")); } };
        permissions.TryRequest(Fake(DateTime.Now.AddMinutes(-11)), out _);
        using (ApprovalContext.Begin("potwierdź", false)) await permissions.ConfirmAsync();
        Check(executions == 0 && !permissions.HasPendingAction, "Expired permission must never execute.");
        permissions.TryRequest(Fake(), out _);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<PermissionExecutionResult> late;
        using (ApprovalContext.Begin("potwierdź", false))
            late = Task.Run(async () => { await release.Task; return await permissions.ConfirmAsync(); });
        release.SetResult();
        Check((await late).ApprovalRejected && executions == 0, "Detached work cannot reuse a closed human-approval capability.");
        permissions.Cancel();

        foreach (var item in ReadOnlyIntentCatalog.Aliases)
        {
            Check(ReadOnlyIntentCatalog.TryResolve("Sentinel, " + item.Key + "?", out var intent) && intent == item.Value, "Wake/punctuation alias regression: " + item.Key);
            Check(ReadOnlyIntentCatalog.TryResolve("proszę " + item.Key, out intent) && intent == item.Value, "Polite alias regression: " + item.Key);
        }
        foreach (var text in new[] { "nie pokazuj ram", "nie zamykaj aplikacji", "ile mam ramu i usuń wspomnienia", "co oznacza RAM?" })
            Check(!ReadOnlyIntentCatalog.TryResolve(text, out _), "Whole-command classifier must not consume compound/negative/unrelated text.");
        foreach (var text in new[] { "ile mam ramu?", "wolna pamięć", "procent RAM", "czas pracy komputera", "która godzina", "dzisiejsza data" })
            Check((await engine.ExecuteAsync(text)).Action?.Status == ActionStatus.Verified, "Read-only alias must produce actual proof: " + text);

        history.AddResult("CSV-ATTACK", "TEST", " =HYPERLINK(\"https://invalid\")", ActionExecutionResult.VerifiedSuccess("Zażółć, \"gęślą\"\nwiersz", "dowód"));
        var jsonExport = await engine.ExecuteAsync("eksportuj historię json");
        Check(jsonExport.Action?.Status == ActionStatus.Verified, "JSON export must have verified tool evidence.");
        string json = Directory.GetFiles(Path.Combine(root, "Exports"), "*.json").Single();
        using (var doc = JsonDocument.Parse(File.ReadAllBytes(json)))
        {
            Check(doc.RootElement.GetProperty("schemaVersion").GetInt32() == 1, "Export schema version missing.");
            Check(doc.RootElement.GetProperty("actions").EnumerateArray().Any(x => x.GetProperty("ActionId").GetString() == "CSV-ATTACK"), "Export lost a fixture row.");
        }
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(json)));
        Check(jsonExport.Action!.ToolResults.Any(x => x.Evidence.Contains(hash)), "Export evidence must match actual on-disk bytes.");
        Check((await engine.ExecuteAsync("eksportuj historię csv")).Action?.Status == ActionStatus.Verified, "CSV export should verify.");
        string csv = File.ReadAllText(Directory.GetFiles(Path.Combine(root, "Exports"), "*.csv").Single());
        Check(csv.Contains("\"' =HYPERLINK") && csv.Contains("\"\"gęślą\"\""), "CSV must escape quotes and neutralize spreadsheet formulas.");
        foreach (var value in new[] { "=1+1", " +123", "-cmd", "@sum(1)", "\t=cmd", "\n=cmd" })
            Check(HistoryExportService.CsvCell(value).StartsWith("\"'"), "Unsafe spreadsheet cell: " + value);
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel(); bool caught = false;
            try { await exporter.ExportAsync("json", cancelled.Token); } catch (OperationCanceledException) { caught = true; }
            Check(caught && Directory.GetFiles(Path.Combine(root, "Exports")).Length == 2, "Cancelled export must leave no partial files.");
        }
        engine.EmergencyStop();
        Check((await engine.ExecuteAsync("eksportuj historię json")).Action == null, "Export must honor STOP."); engine.Resume();
        File.AppendAllText(Path.Combine(root, "History", "actions.jsonl"), "\ninvalid-json\n");
        var broken = await exporter.ExportAsync("json");
        Check(!broken.Success && history.LastReadError != null, "Corrupt history must not silently produce a verified export.");
        File.WriteAllText(Path.Combine(root, "release-tests.txt"), $"PASS: scoped approvals, voice memory deletion prevention, STOP, persistence proof, unrelated/expired/replayed approval, {ReadOnlyIntentCatalog.Aliases.Count} aliases, negations, actual local measurements, JSON/CSV exports, SHA-256, CSV formula defense, corrupt history, cancellation.\n");
    }
    private sealed class NoAi : IAiService
    {
        public string RoutingReason => "No model used in tests";
        public void Cancel() { }
        public Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string> SelectModelAsync(string model, CancellationToken token = default) => Task.FromResult(model);
        public Task<string> AskAsync(string input, string context, CancellationToken token) => throw new InvalidOperationException("Unexpected AI call");
    }
}
