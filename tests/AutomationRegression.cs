using System.IO;
using SentinelX.Services.Apps;
using SentinelX.Services.Automation;
using SentinelX.Services.Link;
using SentinelX.Services.Notifications;

namespace SentinelX.Tests;

/// <summary>Automation storage, scheduling, validation, action order, failure boundaries and cancellation use only local fakes.</summary>
internal static class AutomationRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("AutomationRegression: " + message);
    }

    private sealed class FakeNotifications : INotificationService
    {
        public List<AppNotification> PublishedItems { get; } = [];
        public event Action<AppNotification>? Published;
        public AppNotification Publish(string category, string title, string message, string severity = "info")
        {
            var item = new AppNotification(DateTimeOffset.UtcNow, severity, category, title, message);
            PublishedItems.Add(item);
            Published?.Invoke(item);
            return item;
        }
    }

    private sealed class FakeLauncher : IAppLauncherService
    {
        public string? LastTarget { get; private set; }
        public Task<ActionExecutionResult> LaunchAsync(string target, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastTarget = target;
            return Task.FromResult(ActionExecutionResult.UnverifiedSuccess("fake launch", "not started on this machine"));
        }
        public Task<ActionExecutionResult> SearchWebAsync(string query, bool youtube, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActionExecutionResult.Failure("Not used by the automation tests."));
        public Task<ActionExecutionResult> OpenFolderAsync(string folder, CancellationToken cancellationToken = default) =>
            Task.FromResult(ActionExecutionResult.Failure("Not used by the automation tests."));
    }

    private sealed class RecordingAction : IAutomationActionHandler
    {
        public AutomationActionDescriptor Descriptor { get; } = new("record", "Test action", "Tests", "Only a deterministic test action.", "None");
        public List<string> Calls { get; } = [];
        public TaskCompletionSource<bool> SlowStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ServiceCancelStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool TryValidate(string? parameter, out string normalized, out string error)
        {
            normalized = (parameter ?? "").Trim();
            if (normalized.Length is > 0 and <= 100)
            { error = ""; return true; }
            normalized = "";
            error = "A non-empty test parameter is required.";
            return false;
        }

        public async Task<AutomationActionResult> ExecuteAsync(string normalizedParameter, CancellationToken cancellationToken)
        {
            Calls.Add(normalizedParameter);
            if (normalizedParameter == "slow")
            {
                SlowStarted.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            if (normalizedParameter == "cancel-by-service")
            {
                ServiceCancelStarted.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            if (normalizedParameter == "fail") return new(false, "FAILED", "Expected test failure.", "fake evidence");
            return new(true, "VERIFIED", "Recorded.", "recorded by a fake");
        }
    }

    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute)
    {
        var value = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(value, TimeZoneInfo.Local.GetUtcOffset(value));
    }

    public static async Task RunAsync(string directory)
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
        Directory.CreateDirectory(directory);

        var alertFeed = new AlertFeed();
        var sharedNotifications = new AppNotificationService(alertFeed);
        AppNotification? observedNotification = null;
        sharedNotifications.Published += item => observedNotification = item;
        string injectedControls = "Line one" + (char)13 + (char)10 + "Line two" + (char)0;
        AppNotification sanitized = sharedNotifications.Publish("Desktop" + (char)13 + (char)10 + "Injected", "Title" + (char)13 + (char)10 + "Injected", injectedControls, "warn");
        Check(observedNotification == sanitized && sanitized.Severity == "warn", "the shared notification service delivers a sanitized warning to its subscribers");
        Check(!sanitized.Category.Any(char.IsControl) && !sanitized.Title.Any(char.IsControl) && !sanitized.Message.Any(char.IsControl), "notification fields cannot inject control characters or extra lines");
        LinkAlert pairedAlert = alertFeed.After(0).Single();
        Check(pairedAlert.Level == "warn" && pairedAlert.Title == sanitized.Title && pairedAlert.Text == sanitized.Message,
            "the paired-phone alert feed receives the same bounded notification content");

        var notifications = new FakeNotifications();
        var fakeLauncher = new FakeLauncher();
        var launchHandler = new LaunchApplicationAutomationAction(fakeLauncher);
        var urlHandler = new OpenUrlAutomationAction(fakeLauncher);
        var notificationHandler = new ShowNotificationAutomationAction(notifications);
        var actionCatalog = new AutomationActionRegistry([launchHandler, urlHandler, notificationHandler]);
        Check(actionCatalog.GetAvailableActions().Count == 3, "the app exposes only its three allowlisted automation actions");
        Check(!launchHandler.TryValidate("cmd /c calc", out _, out _), "application launches reject shell-shaped input");
        Check(launchHandler.TryValidate("Brave", out string appTarget, out _) && appTarget == "brave", "application launches canonicalize a known app name");
        await launchHandler.ExecuteAsync(appTarget, CancellationToken.None);
        Check(fakeLauncher.LastTarget == "brave", "the app launcher receives only the validated target");
        Check(!urlHandler.TryValidate("javascript:alert(1)", out _, out _), "URL action rejects script protocols");
        Check(!urlHandler.TryValidate("https://user:pass@example.test", out _, out _), "URL action rejects embedded credentials");
        Check(urlHandler.TryValidate("https://example.test/path", out string safeUrl, out _), "URL action accepts a plain HTTPS address");
        await urlHandler.ExecuteAsync(safeUrl, CancellationToken.None);
        Check(fakeLauncher.LastTarget == safeUrl, "the validated URL reaches only the app launcher");
        Check(!notificationHandler.TryValidate("\n", out _, out _), "notification action rejects control-only content");
        Check(notificationHandler.TryValidate("Backup completed", out string note, out _), "notification action accepts short user-authored text");
        await notificationHandler.ExecuteAsync(note, CancellationToken.None);
        Check(notifications.PublishedItems.Count == 1, "notification actions publish through the shared notification service");

        notifications.PublishedItems.Clear();
        var handler = new RecordingAction();
        var registry = new AutomationActionRegistry([handler]);
        using var service = new AutomationService(registry, notifications, directory);

        Check(!service.TryValidateAction(new("powershell", "Remove-Item *"), out _, out _), "unknown and shell-shaped action IDs are refused");
        var invalidTime = service.SaveRule(new AutomationRule
        {
            Name = "Invalid schedule", Trigger = AutomationTriggerKind.DailySchedule, ScheduleTime = "25:90",
            Actions = [new("record", "valid")]
        }, out string invalidError);
        Check(invalidTime == null && invalidError.Contains("HH:mm"), "invalid schedule time is refused before persistence");
        var invalidAction = service.SaveRule(new AutomationRule
        {
            Name = "Invalid action", Actions = [new("powershell", "echo unsafe")]
        }, out _);
        Check(invalidAction == null, "unknown action cannot be saved in an automation");

        AutomationRule manual = service.SaveRule(new AutomationRule
        {
            Name = "Manual sequence", Trigger = AutomationTriggerKind.Manual, Enabled = true,
            Actions = [new("record", "first"), new("record", "second")]
        }, out string error) ?? throw new InvalidOperationException(error);
        AutomationRunOutcome result = await service.RunNowAsync(manual.Id);
        Check(result.Started && result.Status == "SUCCESS" && result.Record?.CompletedActions == 2, "manual execution records a completed two-step run");
        Check(handler.Calls.TakeLast(2).SequenceEqual(["first", "second"]), "actions execute in the exact order the user selected");
        Check(notifications.PublishedItems.Count == 1, "one meaningful completion notification is published");

        AutomationRule daily = service.SaveRule(new AutomationRule
        {
            Name = "Daily check", Trigger = AutomationTriggerKind.DailySchedule, ScheduleTime = "09:30", Enabled = true,
            Actions = [new("record", "daily")]
        }, out error) ?? throw new InvalidOperationException(error);
        Check(await service.RunDueSchedulesAsync(Local(2026, 10, 2, 9, 29)) == 0, "daily schedule does not run before its local time");
        Check(await service.RunDueSchedulesAsync(Local(2026, 10, 2, 9, 31)) == 1, "daily schedule runs once after its due time");
        Check(await service.RunDueSchedulesAsync(Local(2026, 10, 2, 10, 0)) == 0, "daily schedule cannot repeat during the same local date");
        Check(await service.RunDueSchedulesAsync(Local(2026, 10, 3, 9, 31)) == 1, "daily schedule runs again on the next date");

        AutomationRule failure = service.SaveRule(new AutomationRule
        {
            Name = "Stops on failure", Trigger = AutomationTriggerKind.Manual, Enabled = false,
            Actions = [new("record", "before"), new("record", "fail"), new("record", "after")]
        }, out error) ?? throw new InvalidOperationException(error);
        int beforeFailure = handler.Calls.Count;
        AutomationRunOutcome failed = await service.RunNowAsync(failure.Id);
        Check(failed.Started && failed.Status == "PARTIAL" && failed.Record?.CompletedActions == 1, "a failed step reports partial completion honestly");
        Check(handler.Calls.Skip(beforeFailure).SequenceEqual(["before", "fail"]), "later actions do not run after an earlier action fails");
        Check(service.GetRules().Single(x => x.Id == failure.Id).Enabled == false, "manual execution is explicit and does not silently enable a disabled trigger");

        AutomationRule slow = service.SaveRule(new AutomationRule
        {
            Name = "Cancelable", Actions = [new("record", "slow")]
        }, out error) ?? throw new InvalidOperationException(error);
        using (var cancel = new CancellationTokenSource())
        {
            Task<AutomationRunOutcome> pending = service.RunNowAsync(slow.Id, cancel.Token);
            await handler.SlowStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            cancel.Cancel();
            AutomationRunOutcome canceled = await pending;
            Check(canceled.Status == "CANCELLED" && canceled.Record?.Status == "CANCELLED", "manual execution propagates cancellation into actions and history");
        }
        AutomationRule serviceCancelable = service.SaveRule(new AutomationRule
        {
            Name = "Cancelable from automation page", Actions = [new("record", "cancel-by-service")]
        }, out error) ?? throw new InvalidOperationException(error);
        Task<AutomationRunOutcome> servicePending = service.RunNowAsync(serviceCancelable.Id);
        await handler.ServiceCancelStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Check(service.Cancel(serviceCancelable.Id), "the UI cancellation API reaches a currently running action");
        AutomationRunOutcome serviceCanceled = await servicePending;
        Check(serviceCanceled.Status == "CANCELLED" && !service.IsRunning(serviceCancelable.Id), "service cancellation is recorded and clears the running state");

        using var reloaded = new AutomationService(new AutomationActionRegistry([new RecordingAction()]), notifications, directory);
        Check(reloaded.GetRules().Any(x => x.Id == manual.Id) && reloaded.GetRules().Any(x => x.Id == daily.Id), "automation definitions persist and retain stable IDs");
        Check(reloaded.GetRules().Single(x => x.Id == daily.Id).LastScheduledDate == "2026-10-03", "last scheduled local date persists across restart");
        Check(reloaded.GetHistory().Count >= 5, "execution history persists with results and durations");
        Check(reloaded.GetHistory().All(x => x.DurationMilliseconds >= 0 && x.TotalActions > 0), "history records bounded timing and action counts");
        Check(!service.Delete("missing-rule", out _), "deleting an unknown rule is reported as a failure");

        string corruptRoot = Path.Combine(directory, "corrupt");
        string corruptStore = Path.Combine(corruptRoot, "Automations", "automations.json");
        Directory.CreateDirectory(Path.GetDirectoryName(corruptStore)!);
        File.WriteAllText(corruptStore, "{ definitely not json");
        using var recovered = new AutomationService(new AutomationActionRegistry([new RecordingAction()]), notifications, corruptRoot);
        Check(recovered.GetRules().Count == 0 && recovered.LastStorageError != null, "corrupt storage degrades to an empty usable service with a visible warning");
        Check(Directory.EnumerateFiles(Path.GetDirectoryName(corruptStore)!, "automations.json.corrupt-*").Any(), "corrupt storage is quarantined instead of overwritten");
    }
}
