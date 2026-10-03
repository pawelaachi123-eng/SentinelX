using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Threading.Channels;
using System.Security.Cryptography;
using SentinelX.Core;
using SentinelX.Models;
using SentinelX.Services.Actions;
using SentinelX.Services.Automation;
using SentinelX.Services.Monitoring;

namespace SentinelX.Services.Link;

/// <summary>Endpoints available to an already paired phone. Deliberately thin: every action runs through the same services the
/// desktop UI uses, so the phone sees exactly what the PC sees (chat, tasks, notes, measurements) and obeys the same safety rules.
/// Commands from the phone are treated like voice: a destructive action can be proposed here but is only confirmed on the PC.</summary>
internal sealed class LinkApi
{
    private readonly IActionEngine engine;
    private readonly ISystemMonitorService monitor;
    private readonly TaskService tasks;
    private readonly ConversationMemoryService memory;
    private readonly AlertFeed alerts;
    private readonly Func<LinkInfo> info;
    private readonly Func<IReadOnlyList<string>> addresses;
    private readonly AutomationService? automations;
    private readonly PhoneFileTransferService? fileTransfers;
    private readonly IPcSystemActions systemActions;
    private readonly object powerGate = new();
    private readonly Dictionary<string, PendingPowerAction> powerChallenges = new(StringComparer.Ordinal);
    private static readonly TimeSpan PowerChallengeLifetime = TimeSpan.FromSeconds(60);

    private sealed record PendingPowerAction(string DeviceId, string Action, DateTimeOffset ExpiresAt);

    public LinkApi(IActionEngine engine, ISystemMonitorService monitor, TaskService tasks, ConversationMemoryService memory,
        AlertFeed alerts, Func<LinkInfo> info, Func<IReadOnlyList<string>> addresses,
        AutomationService? automations = null, PhoneFileTransferService? fileTransfers = null,
        IPcSystemActions? systemActions = null)
    {
        this.engine = engine;
        this.monitor = monitor;
        this.tasks = tasks;
        this.memory = memory;
        this.alerts = alerts;
        this.info = info;
        this.addresses = addresses;
        this.automations = automations;
        this.fileTransfers = fileTransfers;
        this.systemActions = systemActions ?? new PcSystemActions();
    }

