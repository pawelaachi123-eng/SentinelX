using SentinelX.Core;
using SentinelX.Models;
using SentinelX.Services.Actions;
using SentinelX.Services.AI;
using SentinelX.Services.History;
using SentinelX.Services.Intent;
using SentinelX.Services.Readiness;
using SentinelX.Services.Settings;
using SentinelX.Services.Voice;
using SentinelX.ViewModels;
using System.IO;
namespace SentinelX.Tests;

internal static class ProductRegression
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var history = new ActionHistoryService(directory);
        var memory = new ConversationMemoryService(Path.Combine(directory, "memory"));
        var toolbox = new SentinelToolboxService(history: history);
        var router = new ScriptedRouter();
        var engine = new ActionEngine(router, toolbox, history, memory, new OfflineAi());
        string? initialId = null;
        engine.ActionStarted += action => initialId = action.ActionId;
        // A faulty UI observer cannot poison the execution lane or mask another observer.
        engine.Changed += () => throw new InvalidOperationException("Deliberate observer fault in test");
        foreach (var text in new[] { "potwierdź!", "Sentinel, potwierdź akcję.", "Hej Sentinel X, confirm?" })
            Check((await engine.ExecuteAsync(text, fromVoice: true)).Action == null, "Wake/punctuation must not bypass voice approval.");
        Check(router.Calls == 0, "Approval bypass reached router.");

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        router.Handler = async (_, token) => { entered.TrySetResult(); await release.Task.WaitAsync(token); return "model response VERIFIED"; };
        var request = engine.ExecuteAsync("unrelated-proof");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Same service, same time, but a different async execution context. This must not be evidence.
        history.AddResult("UNRELATED", "TEST", "other request", ActionExecutionResult.VerifiedSuccess("unrelated", "not this request"));
        release.TrySetResult();
        var unrelated = await request;
        Check(unrelated.Action?.Status == ActionStatus.Unverified && unrelated.Action.ToolResults.Count == 0, "Cross-request proof contamination.");
        Check(!engine.IsBusy, "Observer fault left the lane locked.");

        router.Handler = (input, _) =>
        {
            history.AddResult("STEP-A", "TEST", input, ActionExecutionResult.Failure("first step failed"));
            history.AddResult("STEP-B", "TEST", input, ActionExecutionResult.VerifiedSuccess("second succeeded", "actual evidence"));
            return Task.FromResult("both done");
        };
        var mixed = await engine.ExecuteAsync("mixed workflow");
        Check(mixed.Action?.Status == ActionStatus.Failed && mixed.Action.ToolResults.Count == 2, "Last success must not hide earlier failure.");
        Check(mixed.Action!.ActionId == initialId && mixed.Action.ActionId != "STEP-B", "Request IDs must remain stable.");
        Check(mixed.Action.ToolResults.All(x => x.RequestId == mixed.Action.ActionId), "Tool evidence must carry its request correlation.");
        Check(history.GetRecentEntries().Any(x => x.ActionId == mixed.Action.ActionId && x.Status == "FAILED"), "Parent request audit must match the aggregate outcome.");

        var cancellationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        router.Handler = async (input, token) =>
        {
            history.AddResult("BEFORE-CANCEL", "TEST", input, ActionExecutionResult.VerifiedSuccess("already completed", "actual mutation evidence"));
            cancellationEntered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return "unreachable";
        };
        var work = engine.ExecuteAsync("cancel after partial work");
        await cancellationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Telemetry ticks every 250 ms through the UI dispatcher; on loaded CI runners the first tick
        // can be marshalled late, so poll instead of asserting at a single wall-clock instant.
        bool elapsedAdvanced = false;
        for (int wait = 0; wait < 20 && !elapsedAdvanced; wait++)
        {
            await Task.Delay(100);
            elapsedAdvanced = engine.CurrentAction!.ElapsedMilliseconds > 0;
        }
        Check(elapsedAdvanced, "Elapsed telemetry must advance during work.");
        engine.EmergencyStop(); var cancelled = await work.WaitAsync(TimeSpan.FromSeconds(5));
        Check(cancelled.Action?.Status == ActionStatus.Cancelled && cancelled.Action.ToolResults.Single().ActionId == "BEFORE-CANCEL", "Cancellation must retain completed effects.");
        Check(engine.IsStopped && !engine.IsBusy, "Stop must remain latched after partial completion.");
        engine.Resume();

        // An uninterruptible tool can enqueue permission between Cancel() and its return.
        // Cancellation must also clear such a late permission, not only an emergency stop.
        var raceEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var raceRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        router.Handler = async (_, _) =>
        {
            raceEntered.TrySetResult(); await raceRelease.Task;
            return (await toolbox.ProcessAsync("zamknij notatnik", CancellationToken.None)).Response;
        };
        var race = engine.ExecuteAsync("late permission cancellation race");
        await raceEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        engine.Cancel(); raceRelease.TrySetResult();
        var raceResult = await race.WaitAsync(TimeSpan.FromSeconds(5));
        Check(raceResult.Action?.Status == ActionStatus.Cancelled && !engine.HasPendingPermission, "Cancelled request must not leave a late approval armed.");

        // The audit directory becomes unwritable by replacing it with a regular file.
        string auditPath = Path.Combine(directory, "History"), backupPath = Path.Combine(directory, "History.saved");
        Directory.Move(auditPath, backupPath); File.WriteAllText(auditPath, "blocked directory");
        try
        {
            router.Handler = (_, _) => Task.FromResult("response");
            var storageFailure = await engine.ExecuteAsync("audit storage failure");
            Check(!engine.IsBusy && storageFailure.Action!.StorageWarning.Length > 0, "Audit failure must be visible and release the execution lane.");
        }
        finally { File.Delete(auditPath); Directory.Move(backupPath, auditPath); }
        var recovered = await engine.ExecuteAsync("storage recovered");
        Check(recovered.Action!.StorageWarning.Length == 0 && !engine.IsBusy, "Engine must recover after audit storage is repaired.");

        using (var capture = ActionEvidenceCapture.Begin("SNAPSHOT"))
        {
            history.AddResult("SNAPSHOT-PROOF", "TEST", "snapshot", ActionExecutionResult.VerifiedSuccess("OK", "original"));
            var copy = capture.Snapshot(); copy[0].Evidence = "changed outside capture";
            Check(capture.Snapshot()[0].Evidence == "original", "Captured proof must be defensive copied.");
        }
        using (var bounded = ActionEvidenceCapture.Begin("LIMIT"))
        {
            for (int i = 0; i < 260; i++) ActionEvidenceCapture.Record(new() { ActionId = "L" + i, Status = "VERIFIED" });
            Check(bounded.Snapshot().Any(x => x.Status == "UNVERIFIED"), "Overflow must prevent false verification.");
        }
        var lateStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ActionEvidenceCapture late;
        Task detached;
        using (late = ActionEvidenceCapture.Begin("LATE"))
        {
            detached = Task.Run(async () => { lateStarted.TrySetResult(); await lateRelease.Task; history.AddResult("DETACHED", "TEST", "late", ActionExecutionResult.VerifiedSuccess("late", "late")); });
            await lateStarted.Task;
        }
        lateRelease.TrySetResult(); await detached;
        Check(late.Snapshot().Count == 0, "Detached work must not append to a closed capture.");

        var palette = new CommandPaletteViewModel();
        PaletteEntry? chosen = null; palette.Chosen += entry => chosen = entry;
        palette.Open(); palette.Query = "pamięć";
        Check(palette.Results.Count > 0 && palette.Results.Any(x => x.CommandText != null), "Polish search must find memory commands.");
        palette.Query = "ustawienia"; palette.ChooseCommand.Execute(null);
        Check(chosen?.PageKey == "settings" && !palette.IsOpen, "Palette navigation failed.");
        palette.Open(); palette.Query = "moja całkiem własna komenda";
        Check(palette.HasNoResults, "Custom text must use the no-results path.");
        palette.ChooseCommand.Execute(null);
        Check(chosen?.CommandText == "moja całkiem własna komenda", "Palette should stage custom input, not discard it.");
        palette.Open(); palette.Query = "model";
        palette.PreviousCommand.Execute(null); Check(palette.SelectedEntry == palette.Results.Last(), "Arrow navigation must wrap.");

        var settings = new SettingsService(new AppSettingsService(Path.Combine(directory, "settings")));
        var voice = new ProbeVoice();
        var readiness = new ReadinessService(settings, new HistoryService(history, memory), voice, new OfflineAi());
        var checks = await readiness.CheckAsync(CancellationToken.None);
        Check(checks.Count == 4 && checks.Single(x => x.Key == "ollama").State == ReadinessState.NeedsSetup, "Offline Ollama must be actionable, not a false Ready.");
        Check(voice.StartCalls == 0 && voice.SpeakCalls == 0, "Readiness must never capture or speak.");
        var readyVm = new ReadinessViewModel(readiness);
        await readyVm.RefreshCommand.ExecuteAsync(null);
        Check(readyVm.Checks.Count == 4, "Readiness results must reach the ViewModel.");
        using var cancelledProbe = new CancellationTokenSource(); cancelledProbe.Cancel();
        bool probeCancelled = false;
        try { await readiness.CheckAsync(cancelledProbe.Token); } catch (OperationCanceledException) { probeCancelled = true; }
        Check(probeCancelled, "Readiness must honor cancellation.");
        File.WriteAllText(Path.Combine(directory, "product-tests.txt"), "PASS: correlated evidence, mixed outcomes, stable IDs, cancellation evidence, live elapsed, observer isolation, bounded capture, late writes, audit I/O failure/recovery, late permission cancellation race, voice approval normalization, palette, offline readiness and privacy\n");
    }
    private sealed class ScriptedRouter : IIntentRouter
    {
        public int Calls;
        public Func<string, CancellationToken, Task<string>> Handler { get; set; } = (_, _) => Task.FromResult("response");
        public Task<string> ProcessAsync(string input, CancellationToken token) { Calls++; return Handler(input, token); }
    }
    private sealed class OfflineAi : IAiService
    {
        public string RoutingReason => "offline test";
        public void Cancel() { }
        public Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken token = default) => Task.FromException<IReadOnlyList<string>>(new System.Net.Http.HttpRequestException("offline test"));
        public Task<string> SelectModelAsync(string model, CancellationToken token = default) => Task.FromResult(model);
        public Task<string> AskAsync(string input, string context, CancellationToken token) => Task.FromResult(input);
    }
    private sealed class ProbeVoice : IVoiceService
    {
        public int StartCalls, SpeakCalls;
        public VoiceState State => VoiceState.Off;
        public string Status => "test";
        public bool HasLocalModels => false;
        public event Action? Changed { add { } remove { } }
        public event Action<Services.Voice.VoiceMetrics>? MetricsUpdated { add { } remove { } }
        public event Action<string>? CommandRecognized { add { } remove { } }
        public IReadOnlyList<string> GetMicrophones() => [];
        public Task StartAsync(int device, bool downloadModels, CancellationToken token) { StartCalls++; return Task.CompletedTask; }
        public void Stop() { }
        public void Calibrate() { }
        public void Speak(string text) => SpeakCalls++;
    }
}
