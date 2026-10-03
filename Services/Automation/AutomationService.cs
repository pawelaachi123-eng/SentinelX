using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SentinelX.Services.Notifications;

namespace SentinelX.Services.Automation;

/// <summary>
/// Local, bounded automation runtime. Trigger evaluation is separate from action dispatch;
/// rules contain typed, validated actions and can never become arbitrary shell commands.
/// </summary>
public sealed class AutomationService : IDisposable
{
    private const int MaxRules = 100;
    private const int MaxHistory = 500;
    private const int MaxActionsPerRule = 5;
    private const long MaxStoreBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly object gate = new();
    private readonly string storePath;
    private readonly AutomationActionRegistry actions;
    private readonly INotificationService notifications;
    private readonly TimeProvider timeProvider;
    private readonly HashSet<string> running = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CancellationTokenSource> runCancellation = new(StringComparer.OrdinalIgnoreCase);
    private List<AutomationRule> rules = [];
    private List<AutomationRunRecord> history = [];
    private CancellationTokenSource? lifetime;
    private Task? background;
    private bool disposed;

    public event Action? Changed;
    public string? LastStorageError { get; private set; }
    public string StorePath => storePath;
    public bool IsRunning(string ruleId) { lock (gate) return running.Contains(ruleId); }

    /// <summary>Requests cancellation of an active run, including scheduled runs; completed side effects are not rolled back.</summary>
    public bool Cancel(string ruleId)
    {
        CancellationTokenSource? source;
        lock (gate)
        {
            if (disposed || !runCancellation.TryGetValue(ruleId, out source)) return false;
        }
        if (source == null) return false;
        try { source.Cancel(); return true; }
        catch (ObjectDisposedException) { return false; }
        catch (AggregateException ex)
        {
            AppLog.Write("Automation", "Warning", "An automation cancellation callback failed.", ex);
            return true;
        }
    }

    public IReadOnlyList<AutomationActionDescriptor> GetAvailableActions() => actions.GetAvailableActions();
    public bool TryValidateAction(AutomationStep action, out AutomationStep normalized, out string error) =>
        actions.TryNormalize(action, out normalized, out error);

    public AutomationService(AutomationActionRegistry actions, INotificationService notifications,
        string? dataDirectory = null, TimeProvider? timeProvider = null)
    {
        this.actions = actions;
        this.notifications = notifications;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        storePath = Path.Combine(dataDirectory ?? AppPaths.Root, "Automations", "automations.json");
        Load();
    }

    public IReadOnlyList<AutomationRule> GetRules()
    {
        lock (gate) return rules.Select(Clone).ToArray();
    }

    public IReadOnlyList<AutomationRunRecord> GetHistory(int count = 100)
    {
        lock (gate) return history.Take(Math.Clamp(count, 1, MaxHistory)).ToArray();
    }

    /// <summary>Validates the complete rule and persists it atomically. Existing run evidence is preserved on edit.</summary>
    public AutomationRule? SaveRule(AutomationRule candidate, out string error)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        AutomationRule? saved;
        lock (gate)
        {
            if (disposed) { error = "Automatyzacje są już zamknięte."; return null; }
            AutomationRule? existing = string.IsNullOrWhiteSpace(candidate.Id)
                ? null : rules.FirstOrDefault(x => x.Id.Equals(candidate.Id, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(candidate.Id) && existing == null)
            { error = "Nie znaleziono automatyzacji do edycji. Odśwież listę."; return null; }
            if (existing != null && running.Contains(existing.Id))
            { error = "Nie można edytować automatyzacji w trakcie jej działania."; return null; }
            if (rules.Count >= MaxRules && existing == null)
            { error = $"Osiągnięto limit {MaxRules} automatyzacji."; return null; }

            if (!TryNormalizeRule(candidate, existing, out saved, out error)) return null;
            var next = rules.ToList();
            if (existing == null) next.Add(saved!);
            else next[next.FindIndex(x => x.Id == existing.Id)] = saved!;
            if (!Persist(next, history)) { error = LastStorageError ?? "Nie udało się zapisać automatyzacji."; return null; }
            rules = next;
        }
        error = "";
        RaiseChanged();
        return Clone(saved!);
    }