    public async Task HandleAsync(LinkRequest req, LinkResponse res, CancellationToken ct, string deviceId)
    {
        bool get = req.Method is "GET" or "HEAD";
        bool post = req.Method == "POST";
        switch (req.Path)
        {
            case "/api/state" when get: await res.WriteJsonAsync(200, BuildState(), ct).ConfigureAwait(false); return;
            case "/api/chat" when post: await ChatAsync(req, res, ct).ConfigureAwait(false); return;
            case "/api/control" when post: await ControlAsync(req, res, ct).ConfigureAwait(false); return;
            case "/api/tasks" when get: await res.WriteJsonAsync(200, BuildTasks(), ct).ConfigureAwait(false); return;
            case "/api/tasks" when post: await AddTaskAsync(req, res, ct).ConfigureAwait(false); return;
            case "/api/tasks/status" when post: await TaskStatusAsync(req, res, ct).ConfigureAwait(false); return;
            case "/api/tasks/delete" when post: await DeleteTaskAsync(req, res, ct).ConfigureAwait(false); return;
            case "/api/reminders" when post: await AddReminderAsync(req, res, ct).ConfigureAwait(false); return;
            case "/api/reminders/delete" when post: await DeleteReminderAsync(req, res, ct).ConfigureAwait(false); return;
            case "/api/notes" when get: await res.WriteJsonAsync(200, BuildNotes(req.Q("q")), ct).ConfigureAwait(false); return;
            case "/api/notes" when post: await AddNoteAsync(req, res, ct).ConfigureAwait(false); return;
            case "/api/alerts" when get: await AlertsAsync(req, res, ct).ConfigureAwait(false); return;
            case "/api/files" when get: await FilesAsync(res, ct, deviceId).ConfigureAwait(false); return;
            case "/api/files/upload/start" when post: await StartFileUploadAsync(req, res, ct, deviceId).ConfigureAwait(false); return;
            case "/api/files/upload/chunk" when post: await WriteFileChunkAsync(req, res, ct, deviceId).ConfigureAwait(false); return;
            case "/api/files/upload/cancel" when post: await CancelFileUploadAsync(req, res, ct, deviceId).ConfigureAwait(false); return;
            case "/api/files/download" when get: await DownloadFileAsync(req, res, ct, deviceId).ConfigureAwait(false); return;
            case "/api/files/delete" when post: await DeleteFileAsync(req, res, ct, deviceId).ConfigureAwait(false); return;
            case "/api/automations" when get: await AutomationsAsync(res, ct).ConfigureAwait(false); return;
            case "/api/automations/save" when post: await SaveAutomationAsync(req, res, ct).ConfigureAwait(false); return;
            case "/api/automations/enabled" when post: await SetAutomationEnabledAsync(req, res, ct).ConfigureAwait(false); return;
            case "/api/automations/run" when post: await RunAutomationAsync(req, res, ct).ConfigureAwait(false); return;
            case "/api/automations/cancel" when post: await CancelAutomationAsync(req, res, ct).ConfigureAwait(false); return;
            case "/api/automations/delete" when post: await DeleteAutomationAsync(req, res, ct).ConfigureAwait(false); return;
            case "/api/power/prepare" when post: await PreparePowerActionAsync(req, res, ct, deviceId).ConfigureAwait(false); return;
            case "/api/power/execute" when post: await ExecutePowerActionAsync(req, res, ct, deviceId).ConfigureAwait(false); return;
        }
        await res.WriteJsonAsync(404, new LinkErrorResponse("Nie znaleziono."), ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ state

    internal LinkStateResponse BuildState()
    {
        SystemSnapshot snapshot = monitor.Current;
        LinkInfo link = info();
        string uptime = FormatUptime(TimeSpan.FromMilliseconds(Environment.TickCount64));
        return new LinkStateResponse(
            Pc: new LinkPcState(Environment.MachineName, link.Version, uptime,
                DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture), LinkNetwork.GetWakeMacs(), addresses()),
            Metrics: new LinkMetricsState(
                Cpu: Num(snapshot.Cpu),
                RamUsed: Num(snapshot.RamUsed),
                RamTotal: Num(snapshot.RamTotal),
                Gpu: Num(snapshot.Gpu),
                Game: snapshot.GamingDetectionAvailable ? snapshot.Game ?? "" : "Niedostępne",
                Network: snapshot.Network ?? "",
                Disks: snapshot.Disks.Select(d => new LinkDiskState(d.Name, Num(d.UsedGb), Num(d.TotalGb))).ToArray(),
                Processes: snapshot.Processes.Take(8).Select(p => new LinkProcessState(p.Name, p.Pid, Num(p.MemoryMb, 0), Num(p.CpuPercent ?? double.NaN))).ToArray()),
            Engine: new LinkEngineState(link.EngineState, link.EngineMessage, Num(link.EngineProgress, 3), link.EngineModel, link.EngineInstalled),
            Assistant: new LinkAssistantState(engine.IsBusy, engine.IsStopped, engine.HasPendingPermission, engine.PermissionSummary),
            Care: new LinkCareState(link.CareOk, link.CareText),
            Counts: new LinkCountsState(tasks.GetTasks().Count, tasks.GetReminders().Count(r => r.NotifiedAt == null), memory.NoteCount, alerts.After(0, 200).Count),
            AlertsLast: alerts.LastId,
            Capabilities: LinkProtocol.HostCapabilities);
    }

    private static double? Num(double value, int digits = 1) => double.IsFinite(value) ? Math.Round(value, digits) : null;

    private static string FormatUptime(TimeSpan span) =>
        span.TotalDays >= 1 ? $"{(int)span.TotalDays} d {span.Hours} h" : span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes} min" : $"{span.Minutes} min";

