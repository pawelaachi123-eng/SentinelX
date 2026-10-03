using System.IO;
using System.Text.Json;
using SentinelX.Models;
using SentinelX.Services.Actions;
using SentinelX.Services.AI;
using SentinelX.Services.Intent;
using SentinelX.Services.Desktop;

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
        var offlineAi = new OfflineAi();
        var engine = new ActionEngine(router, toolbox, history, memory, offlineAi);
        void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
        engine.EmergencyStop();
        var blocked = await engine.ExecuteAsync("anything");
        Check(blocked.Action == null && router.Calls == 0, "Emergency stop must prevent routing.");
        engine.Resume();
        await engine.ExecuteAsync("potwierdz", fromVoice: true);
        Check(router.Calls == 0, "Voice approval must not reach a tool.");
        var spoofed = await engine.ExecuteAsync("answer");
        Check(spoofed.Action?.Status == ActionStatus.Unverified, "A model saying VERIFIED is not proof.");
        Check(history.GetRecentEntries().Any(x => x.ActionId == spoofed.Action!.ActionId && x.Status == "UNVERIFIED"), "Request must be persisted.");
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
        bool survivingStreamObserver = false;
        bool callerStreamCallback = false;
        engine.StreamDelta += _ => throw new InvalidOperationException("Intentional stream observer fault.");
        engine.StreamDelta += _ => survivingStreamObserver = true;
        await engine.ExecuteAsync("stream", onDelta: _ => callerStreamCallback = true);
        Check(survivingStreamObserver && callerStreamCallback,
            "a failing stream observer cannot block later UI observers or the request-scoped stream callback");
        var cancelEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        offlineAi.CancelAction = () =>
        {
            cancelEntered.TrySetResult();
            cancelRelease.Task.GetAwaiter().GetResult();
        };
        Task cancelling = Task.Run(engine.Cancel);
        await cancelEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        IntentResult duringCancellation = await engine.ExecuteAsync("during cancellation");
        Check(duringCancellation.Action == null && duringCancellation.Text.Contains("anulowanie", StringComparison.OrdinalIgnoreCase),
            "a new action cannot race into the AI lane while cancellation callbacks are still running");
        cancelRelease.TrySetResult();
        await cancelling.WaitAsync(TimeSpan.FromSeconds(2));
        offlineAi.CancelAction = null;
        var afterCancellation = await engine.ExecuteAsync("after cancellation barrier");
        Check(afterCancellation.Action?.Status == ActionStatus.Unverified, "the execution lane reopens after cancellation cleanup finishes");

        var store = new AppSettingsService(Path.Combine(directory, "Settings"));
        var field = SettingsCatalog.Create(store).Single(x => x.Label == "Próg VAD");
        Check(field.Write("NaN") != null && field.Write("2") != null, "Invalid VAD values must be rejected.");
        Check(field.Write("0,35") == null, "Polish decimal separator must be accepted.");
        store.Save();
        Check(new AppSettingsService(Path.Combine(directory, "Settings")).Settings.Voice.VadThreshold == .35, "Settings must round-trip.");
        File.WriteAllText(store.SettingsPath, "broken JSON");
        var damaged = new AppSettingsService(Path.Combine(directory, "Settings"));
        Check(damaged.LastError != null, "Corrupted settings must be reported.");
        Check(Directory.GetFiles(Path.Combine(directory, "Settings"), "settings.json.corrupt-*").Length == 1,
            "the original corrupt settings file must be preserved instead of overwritten");

        string recoveryDirectory = Path.Combine(directory, "settings-recovery");
        var goodSettings = new AppSettingsService(recoveryDirectory);
        goodSettings.Settings.Voice.VadThreshold = .25;
        goodSettings.Save();
        goodSettings.Settings.Voice.VadThreshold = .75;
        goodSettings.Save();
        File.WriteAllText(goodSettings.SettingsPath, "{ damaged primary");
        var recoveredSettings = new AppSettingsService(recoveryDirectory);
        Check(recoveredSettings.Settings.Voice.VadThreshold == .25 && recoveredSettings.LastError?.Contains("Przywrócono") == true,
            "a valid last-good backup must recover settings after primary JSON corruption");
        Check(File.ReadAllText(recoveredSettings.SettingsPath).Contains("0.25") && File.Exists(recoveredSettings.SettingsPath + ".backup"),
            "backup recovery must repair the primary while preserving the usable backup");

        string observerDirectory = Path.Combine(directory, "settings-observers");
        var observedSettings = new AppSettingsService(observerDirectory);
        int survivingObservers = 0;
        observedSettings.Changed += () => throw new InvalidOperationException("Intentional settings observer fault.");
        observedSettings.Changed += () => survivingObservers++;
        observedSettings.Settings.Voice.VadThreshold = .4;
        observedSettings.Save();
        Check(survivingObservers == 1 && observedSettings.LastError == null
            && new AppSettingsService(observerDirectory).Settings.Voice.VadThreshold == .4,
            "a failing settings observer cannot turn a durable save into a reported failure");
        string settingsImport = Path.Combine(directory, "settings-import.json");
        File.WriteAllText(settingsImport, "{\"Voice\":{\"VadThreshold\":0.6}}");
        Check(observedSettings.Import(settingsImport) && observedSettings.Settings.Voice.VadThreshold == .6,
            "import remains successful and durable when a Changed observer fails");
        Check(!Directory.GetFiles(observerDirectory, "*.tmp").Any(), "atomic settings writes leave no temporary files behind");

        Check(OverlayPositionPolicy.GetNearestCorner(10, 10, 100, 60, 0, 0, 1000, 800) == "Lewy górny"
            && OverlayPositionPolicy.GetNearestCorner(900, 10, 100, 60, 0, 0, 1000, 800) == "Prawy górny"
            && OverlayPositionPolicy.GetNearestCorner(10, 700, 100, 60, 0, 0, 1000, 800) == "Lewy dolny"
            && OverlayPositionPolicy.GetNearestCorner(900, 700, 100, 60, 0, 0, 1000, 800) == "Prawy dolny",
            "dragging the overlay into each screen quadrant selects the corresponding corner preset");
        string overlaySettingsDirectory = Path.Combine(directory, "overlay-settings");
        var overlaySettings = new AppSettingsService(overlaySettingsDirectory);
        overlaySettings.Settings.Ui.OverlayPosition = OverlayPositionPolicy.GetNearestCorner(900, 700, 100, 60, 0, 0, 1000, 800);
        overlaySettings.Save();
        Check(new AppSettingsService(overlaySettingsDirectory).Settings.Ui.OverlayPosition == "Prawy dolny",
            "the selected overlay corner persists across settings reload");

        AppLog.Write("Settings", "Info", "settings logger category canary token=never-persist-this");
        using (JsonDocument logRecord = JsonDocument.Parse(File.ReadLines(AppLog.LogPath).Last()))
        {
            Check(logRecord.RootElement.GetProperty("Category").GetString() == "Settings",
                "structured logs preserve the settings category");
            Check(logRecord.RootElement.GetProperty("Message").GetString()?.Contains("[REDACTED]") == true,
                "structured logs redact labelled credentials even for new categories");
        }

        var files = new FileWorkspaceService(directory, directory, history);
        Check((await files.ProcessAsync("utwórz plik original.txt: Zażółć gęślą jaźń", CancellationToken.None))!.Contains("VERIFIED"), "Create + verify UTF-8 file.");
        Check((await files.ProcessAsync("Skopiuj ten plik jako copy.txt", CancellationToken.None))!.Contains("VERIFIED"), "Copy + verify file.");
        Check(File.ReadAllText(Path.Combine(directory, "CreatedFiles", "copy.txt")) == "Zażółć gęślą jaźń", "Copy must preserve exact text.");
        Check((await files.ProcessAsync("przenieś ten plik jako moved.txt", CancellationToken.None))!.Contains("VERIFIED"), "Move + verify file.");
        Check(!File.Exists(Path.Combine(directory, "CreatedFiles", "copy.txt")), "Move must remove the old name.");
        Check((await files.ProcessAsync("skopiuj ten plik jako original.txt", CancellationToken.None))!.Contains("FAILED"), "Copy must never overwrite.");
        Check((await files.ProcessAsync("przenieś ten plik jako ../escape.txt", CancellationToken.None))!.Contains("FAILED"), "Move must reject traversal.");
        Check((await files.ProcessAsync("znajdź plik moved", CancellationToken.None))!.Contains("moved.txt"), "Search only in the workspace.");
        File.WriteAllText(Path.Combine(directory, "backend-tests.txt"), "PASS: STOP, voice permission, proof spoofing, isolated stream observers, bounded history, concurrency, cancellation/resume, typed validation, atomic settings recovery/import, overlay-corner persistence, structured log redaction, safe file operations\n");
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
        public Task<string> ProcessAsync(string input, CancellationToken token, Action<string>? onDelta)
        {
            if (input == "stream")
            {
                token.ThrowIfCancellationRequested();
                onDelta?.Invoke("stream chunk");
                return Task.FromResult("stream complete");
            }
            return ProcessAsync(input, token);
        }
    }
    private sealed class OfflineAi : IAiService
    {
        public string RoutingReason => "test";
        public Action? CancelAction { get; set; }
        public void Cancel() => CancelAction?.Invoke();
        public Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string> SelectModelAsync(string model, CancellationToken token = default) => Task.FromResult(model);
        public Task<string> AskAsync(string input, string context, CancellationToken token) => Task.FromResult(input);
    }
}