    public bool SetEnabled(string ruleId, bool enabled, out string error)
    {
        lock (gate)
        {
            AutomationRule? existing = rules.FirstOrDefault(x => x.Id.Equals(ruleId, StringComparison.OrdinalIgnoreCase));
            if (existing == null) { error = "Nie znaleziono automatyzacji."; return false; }
            if (running.Contains(existing.Id)) { error = "Nie można zmienić włączonej automatyzacji, dopóki jej wykonanie trwa."; return false; }
            AutomationRule updated = existing with { Enabled = enabled };
            var next = rules.Select(x => x.Id == existing.Id ? updated : x).ToList();
            if (!Persist(next, history)) { error = LastStorageError ?? "Nie udało się zapisać ustawienia."; return false; }
            rules = next;
        }
        error = "";
        RaiseChanged();
        return true;
    }

    public bool Delete(string ruleId, out string error)
    {
        lock (gate)
        {
            AutomationRule? existing = rules.FirstOrDefault(x => x.Id.Equals(ruleId, StringComparison.OrdinalIgnoreCase));
            if (existing == null) { error = "Nie znaleziono automatyzacji."; return false; }
            if (running.Contains(existing.Id)) { error = "Nie można usunąć automatyzacji w trakcie jej działania."; return false; }
            var next = rules.Where(x => x.Id != existing.Id).ToList();
            if (!Persist(next, history)) { error = LastStorageError ?? "Nie udało się zapisać zmian."; return false; }
            rules = next;
        }
        error = "";
        RaiseChanged();
        return true;
    }