    // ------------------------------------------------------------ chat (Server-Sent Events)

    private async Task ChatAsync(LinkRequest req, LinkResponse res, CancellationToken ct)
    {
        LinkChatRequest? command = req.Deserialize<LinkChatRequest>();
        string text = command?.Text?.Trim() ?? "";
        if (text.Length == 0) { await res.WriteJsonAsync(400, new LinkErrorResponse("Wpisz polecenie."), ct).ConfigureAwait(false); return; }
        if (text.Length > 4000) { await res.WriteJsonAsync(400, new LinkErrorResponse("Polecenie jest za długie (limit 4000 znaków)."), ct).ConfigureAwait(false); return; }

        await res.StartSseAsync(ct).ConfigureAwait(false);
        await res.WriteSseAsync("start", new LinkChatStartedResponse(), ct).ConfigureAwait(false);

        // Bound buffered output for a slow/disconnected phone. If the queue fills, intermediate deltas may be skipped;
        // the final typed "done" event still carries the complete answer.
        var chunks = Channel.CreateBounded<string>(new BoundedChannelOptions(128)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // fromVoice: true → the engine itself refuses to confirm risky actions; they are confirmed only on the PC.
        Task<IntentResult> run = engine.ExecuteAsync(text, cts.Token, fromVoice: true, onDelta: chunk => chunks.Writer.TryWrite(chunk));
        Task pump = PumpAsync(chunks.Reader, res, cts);

        IntentResult? result = null;
        string failure = "";
        try { result = await run.ConfigureAwait(false); }
        catch (OperationCanceledException) { failure = "Przerwano."; }
        catch (Exception ex) { AppLog.Write(ex); failure = "Nie udało się wykonać polecenia."; }
        finally { chunks.Writer.TryComplete(); }

        try { await pump.ConfigureAwait(false); }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException or SocketException) { return; }

        if (result != null)
        {
            ActionRecord? record = result.Action;
            await res.WriteSseAsync("done", new LinkChatDoneResponse(
                result.Text,
                record == null ? "" : record.Status.ToString().ToLowerInvariant(),
                record?.Evidence ?? "",
                record?.ElapsedMilliseconds ?? 0), ct).ConfigureAwait(false);
        }
        else await res.WriteSseAsync("error", new LinkErrorResponse(failure.Length > 0 ? failure : "Błąd."), ct).ConfigureAwait(false);
    }

