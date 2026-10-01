using System.IO;
using System.Globalization;
using System.Net.Sockets;
using System.Text.Json;
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
        await res.WriteJsonAsync(404, new { error = "Nie znaleziono." }, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ state

    internal object BuildState()
    {
        SystemSnapshot snapshot = monitor.Current;
        LinkInfo link = info();
        string uptime = FormatUptime(TimeSpan.FromMilliseconds(Environment.TickCount64));
        return new
        {
            pc = new
            {
                name = Environment.MachineName,
                version = link.Version,
                uptime,
                time = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
                mac = LinkNetwork.GetWakeMacs(),
                addresses = addresses()
            },
            metrics = new
            {
                cpu = Num(snapshot.Cpu),
                ramUsed = Num(snapshot.RamUsed),
                ramTotal = Num(snapshot.RamTotal),
                gpu = Num(snapshot.Gpu),
                game = snapshot.Game ?? "",
                network = snapshot.Network ?? "",
                disks = snapshot.Disks.Select(d => new { name = d.Name, usedGb = Num(d.UsedGb), totalGb = Num(d.TotalGb) }).ToArray(),
                processes = snapshot.Processes.Take(8).Select(p => new { name = p.Name, pid = p.Pid, memoryMb = Num(p.MemoryMb, 0), cpu = Num(p.CpuPercent ?? double.NaN) }).ToArray()
            },
            engine = new
            {
                state = link.EngineState,
                message = link.EngineMessage,
                progress = Num(link.EngineProgress, 3),
                model = link.EngineModel,
                installed = link.EngineInstalled
            },
            assistant = new
            {
                busy = engine.IsBusy,
                stopped = engine.IsStopped,
                pending = engine.HasPendingPermission,
                pendingSummary = engine.PermissionSummary
            },
            care = new { ok = link.CareOk, text = link.CareText },
            counts = new
            {
                tasks = tasks.GetTasks().Count,
                reminders = tasks.GetReminders().Count(r => r.NotifiedAt == null),
                notes = memory.NoteCount,
                alerts = alerts.After(0, 200).Count
            },
            alertsLast = alerts.LastId
        };
    }

    private static double? Num(double value, int digits = 1) => double.IsFinite(value) ? Math.Round(value, digits) : null;

    private static string FormatUptime(TimeSpan span) =>
        span.TotalDays >= 1 ? $"{(int)span.TotalDays} d {span.Hours} h" : span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes} min" : $"{span.Minutes} min";

    // ------------------------------------------------------------ chat (Server-Sent Events)

    private async Task ChatAsync(LinkRequest req, LinkResponse res, CancellationToken ct)
    {
        string text = Str(req.Json(), "text").Trim();
        if (text.Length == 0) { await res.WriteJsonAsync(400, new { error = "Wpisz polecenie." }, ct).ConfigureAwait(false); return; }
        if (text.Length > 4000) { await res.WriteJsonAsync(400, new { error = "Polecenie jest za długie (limit 4000 znaków)." }, ct).ConfigureAwait(false); return; }

        await res.StartSseAsync(ct).ConfigureAwait(false);
        await res.WriteSseAsync("start", new { }, ct).ConfigureAwait(false);

        var chunks = Channel.CreateUnbounded<string>();
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
            await res.WriteSseAsync("done", new
            {
                text = result.Text,
                status = record == null ? "" : record.Status.ToString().ToLowerInvariant(),
                evidence = record?.Evidence ?? "",
                elapsedMs = record?.ElapsedMilliseconds ?? 0
            }, ct).ConfigureAwait(false);
        }
        else await res.WriteSseAsync("error", new { error = failure.Length > 0 ? failure : "Błąd." }, ct).ConfigureAwait(false);
    }

    private static async Task PumpAsync(ChannelReader<string> reader, LinkResponse res, CancellationTokenSource cts)
    {
        try
        {
            await foreach (string chunk in reader.ReadAllAsync(cts.Token).ConfigureAwait(false))
                await res.WriteSseAsync("delta", new { text = chunk }, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
        {
            cts.Cancel(); // the phone went away: stop generating for nobody
            throw;
        }
    }

    private async Task ControlAsync(LinkRequest req, LinkResponse res, CancellationToken ct)
    {
        switch (Str(req.Json(), "action"))
        {
            case "cancel": engine.Cancel(); break;
            case "stop": engine.EmergencyStop(); break;
            case "resume": engine.Resume(); break;
            default: await res.WriteJsonAsync(400, new { error = "Nieznana akcja." }, ct).ConfigureAwait(false); return;
        }
        await res.WriteJsonAsync(200, new { ok = true, stopped = engine.IsStopped }, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ tasks and reminders

    private object BuildTasks() => new
    {
        tasks = tasks.GetTasks().Select(t => new
        {
            id = t.Id, title = t.Title, priority = t.Priority, status = t.Status, due = Iso(t.DueAt), project = t.ProjectId
        }).ToArray(),
        reminders = tasks.GetReminders().Where(r => r.NotifiedAt == null || r.Missed).Select(r => new
        {
            id = r.Id, text = r.Text, at = Iso(r.RemindAt), missed = r.Missed
        }).ToArray()
    };

    private async Task AddTaskAsync(LinkRequest req, LinkResponse res, CancellationToken ct)
    {
        JsonElement body = req.Json();
        TaskRecord? created = tasks.AddTask(Str(body, "title"), Str(body, "priority"), ParseLocal(Str(body, "due")), "");
        if (created == null) { await res.WriteJsonAsync(400, new { error = tasks.LastStorageError ?? "Nie udało się dodać zadania." }, ct).ConfigureAwait(false); return; }
        await res.WriteJsonAsync(200, new { ok = true, id = created.Id }, ct).ConfigureAwait(false);
    }

    private async Task TaskStatusAsync(LinkRequest req, LinkResponse res, CancellationToken ct)
    {
        JsonElement body = req.Json();
        bool ok = tasks.SetTaskStatus(Str(body, "id"), Str(body, "status"));
        await res.WriteJsonAsync(ok ? 200 : 404, new { ok }, ct).ConfigureAwait(false);
    }

    private async Task DeleteTaskAsync(LinkRequest req, LinkResponse res, CancellationToken ct)
    {
        bool ok = tasks.DeleteTask(Str(req.Json(), "id"));
        await res.WriteJsonAsync(ok ? 200 : 404, new { ok }, ct).ConfigureAwait(false);
    }

    private async Task AddReminderAsync(LinkRequest req, LinkResponse res, CancellationToken ct)
    {
        JsonElement body = req.Json();
        DateTime? at = ParseLocal(Str(body, "at"));
        ReminderRecord? created = at == null ? null : tasks.AddReminder(Str(body, "text"), at.Value, "");
        if (created == null) { await res.WriteJsonAsync(400, new { error = at == null ? "Podaj termin przypomnienia." : tasks.LastStorageError ?? "Nie udało się dodać przypomnienia." }, ct).ConfigureAwait(false); return; }
        await res.WriteJsonAsync(200, new { ok = true, id = created.Id }, ct).ConfigureAwait(false);
    }

    private async Task DeleteReminderAsync(LinkRequest req, LinkResponse res, CancellationToken ct)
    {
        bool ok = tasks.DeleteReminder(Str(req.Json(), "id"));
        await res.WriteJsonAsync(ok ? 200 : 404, new { ok }, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ notes

    private object BuildNotes(string query) => new
    {
        notes = memory.SearchNotes(query).Where(n => n.SupersededAt == null).Take(100).Select(n => new
        {
            id = n.Id, text = n.Text, category = n.Category, pinned = n.Pinned, at = Iso(n.UpdatedAt ?? n.Timestamp)
        }).ToArray()
    };

    private async Task AddNoteAsync(LinkRequest req, LinkResponse res, CancellationToken ct)
    {
        NoteAddResult result = memory.AddNote(Str(req.Json(), "text"), "notatka", "phone");
        await res.WriteJsonAsync(200, new { ok = result is NoteAddResult.Added or NoteAddResult.StaleDuplicate, result = result.ToString() }, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ alerts

    private async Task AlertsAsync(LinkRequest req, LinkResponse res, CancellationToken ct)
    {
        long.TryParse(req.Q("after"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long after);
        int.TryParse(req.Q("wait"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int wait);
        IReadOnlyList<LinkAlert> list = await alerts.WaitAfterAsync(after, TimeSpan.FromSeconds(Math.Clamp(wait, 0, 25)), ct).ConfigureAwait(false);
        await res.WriteJsonAsync(200, new
        {
            alerts = list.Select(a => new { id = a.Id, at = a.At.ToString("O", CultureInfo.InvariantCulture), level = a.Level, title = a.Title, text = a.Text }).ToArray(),
            last = alerts.LastId
        }, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ helpers

    private static string Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";

    /// <summary>The phone sends ISO-8601 with an offset; the PC stores local times.</summary>
    private static DateTime? ParseLocal(string text)
    {
        if (text.Length == 0) return null;
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed) ? parsed.LocalDateTime : null;
    }

    private static string? Iso(DateTime? value) =>
        value == null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Local)).ToString("O", CultureInfo.InvariantCulture);
}