    /// <summary>Runs one rule because the user explicitly clicked Run Now; disabled rules may be run this way.</summary>
    public Task<AutomationRunOutcome> RunNowAsync(string ruleId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(ruleId, "manual", null, requireEnabled: false, cancellationToken);

    /// <summary>Called by the hosted scheduler and exposed for deterministic regression tests.</summary>
    public async Task<int> RunDueSchedulesAsync(DateTimeOffset? now = null, CancellationToken cancellationToken = default)
    {
        DateTime localNow = (now ?? timeProvider.GetLocalNow()).LocalDateTime;
        string dateKey = localNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        TimeOnly currentTime = TimeOnly.FromDateTime(localNow);
        string[] due;
        lock (gate)
        {
            due = rules.Where(rule => rule.Enabled && rule.Trigger == AutomationTriggerKind.DailySchedule
                    && rule.LastScheduledDate != dateKey
                    && AutomationValidation.IsValidScheduleTime(rule.ScheduleTime, out string schedule)
                    && currentTime >= TimeOnly.ParseExact(schedule, "HH:mm", CultureInfo.InvariantCulture))
                .Select(rule => rule.Id).ToArray();
        }
        int started = 0;
        foreach (string id in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AutomationRunOutcome result = await ExecuteAsync(id, "daily schedule", dateKey, requireEnabled: true, cancellationToken).ConfigureAwait(false);
            if (result.Started) started++;
        }
        return started;
    }

    /// <summary>Starts once after the shell is shown. No background scheduler starts in UI smoke tests.</summary>
    public void Start()
    {
        if (Environment.GetEnvironmentVariable("SENTINEL_UI_SMOKE") == "1"
            || Environment.GetEnvironmentVariable("SENTINEL_NO_AUTOMATION") == "1") return;
        CancellationToken token;
        lock (gate)
        {
            if (disposed || lifetime != null) return;
            lifetime = new CancellationTokenSource();
            token = lifetime.Token;
            background = Task.Run(() => RunHostedAsync(token));
        }
    }

    private async Task RunHostedAsync(CancellationToken token)
    {
        Task startup = RunStartupAutomationsAsync(token);
        Task scheduler = RunScheduleLoopAsync(token);
        try { await Task.WhenAll(startup, scheduler).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { AppLog.Write(ex); }
    }

    private async Task RunStartupAutomationsAsync(CancellationToken token)
    {
        string[] startupIds;
        lock (gate) startupIds = rules.Where(x => x.Enabled && x.Trigger == AutomationTriggerKind.ApplicationStartup).Select(x => x.Id).ToArray();
        foreach (string id in startupIds)
        {
            if (token.IsCancellationRequested) break;
            try { await ExecuteAsync(id, "application startup", null, requireEnabled: true, token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex) { AppLog.Write(ex); }
        }
    }

    private async Task RunScheduleLoopAsync(CancellationToken token)
    {
        try
        {
            // Run a catch-up check immediately, then use one low-frequency timer; no process polling or busy loop.
            await RunDueSchedulesAsync(cancellationToken: token).ConfigureAwait(false);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                await RunDueSchedulesAsync(cancellationToken: token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { AppLog.Write(ex); }
    }

    private async Task<AutomationRunOutcome> ExecuteAsync(string ruleId, string trigger, string? scheduledDate,
        bool requireEnabled, CancellationToken cancellationToken)
    {
        AutomationRule? rule;
        CancellationTokenSource linked;
        lock (gate)
        {
            if (disposed) return new(false, "CLOSED", "Automatyzacje są zamknięte.");
            rule = rules.FirstOrDefault(x => x.Id.Equals(ruleId, StringComparison.OrdinalIgnoreCase));
            if (rule == null) return new(false, "NOT_FOUND", "Nie znaleziono automatyzacji.");
            if (requireEnabled && !rule.Enabled) return new(false, "DISABLED", "Automatyzacja jest wyłączona.");
            if (scheduledDate != null && rule.LastScheduledDate == scheduledDate)
                return new(false, "ALREADY_RAN", "Dzisiejsza automatyzacja już została uruchomiona.");
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime?.Token ?? CancellationToken.None);
            if (!running.Add(rule.Id))
            {
                linked.Dispose();
                return new(false, "BUSY", "Ta automatyzacja już działa.");
            }
            runCancellation[rule.Id] = linked;
        }
        RaiseChanged();

        using var linkedTokenSource = linked;
        CancellationToken token = linkedTokenSource.Token;
        DateTimeOffset startedAt = timeProvider.GetUtcNow();
        var stopwatch = Stopwatch.StartNew();
        int completed = 0;
        string status = "SUCCESS";
        string message = "Wykonano wszystkie akcje.";
        string error = "";
        try
        {
            foreach (AutomationStep step in rule.Actions)
            {
                token.ThrowIfCancellationRequested();
                AutomationActionResult result = await actions.ExecuteAsync(step, token).ConfigureAwait(false);
                if (!result.Success)
                {
                    status = completed == 0 ? "FAILED" : "PARTIAL";
                    message = result.Message.Length > 0 ? result.Message : "Jedna z akcji nie powiodła się.";
                    error = result.Evidence;
                    break;
                }
                completed++;
            }
            if (completed == 0 && rule.Actions.Count == 0)
            {
                status = "FAILED";
                message = "Automatyzacja nie zawiera żadnych akcji.";
                error = "Zapisz co najmniej jedną obsługiwaną akcję.";
            }
        }
        catch (OperationCanceledException)
        {
            status = "CANCELLED";
            message = "Wykonywanie automatyzacji anulowano. Ukończone akcje nie są cofane.";
        }
        catch (Exception ex)
        {
            AppLog.Write("Automation", "Error", "A registered automation action failed.", ex);
            status = completed == 0 ? "FAILED" : "PARTIAL";
            message = "Akcja automatyzacji zakończyła się błędem.";
            error = ex.Message;
        }
        finally
        {
            stopwatch.Stop();
            DateTimeOffset finishedAt = timeProvider.GetUtcNow();
            var record = new AutomationRunRecord
            {
                Id = Guid.NewGuid().ToString("N"), RuleId = rule.Id, RuleName = LimitText(rule.Name, 80), Trigger = LimitText(trigger, 40),
                StartedAt = startedAt, FinishedAt = finishedAt, DurationMilliseconds = stopwatch.ElapsedMilliseconds,
                Status = status, Result = LimitText(message, 1000), Error = LimitText(error, 2000), CompletedActions = completed, TotalActions = rule.Actions.Count
            };
            lock (gate)
            {
                running.Remove(rule.Id);
                runCancellation.Remove(rule.Id);
                var nextRules = rules.Select(x => x.Id == rule.Id
                    ? x with
                    {
                        LastRunAt = finishedAt,
                        LastRunStatus = status,
                        LastRunMessage = LimitText(message, 1000),
                        LastRunDurationMilliseconds = stopwatch.ElapsedMilliseconds,
                        LastScheduledDate = scheduledDate ?? x.LastScheduledDate
                    }
                    : x).ToList();
                var nextHistory = new List<AutomationRunRecord>(history.Count + 1) { record };
                nextHistory.AddRange(history.Take(MaxHistory - 1));
                rules = nextRules;
                history = nextHistory;
                Persist(rules, history);
            }
            bool alreadyNotified = status == "SUCCESS" && rule.Actions.Take(completed).Any(x => x.ActionId == "notify");
            if (!alreadyNotified)
                notifications.Publish("Automation", "Automatyzacja: " + rule.Name,
                    status == "SUCCESS" ? $"Zakończono ({completed}/{rule.Actions.Count} akcji, {stopwatch.ElapsedMilliseconds} ms)." : message,
                    status is "FAILED" or "PARTIAL" or "CANCELLED" ? "warn" : "info");
            RaiseChanged();
        }
        return new(true, status, message, GetHistory(1).FirstOrDefault());
    }

    private bool TryNormalizeRule(AutomationRule input, AutomationRule? existing, out AutomationRule? normalized, out string error,
        bool preserveCandidateId = false)
    {
        normalized = null;
        string name = (input.Name ?? "").Trim();
        if (name.Length is 0 or > 80 || name.Any(char.IsControl))
        { error = "Nazwa musi mieć od 1 do 80 znaków i nie może zawierać znaków sterujących."; return false; }
        if (!Enum.IsDefined(typeof(AutomationTriggerKind), input.Trigger))
        { error = "Nieobsługiwany typ wyzwalacza."; return false; }
        if (rules.Any(x => x.Id != existing?.Id && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        { error = "Istnieje już automatyzacja o takiej nazwie."; return false; }
        string schedule = "";
        if (input.Trigger == AutomationTriggerKind.DailySchedule && !AutomationValidation.IsValidScheduleTime(input.ScheduleTime, out schedule))
        { error = "Podaj godzinę w formacie HH:mm (czas lokalny komputera)."; return false; }
        AutomationStep[] inputActions = (input.Actions ?? Array.Empty<AutomationStep>()).ToArray();
        if (inputActions.Length is < 1 or > MaxActionsPerRule)
        { error = $"Automatyzacja musi zawierać od 1 do {MaxActionsPerRule} akcji."; return false; }
        var steps = new AutomationStep[inputActions.Length];
        for (int i = 0; i < inputActions.Length; i++)
        {
            if (!actions.TryNormalize(inputActions[i], out steps[i], out error)) return false;
        }
        string lastScheduledDate = existing == null || existing.Trigger != input.Trigger || existing.ScheduleTime != schedule
            ? "" : existing.LastScheduledDate;
        DateTimeOffset? lastRunAt = existing?.LastRunAt;
        string lastRunStatus = existing?.LastRunStatus ?? "";
        string lastRunMessage = existing?.LastRunMessage ?? "";
        long lastRunDuration = existing?.LastRunDurationMilliseconds ?? 0;
        if (preserveCandidateId)
        {
            lastRunAt = input.LastRunAt;
            lastRunStatus = input.LastRunStatus is "SUCCESS" or "FAILED" or "PARTIAL" or "CANCELLED" ? input.LastRunStatus : "";
            lastRunMessage = LimitText(input.LastRunMessage, 1000);
            lastRunDuration = Math.Clamp(input.LastRunDurationMilliseconds, 0, 3_600_000);
            lastScheduledDate = input.Trigger == AutomationTriggerKind.DailySchedule
                && DateOnly.TryParseExact(input.LastScheduledDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                ? input.LastScheduledDate : "";
        }
        normalized = new AutomationRule
        {
            Id = existing?.Id ?? (preserveCandidateId ? input.Id.Trim() : Guid.NewGuid().ToString("N")),
            Name = name, Trigger = input.Trigger, ScheduleTime = schedule, Enabled = input.Enabled,
            CreatedAt = existing?.CreatedAt ?? (input.CreatedAt == default ? timeProvider.GetUtcNow() : input.CreatedAt),
            LastRunAt = lastRunAt, LastRunStatus = lastRunStatus,
            LastRunMessage = lastRunMessage, LastRunDurationMilliseconds = lastRunDuration,
            LastScheduledDate = lastScheduledDate,
            Actions = steps
        };
        error = "";
        return true;
    }

    private void Load()
    {
        if (!File.Exists(storePath)) return;
        try
        {
            var info = new FileInfo(storePath);
            if (info.Length > MaxStoreBytes) throw new InvalidDataException("Magazyn automatyzacji przekracza limit 4 MB.");
            AutomationStore store = JsonSerializer.Deserialize<AutomationStore>(File.ReadAllText(storePath), JsonOptions)
                ?? throw new InvalidDataException("Magazyn automatyzacji jest pusty.");
            if (store.Version != 1) throw new InvalidDataException("Nieobsługiwana wersja magazynu automatyzacji.");
            foreach (AutomationRule? candidate in (store.Rules ?? []).Take(MaxRules))
            {
                if (candidate == null || !Guid.TryParseExact(candidate.Id, "N", out _) || rules.Any(x => x.Id.Equals(candidate.Id, StringComparison.OrdinalIgnoreCase)))
                { LastStorageError = "Pominięto nieprawidłowy lub powtórzony wpis automatyzacji."; continue; }
                if (TryNormalizeRule(candidate, null, out AutomationRule? normalized, out string error, preserveCandidateId: true)) rules.Add(normalized!);
                else LastStorageError = "Pominięto nieprawidłową automatyzację: " + error;
            }
            history = (store.History ?? [])
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Id) && !string.IsNullOrWhiteSpace(x.RuleId))
                .Take(MaxHistory)
                .Select(x => NormalizeHistory(x!)).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            string quarantine = storePath + ".corrupt-" + timeProvider.GetUtcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            try { File.Move(storePath, quarantine, false); }
            catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException) { }
            LastStorageError = "Nie udało się odczytać magazynu automatyzacji; zachowałem uszkodzony plik jako kopię, jeśli system na to pozwolił. " + ex.Message;
            AppLog.Write("Automation", "Warning", "Automation storage could not be loaded.", ex);
        }
    }

    private bool Persist(List<AutomationRule> candidateRules, List<AutomationRunRecord> candidateHistory)
    {
        string temp = storePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
            string json = JsonSerializer.Serialize(new AutomationStore
            {
                Version = 1,
                Rules = candidateRules,
                History = candidateHistory.Take(MaxHistory).ToList()
            }, JsonOptions);
            File.WriteAllText(temp, json);
            _ = JsonSerializer.Deserialize<AutomationStore>(File.ReadAllText(temp), JsonOptions)
                ?? throw new InvalidDataException("Zapis automatyzacji nie przeszedł odczytu kontrolnego.");
            if (File.Exists(storePath)) File.Copy(storePath, storePath + ".backup", true);
            File.Move(temp, storePath, true);
            _ = JsonSerializer.Deserialize<AutomationStore>(File.ReadAllText(storePath), JsonOptions)
                ?? throw new InvalidDataException("Zapis automatyzacji nie przeszedł końcowego odczytu kontrolnego.");
            LastStorageError = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or NotSupportedException)
        {
            LastStorageError = "Nie udało się bezpiecznie zapisać automatyzacji: " + ex.Message;
            AppLog.Write("Automation", "Error", "Automation data could not be saved.", ex);
            return false;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static AutomationRunRecord NormalizeHistory(AutomationRunRecord record) => record with
    {
        Id = LimitText(record.Id, 64),
        RuleId = LimitText(record.RuleId, 64),
        RuleName = LimitText(record.RuleName, 80),
        Trigger = LimitText(record.Trigger, 40),
        Status = LimitText(record.Status, 24),
        Result = LimitText(record.Result, 1000),
        Error = LimitText(record.Error, 2000)
    };

    private static string LimitText(string? value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value) || maximumLength <= 0) return "";
        char[] safe = value.Where(ch => !char.IsControl(ch) || ch is '\r' or '\n').Take(maximumLength).ToArray();
        return new string(safe);
    }

    private static AutomationRule Clone(AutomationRule rule) => rule with { Actions = (rule.Actions ?? Array.Empty<AutomationStep>()).ToArray() };

    private void RaiseChanged()
    {
        foreach (Action observer in Changed?.GetInvocationList() ?? [])
        {
            try { observer(); }
            catch (Exception ex) { AppLog.Write(ex); }
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public void Dispose()
    {
        CancellationTokenSource? source;
        Task? task;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            source = lifetime;
            task = background;
            lifetime = null;
        }
        if (source == null) return;
        try { source.Cancel(); }
        catch (AggregateException ex) { AppLog.Write("Automation", "Warning", "Automation shutdown cancellation callbacks failed.", ex); }
        if (task == null) source.Dispose();
        else _ = task.ContinueWith(_ => source.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
