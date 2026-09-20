using System.IO;
using SentinelX.Models;
using SentinelX.Services.Actions;
using SentinelX.Services.AI;
using SentinelX.Services.Intent;

namespace SentinelX.Tests;

internal static class BackendRegression
{
    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var history = new ActionHistoryService(directory);
        var memory = new ConversationMemoryService(Path.Combine(directory, "Memory"));
        var toolbox = new SentinelToolboxService(history: history);
        var router = new ControlledRouter();
        var engine = new ActionEngine(router, toolbox, history, memory, new OfflineAi());
        void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
        engine.EmergencyStop();
        var blocked = await engine.ExecuteAsync("anything");
        Check(blocked.Action == null && router.Calls == 0, "Emergency stop must prevent routing.");
        engine.Resume();
        await engine.ExecuteAsync("potwierdz", fromVoice: true);
        Check(router.Calls == 0, "Voice approval must not reach a tool.");
        var spoofed = await engine.ExecuteAsync("answer");
        Check(spoofed.Action?.Status == ActionStatus.Unverified, "A model saying VERIFIED is not proof.");
        Check(history.GetRecentEntries().Any(x => x.ActionId == spoofed.Action.ActionId && x.Status == "UNVERIFIED"), "Request must be persisted.");
        router.Wait = true;
        var first = engine.ExecuteAsync("wait");
        await router.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = await engine.ExecuteAsync("second");
        Check(second.Action == null, "Concurrent requests must not execute.");
        engine.EmergencyStop();
        var cancelled = await first.WaitAsync(TimeSpan.FromSeconds(5));
        Check(cancelled.Action?.Status == ActionStatus.Cancelled && engine.IsStopped && !engine.IsBusy, "Stop must cancel and remain latched.");
        engine.Resume(); router.Wait = false;
        var resumed = await engine.ExecuteAsync("again");
        Check(resumed.Action?.Status == ActionStatus.Unverified, "Engine must recover after cancellation.");
        var store = new AppSettingsService(Path.Combine(directory, "Settings"));
        var field = SettingsCatalog.Create(store).Single(x => x.Label == "Próg VAD");
        Check(field.Write("NaN") != null && field.Write("2") != null, "Invalid VAD values must be rejected.");
        Check(field.Write("0,35") == null, "Polish decimal separator must be accepted.");
        store.Save();
        Check(new AppSettingsService(Path.Combine(directory, "Settings")).Settings.Voice.VadThreshold == .35, "Settings must round-trip.");
        File.WriteAllText(store.SettingsPath, "broken JSON");
        var damaged = new AppSettingsService(Path.Combine(directory, "Settings"));
        Check(damaged.LastError != null, "Corrupted settings must be reported.");
        var files = new FileWorkspaceService(directory, directory, history);
        Check((await files.ProcessAsync("utwórz plik original.txt: Zażółć gęślą jaźń", CancellationToken.None))!.Contains("VERIFIED"), "Create + verify UTF-8 file.");
        Check((await files.ProcessAsync("skopiuj ten plik jako copy.txt", CancellationToken.None))!.Contains("VERIFIED"), "Copy + verify file.");
        Check(File.ReadAllText(Path.Combine(directory, "CreatedFiles", "copy.txt")) == "Zażółć gęślą jaźń", "Copy must preserve exact text.");
        Check((await files.ProcessAsync("przenieś ten plik jako moved.txt", CancellationToken.None))!.Contains("VERIFIED"), "Move + verify file.");
        Check(!File.Exists(Path.Combine(directory, "CreatedFiles", "copy.txt")), "Move must remove the old name.");
        Check((await files.ProcessAsync("skopiuj ten plik jako original.txt", CancellationToken.None))!.Contains("FAILED"), "Copy must never overwrite.");
        Check((await files.ProcessAsync("przenieś ten plik jako ../escape.txt", CancellationToken.None))!.Contains("FAILED"), "Move must reject traversal.");
        Check((await files.ProcessAsync("znajdź plik moved", CancellationToken.None))!.Contains("moved.txt"), "Search only in the workspace.");
        File.WriteAllText(Path.Combine(directory, "backend-tests.txt"), "PASS: STOP, voice permission, proof spoofing, history, concurrency, cancellation, resume, validation, persistence, corrupt JSON, file copy/move/search/safety\n");
    }
    private sealed class ControlledRouter : IIntentRouter
    {
        public int Calls;
        public bool Wait;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<string> ProcessAsync(string input, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            if (Wait) { Entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
            return "VERIFIED — this is only model text, not a tool result";
        }
    }
    private sealed class OfflineAi : IAiService
    {
        public string RoutingReason => "test";
        public void Cancel() { }
        public Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string> SelectModelAsync(string model, CancellationToken token = default) => Task.FromResult(model);
        public Task<string> AskAsync(string input, string context, CancellationToken token) => Task.FromResult(input);
    }
}