    private static async Task PumpAsync(ChannelReader<string> reader, LinkResponse res, CancellationTokenSource cts)
    {
        try
        {
            await foreach (string chunk in reader.ReadAllAsync(cts.Token).ConfigureAwait(false))
                await res.WriteSseAsync("delta", new LinkChatDeltaResponse(chunk), cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
        {
            cts.Cancel(); // the phone went away: stop generating for nobody
            throw;
        }
    }

    private async Task ControlAsync(LinkRequest req, LinkResponse res, CancellationToken ct)
    {
        LinkControlRequest? command = req.Deserialize<LinkControlRequest>();
        switch (command?.Action)
        {
            case "cancel": engine.Cancel(); break;
            case "stop": engine.EmergencyStop(); break;
            case "resume": engine.Resume(); break;
            default: await res.WriteJsonAsync(400, new LinkErrorResponse("Nieznana akcja."), ct).ConfigureAwait(false); return;
        }
        await res.WriteJsonAsync(200, new LinkControlResponse(true, engine.IsStopped), ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ typed PC power actions

    private async Task PreparePowerActionAsync(LinkRequest req, LinkResponse res, CancellationToken ct, string deviceId)
    {
        LinkPowerRequest? command = req.Deserialize<LinkPowerRequest>();
        string action = (command?.Action ?? "").Trim().ToLowerInvariant();
        if (action is not ("lock" or "restart" or "shutdown"))
        { await res.WriteJsonAsync(400, new LinkErrorResponse("Dozwolone operacje to blokada, restart i wyłączenie."), ct).ConfigureAwait(false); return; }
        if (!systemActions.IsSupported)
        { await res.WriteJsonAsync(503, new LinkErrorResponse("Sterowanie zasilaniem nie jest obsługiwane przez ten komputer."), ct).ConfigureAwait(false); return; }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string challenge = LinkDeviceStore.Base64Url(RandomNumberGenerator.GetBytes(24));
        bool challengeLimitReached;
        lock (powerGate)
        {
            foreach (string expired in powerChallenges.Where(x => x.Value.ExpiresAt <= now).Select(x => x.Key).ToArray())
                powerChallenges.Remove(expired);
            challengeLimitReached = powerChallenges.Count >= 64;
            if (!challengeLimitReached)
                powerChallenges.Add(challenge, new PendingPowerAction(deviceId, action, now + PowerChallengeLifetime));
        }
        if (challengeLimitReached)
        { await res.WriteJsonAsync(429, new LinkErrorResponse("Jest już zbyt wiele potwierdzeń oczekujących."), ct).ConfigureAwait(false); return; }
        await res.WriteJsonAsync(200, new LinkPowerPreparedResponse(true, action, challenge,
            (int)PowerChallengeLifetime.TotalSeconds), ct).ConfigureAwait(false);
    }

    private async Task ExecutePowerActionAsync(LinkRequest req, LinkResponse res, CancellationToken ct, string deviceId)
    {
        LinkPowerRequest? command = req.Deserialize<LinkPowerRequest>();
        string action = (command?.Action ?? "").Trim().ToLowerInvariant();
        string challenge = (command?.Challenge ?? "").Trim();
        if (req.RequestId.Length is < 20 or > 64)
        { await res.WriteJsonAsync(400, new LinkErrorResponse("Brak unikalnego identyfikatora żądania."), ct).ConfigureAwait(false); return; }
        if (action is not ("lock" or "restart" or "shutdown") || challenge.Length is < 20 or > 64)
        { await res.WriteJsonAsync(400, new LinkErrorResponse("Nieprawidłowa prośba o potwierdzenie operacji."), ct).ConfigureAwait(false); return; }

        string failure = "";
        lock (powerGate)
        {
            if (!powerChallenges.TryGetValue(challenge, out PendingPowerAction? pending))
                failure = "Potwierdzenie nie istnieje, wygasło lub zostało już użyte.";
            else if (pending.DeviceId != deviceId)
                failure = "Potwierdzenie należy do innego telefonu.";
            else if (pending.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                powerChallenges.Remove(challenge);
                failure = "Potwierdzenie wygasło. Rozpocznij operację ponownie.";
            }
            else if (pending.Action != action)
                failure = "Operacja nie zgadza się z przygotowanym potwierdzeniem.";
            else powerChallenges.Remove(challenge); // one-use, consumed before any OS call
        }
        if (failure.Length > 0)
        {
            await res.WriteJsonAsync(409, new LinkErrorResponse(failure), ct).ConfigureAwait(false);
            return;
        }

        bool ok = systemActions.TryExecute(action, out string message);
        await res.WriteJsonAsync(ok ? 200 : 503, new LinkPowerResponse(ok, action, message), ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ bounded phone file transfer

    private async Task FilesAsync(LinkResponse res, CancellationToken ct, string deviceId)
    {
        if (fileTransfers == null)
        { await res.WriteJsonAsync(503, new LinkErrorResponse("Transfer plików nie jest skonfigurowany na tym komputerze."), ct).ConfigureAwait(false); return; }
        await res.WriteJsonAsync(200, new LinkFilesResponse(fileTransfers.List(deviceId),
            (int)PhoneFileTransferService.MaxFileBytes, PhoneFileTransferService.ChunkBytes), ct).ConfigureAwait(false);
    }

    private async Task StartFileUploadAsync(LinkRequest req, LinkResponse res, CancellationToken ct, string deviceId)
    {
        if (fileTransfers == null)
        { await res.WriteJsonAsync(503, new LinkErrorResponse("Transfer plików nie jest skonfigurowany na tym komputerze."), ct).ConfigureAwait(false); return; }
        LinkFileUploadStartRequest? input = req.Deserialize<LinkFileUploadStartRequest>();
        if (input == null)
        { await res.WriteJsonAsync(400, new LinkErrorResponse("Nieprawidłowe dane pliku."), ct).ConfigureAwait(false); return; }
        if (!fileTransfers.TryBegin(deviceId, input.Name, input.Size, input.Sha256,
                out LinkFileUploadStarted? started, out int status, out string error))
        { await res.WriteJsonAsync(status, new LinkErrorResponse(error), ct).ConfigureAwait(false); return; }
        await res.WriteJsonAsync(200, new LinkFileUploadStartedResponse(true, started!.Id, started.ChunkBytes), ct).ConfigureAwait(false);
    }

    private async Task WriteFileChunkAsync(LinkRequest req, LinkResponse res, CancellationToken ct, string deviceId)
    {
        if (fileTransfers == null)
        { await res.WriteJsonAsync(503, new LinkErrorResponse("Transfer plików nie jest skonfigurowany na tym komputerze."), ct).ConfigureAwait(false); return; }
        LinkFileUploadChunkRequest? input = req.Deserialize<LinkFileUploadChunkRequest>();
        if (input?.Data == null || input.Data.Length > 56 * 1024)
        { await res.WriteJsonAsync(400, new LinkErrorResponse("Brak danych porcji lub przekroczony limit żądania."), ct).ConfigureAwait(false); return; }
        byte[] bytes;
        try { bytes = Convert.FromBase64String(input.Data); }
        catch (FormatException)
        { await res.WriteJsonAsync(400, new LinkErrorResponse("Dane porcji nie są poprawnym Base64."), ct).ConfigureAwait(false); return; }
        LinkFileChunkResult result = await fileTransfers.WriteChunkAsync(deviceId, input.Id, input.Index, bytes, ct).ConfigureAwait(false);
        if (!result.Ok)
        { await res.WriteJsonAsync(result.HttpStatus, new LinkErrorResponse(result.Error ?? "Nie udało się odebrać porcji."), ct).ConfigureAwait(false); return; }
        await res.WriteJsonAsync(200, new LinkFileChunkResponse(true, result.Done, result.NextIndex, result.File), ct).ConfigureAwait(false);
    }

    private async Task CancelFileUploadAsync(LinkRequest req, LinkResponse res, CancellationToken ct, string deviceId)
    {
        if (fileTransfers == null)
        { await res.WriteJsonAsync(503, new LinkErrorResponse("Transfer plików nie jest skonfigurowany na tym komputerze."), ct).ConfigureAwait(false); return; }
        LinkIdRequest? input = req.Deserialize<LinkIdRequest>();
        bool cancelled = input != null && await fileTransfers.CancelAsync(deviceId, input.Id, ct).ConfigureAwait(false);
        await res.WriteJsonAsync(cancelled ? 200 : 404, new LinkOkResponse(cancelled), ct).ConfigureAwait(false);
    }

    private async Task DownloadFileAsync(LinkRequest req, LinkResponse res, CancellationToken ct, string deviceId)
    {
        if (fileTransfers == null)
        { await res.WriteJsonAsync(503, new LinkErrorResponse("Transfer plików nie jest skonfigurowany na tym komputerze."), ct).ConfigureAwait(false); return; }
        if (!fileTransfers.TryGetDownload(deviceId, req.Q("id"), out _, out FileStream? file) || file == null)
        { await res.WriteJsonAsync(404, new LinkErrorResponse("Nie znaleziono pliku na tym telefonie."), ct).ConfigureAwait(false); return; }
        await using FileStream download = file;
        await res.WriteFileAsync(200, download, ct).ConfigureAwait(false);
    }

    private async Task DeleteFileAsync(LinkRequest req, LinkResponse res, CancellationToken ct, string deviceId)
    {
        if (fileTransfers == null)
        { await res.WriteJsonAsync(503, new LinkErrorResponse("Transfer plików nie jest skonfigurowany na tym komputerze."), ct).ConfigureAwait(false); return; }
        LinkIdRequest? input = req.Deserialize<LinkIdRequest>();
        string error = "";
        bool deleted = input != null && fileTransfers.TryDelete(deviceId, input.Id, out error);
        string message = deleted ? "" : input == null ? "Nieprawidłowe żądanie." : error;
        await res.WriteJsonAsync(deleted ? 200 : 404, deleted ? new LinkOkResponse(true) : new LinkErrorResponse(message), ct).ConfigureAwait(false);
    }

    public Task CancelDeviceTransfersAsync(string deviceId, CancellationToken ct = default) =>
        fileTransfers?.CancelOwnerAsync(deviceId, ct) ?? Task.CompletedTask;

    // ------------------------------------------------------------ typed phone automation API

    private async Task AutomationsAsync(LinkResponse res, CancellationToken ct)
    {
        if (automations == null)
        { await res.WriteJsonAsync(503, new LinkErrorResponse("Automatyzacje nie są skonfigurowane na tym komputerze."), ct).ConfigureAwait(false); return; }
        LinkAutomationRuleState[] rules = automations.GetRules()
            .Select(rule => new LinkAutomationRuleState(rule, automations.IsRunning(rule.Id))).ToArray();
        await res.WriteJsonAsync(200, new LinkAutomationsResponse(true,
            automations.LastStorageError ?? "Akcje są ograniczone do katalogu dozwolonych przez Sentinel.",
            automations.GetAvailableActions(), rules, automations.GetHistory(50)), ct).ConfigureAwait(false);
    }

    private async Task SaveAutomationAsync(LinkRequest req, LinkResponse res, CancellationToken ct)
    {
        if (automations == null)
        { await res.WriteJsonAsync(503, new LinkErrorResponse("Automatyzacje nie są skonfigurowane na tym komputerze."), ct).ConfigureAwait(false); return; }
        LinkAutomationSaveRequest? input = req.Deserialize<LinkAutomationSaveRequest>();
        if (input == null || input.Actions == null || input.Actions.Length is < 1 or > 5 || input.Actions.Any(x => x == null))
        { await res.WriteJsonAsync(400, new LinkErrorResponse("Podaj od 1 do 5 typowanych akcji z katalogu."), ct).ConfigureAwait(false); return; }
        var candidate = new AutomationRule
        {
            Id = input.Id ?? "", Name = input.Name ?? "", Trigger = input.Trigger,
            ScheduleTime = input.ScheduleTime ?? "", Enabled = input.Enabled, Actions = input.Actions,
        };
        AutomationRule? saved = automations.SaveRule(candidate, out string error);
        if (saved == null)
        { await res.WriteJsonAsync(400, new LinkErrorResponse(error), ct).ConfigureAwait(false); return; }
        await res.WriteJsonAsync(200, new LinkAutomationRuleState(saved, automations.IsRunning(saved.Id)), ct).ConfigureAwait(false);
    }

    private async Task SetAutomationEnabledAsync(LinkRequest req, LinkResponse res, CancellationToken ct)
    {
        if (automations == null)
        { await res.WriteJsonAsync(503, new LinkErrorResponse("Automatyzacje nie są skonfigurowane na tym komputerze."), ct).ConfigureAwait(false); return; }
        LinkAutomationEnabledRequest? input = req.Deserialize<LinkAutomationEnabledRequest>();
        string error = "";
        bool ok = input != null && automations.SetEnabled(input.Id ?? "", input.Enabled, out error);
        if (!ok)
        { await res.WriteJsonAsync(400, new LinkErrorResponse(input == null ? "Nieprawidłowe żądanie." : error), ct).ConfigureAwait(false); return; }
        await res.WriteJsonAsync(200, new LinkOkResponse(true), ct).ConfigureAwait(false);
    }

    private async Task RunAutomationAsync(LinkRequest req, LinkResponse res, CancellationToken ct)
    {
        if (automations == null)
        { await res.WriteJsonAsync(503, new LinkErrorResponse("Automatyzacje nie są skonfigurowane na tym komputerze."), ct).ConfigureAwait(false); return; }
        if (req.RequestId.Length is < 20 or > 64)
        { await res.WriteJsonAsync(400, new LinkErrorResponse("Brak unikalnego identyfikatora żądania."), ct).ConfigureAwait(false); return; }
        LinkIdRequest? input = req.Deserialize<LinkIdRequest>();
        if (input == null || string.IsNullOrWhiteSpace(input.Id))
        { await res.WriteJsonAsync(400, new LinkErrorResponse("Wybierz zapisaną automatyzację."), ct).ConfigureAwait(false); return; }
        AutomationRunOutcome result = await automations.RunNowAsync(input.Id, ct).ConfigureAwait(false);
        await res.WriteJsonAsync(result.Started ? 200 : 409,
            new LinkAutomationRunResponse(result.Started, result.Status, result.Message, result.Record), ct).ConfigureAwait(false);
    }

    private async Task CancelAutomationAsync(LinkRequest req, LinkResponse res, CancellationToken ct)
    {
        if (automations == null)
        { await res.WriteJsonAsync(503, new LinkErrorResponse("Automatyzacje nie są skonfigurowane na tym komputerze."), ct).ConfigureAwait(false); return; }
        LinkIdRequest? input = req.Deserialize<LinkIdRequest>();
        bool cancelled = input != null && automations.Cancel(input.Id ?? "");
        await res.WriteJsonAsync(cancelled ? 200 : 409, new LinkOkResponse(cancelled), ct).ConfigureAwait(false);
    }

    private async Task DeleteAutomationAsync(LinkRequest req, LinkResponse res, CancellationToken ct)
    {
        if (automations == null)
        { await res.WriteJsonAsync(503, new LinkErrorResponse("Automatyzacje nie są skonfigurowane na tym komputerze."), ct).ConfigureAwait(false); return; }
        LinkIdRequest? input = req.Deserialize<LinkIdRequest>();
        string error = "";
        bool deleted = input != null && automations.Delete(input.Id ?? "", out error);
        if (!deleted)
        { await res.WriteJsonAsync(400, new LinkErrorResponse(input == null ? "Nieprawidłowe żądanie." : error), ct).ConfigureAwait(false); return; }
        await res.WriteJsonAsync(200, new LinkOkResponse(true), ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ tasks and reminders

    private LinkTasksResponse BuildTasks() => new(
        tasks.GetTasks().Select(t => new LinkTaskState(t.Id, t.Title, t.Priority, t.Status, Iso(t.DueAt), t.ProjectId)).ToArray(),
        tasks.GetReminders().Where(r => r.NotifiedAt == null || r.Missed).Select(r => new LinkReminderState(r.Id, r.Text, Iso(r.RemindAt), r.Missed)).ToArray());

    private async Task AddTaskAsync(LinkRequest req, LinkResponse res, CancellationToken ct)
    {
        LinkTaskRequest? input = req.Deserialize<LinkTaskRequest>();
        if (input == null) { await res.WriteJsonAsync(400, new LinkErrorResponse("Niepoprawne dane zadania."), ct).ConfigureAwait(false); return; }
        TaskRecord? created = tasks.AddTask(input.Title ?? "", input.Priority ?? "", ParseLocal(input.Due ?? ""), "");
        if (created == null) { await res.WriteJsonAsync(400, new LinkErrorResponse(tasks.LastStorageError ?? "Nie udało się dodać zadania."), ct).ConfigureAwait(false); return; }
        await res.WriteJsonAsync(200, new LinkIdResponse(true, created.Id), ct).ConfigureAwait(false);
    }

    private async Task TaskStatusAsync(LinkRequest req, LinkResponse res, CancellationToken ct)
    {
        LinkTaskStatusRequest? input = req.Deserialize<LinkTaskStatusRequest>();
        bool ok = input != null && tasks.SetTaskStatus(input.Id ?? "", input.Status ?? "");
        await res.WriteJsonAsync(ok ? 200 : 404, new LinkOkResponse(ok), ct).ConfigureAwait(false);
    }

    private async Task DeleteTaskAsync(LinkRequest req, LinkResponse res, CancellationToken ct)
    {
        LinkIdRequest? input = req.Deserialize<LinkIdRequest>();
        bool ok = input != null && tasks.DeleteTask(input.Id ?? "");
        await res.WriteJsonAsync(ok ? 200 : 404, new LinkOkResponse(ok), ct).ConfigureAwait(false);
    }

    private async Task AddReminderAsync(LinkRequest req, LinkResponse res, CancellationToken ct)
    {
        LinkReminderRequest? input = req.Deserialize<LinkReminderRequest>();
        DateTime? at = ParseLocal(input?.At ?? "");
        ReminderRecord? created = at == null ? null : tasks.AddReminder(input?.Text ?? "", at.Value, "");
        if (created == null) { await res.WriteJsonAsync(400, new LinkErrorResponse(at == null ? "Podaj termin przypomnienia." : tasks.LastStorageError ?? "Nie udało się dodać przypomnienia."), ct).ConfigureAwait(false); return; }
        await res.WriteJsonAsync(200, new LinkIdResponse(true, created.Id), ct).ConfigureAwait(false);
    }

    private async Task DeleteReminderAsync(LinkRequest req, LinkResponse res, CancellationToken ct)
    {
        LinkIdRequest? input = req.Deserialize<LinkIdRequest>();
        bool ok = input != null && tasks.DeleteReminder(input.Id ?? "");
        await res.WriteJsonAsync(ok ? 200 : 404, new LinkOkResponse(ok), ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ notes

    private LinkNotesResponse BuildNotes(string query) => new(
        memory.SearchNotes(query).Where(n => n.SupersededAt == null).Take(100)
            .Select(n => new LinkNoteState(n.Id, n.Text, n.Category, n.Pinned, Iso(n.UpdatedAt ?? n.Timestamp))).ToArray());

    private async Task AddNoteAsync(LinkRequest req, LinkResponse res, CancellationToken ct)
    {
        LinkNoteRequest? input = req.Deserialize<LinkNoteRequest>();
        NoteAddResult result = memory.AddNote(input?.Text ?? "", "notatka", "phone");
        bool ok = result is NoteAddResult.Added or NoteAddResult.StaleDuplicate;
        await res.WriteJsonAsync(200, new LinkNoteAddedResponse(ok, result.ToString()), ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ alerts

    private async Task AlertsAsync(LinkRequest req, LinkResponse res, CancellationToken ct)
    {
        long.TryParse(req.Q("after"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long after);
        int.TryParse(req.Q("wait"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int wait);
        IReadOnlyList<LinkAlert> list = await alerts.WaitAfterAsync(after, TimeSpan.FromSeconds(Math.Clamp(wait, 0, 25)), ct).ConfigureAwait(false);
        LinkAlertState[] response = list.Select(a => new LinkAlertState(
            a.Id, a.At.ToString("O", CultureInfo.InvariantCulture), a.Level, a.Title, a.Text)).ToArray();
        await res.WriteJsonAsync(200, new LinkAlertsResponse(response, alerts.LastId), ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ helpers

    /// <summary>The phone sends ISO-8601 with an offset; the PC stores local times.</summary>
    private static DateTime? ParseLocal(string text)
    {
        if (text.Length == 0) return null;
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed) ? parsed.LocalDateTime : null;
    }

    private static string? Iso(DateTime? value) =>
        value == null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Local)).ToString("O", CultureInfo.InvariantCulture);
}
