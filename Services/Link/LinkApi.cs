using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Threading.Channels;
using SentinelX.Core;
using SentinelX.Models;
using SentinelX.Services.Actions;
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

    public LinkApi(IActionEngine engine, ISystemMonitorService monitor, TaskService tasks, ConversationMemoryService memory,
        AlertFeed alerts, Func<LinkInfo> info, Func<IReadOnlyList<string>> addresses)
    {
        this.engine = engine;
        this.monitor = monitor;
        this.tasks = tasks;
        this.memory = memory;
        this.alerts = alerts;
        this.info = info;
        this.addresses = addresses;
    }

    public async Task HandleAsync(LinkRequest req, LinkResponse res, CancellationToken ct)
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
                Game: snapshot.Game ?? "",
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
