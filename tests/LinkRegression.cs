using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SentinelX.Models;
using SentinelX.Services.Actions;
using SentinelX.Services.Care;
using SentinelX.Services.Engine;
using SentinelX.Services.Link;
using SentinelX.Services.Monitoring;
using SentinelX.Services.Settings;

namespace SentinelX.Tests;

/// <summary>The phone link over real TLS on loopback: certificate pinning, pairing with the confirmation code, one-time token, chat as a live
/// stream, tasks, notes, alerts (including long-polling), unpairing and resistance to garbage. The engine is a fake; nothing leaves the machine.</summary>
internal static class LinkRegression
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException("LinkRegression: " + message); }

    private sealed class FakeEngine : IActionEngine
    {
        public bool IsStopped { get; private set; }
        public bool IsBusy => false;
        public ActionRecord? CurrentAction => null;
        public bool HasPendingPermission => false;
        public bool IsStreaming => false;
        public event Action<string>? StreamDelta { add { } remove { } }
        public string PermissionSummary => "";
        public event Action? Changed { add { } remove { } }
        public event Action<ActionRecord>? ActionStarted { add { } remove { } }
        public List<(string Input, bool FromVoice)> Calls { get; } = [];
        public int Cancelled { get; private set; }

        public Task<IntentResult> ExecuteAsync(string input, CancellationToken token = default, bool fromVoice = false, Action<string>? onDelta = null)
        {
            Calls.Add((input, fromVoice));
            if (input.Contains("stream"))
            {
                foreach (string part in new[] { "Jeden ", "dwa ", "trzy" }) onDelta?.Invoke(part);
                return Task.FromResult(new IntentResult("Jeden dwa trzy", new ActionRecord { Status = ActionStatus.Verified, Evidence = "dowód" }));
            }
            return Task.FromResult(new IntentResult("echo: " + input));
        }

        public void Cancel() => Cancelled++;
        public void EmergencyStop() => IsStopped = true;
        public void Resume() => IsStopped = false;
    }

    private sealed class FakeMonitor : ISystemMonitorService
    {
        public SystemSnapshot Current { get; } = new(DateTime.Now, 23.4, 9.1, 15.9, 12, "", "Wi-Fi test", [new("C:\\", 200, 500)], [new("proc", 1, 500, 2.5)]);
        public event Action<SystemSnapshot>? Updated { add { } remove { } }
        public void Start() { }
    }

    private sealed class FakeUi : ILinkApprovalUi
    {
        public PairingRequestInfo? Last { get; private set; }
        public Action<bool>? Decide { get; private set; }
        public int Closed { get; private set; }
        public void ShowRequest(PairingRequestInfo request, Action<bool> decide) { Last = request; Decide = decide; }
        public void CloseRequest(string requestId) => Closed++;
    }

    private static HttpClient Client(LinkService service, string? token = null)
    {
        var handler = new HttpClientHandler
        {
            UseProxy = false,
            // the same pinning the Android app does: trust exactly the certificate whose fingerprint was shown during pairing
            ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
                cert != null && Convert.ToHexString(SHA256.HashData(cert.RawData)).Equals(service.Fingerprint, StringComparison.OrdinalIgnoreCase)
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri($"https://127.0.0.1:{service.Port}/"), Timeout = TimeSpan.FromSeconds(30) };
        if (token != null) http.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return http;
    }

    private static async Task<(int Status, JsonElement Json)> Send(HttpClient http, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await http.SendAsync(request);
        string text = await response.Content.ReadAsStringAsync();
        JsonElement json = default;
        if (text.Length > 0 && text[0] == '{') { using JsonDocument doc = JsonDocument.Parse(text); json = doc.RootElement.Clone(); }
        return ((int)response.StatusCode, json);
    }

    private static Task<(int Status, JsonElement Json)> Get(HttpClient http, string path) => Send(http, HttpMethod.Get, path);
    private static Task<(int Status, JsonElement Json)> Post(HttpClient http, string path, object body) => Send(http, HttpMethod.Post, path, body);

    private static async Task<List<(string Name, JsonElement Data)>> PostSse(HttpClient http, string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        Check(response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "text/event-stream", "the chat answers as a server-sent event stream");
        var events = new List<(string, JsonElement)>();
        await using Stream stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string name = "";
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (line.StartsWith("event:", StringComparison.Ordinal)) name = line[6..].Trim();
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                using JsonDocument doc = JsonDocument.Parse(line[5..].Trim());
                events.Add((name, doc.RootElement.Clone()));
            }
        }
        return events;
    }

    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);

        // ---- pure pieces first
        Check(LinkService.ComputeSas(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray(),
                LinkService.DecodeNonce("oKGio6SlpqeoqaqrrK2urw")!, LinkService.DecodeNonce("sLGys7S1tre4ubq7vL2-vw")!) == "078914",
            "the confirmation code matches the cross-language test vector (C#, JS and Python must agree)");
        Check(LinkService.DecodeNonce("krótki") == null && LinkService.DecodeNonce(new string('A', 30)) == null, "nonces must be exactly 16 bytes");
        foreach (string good in new[] { "10.1.2.3", "172.16.5.4", "172.31.255.1", "192.168.1.20", "100.64.0.9", "169.254.3.3", "127.0.0.1", "::1", "fe80::1" })
            Check(LinkNetwork.IsTrusted(IPAddress.Parse(good)), "trusted network peer: " + good);
        foreach (string bad in new[] { "8.8.8.8", "172.32.0.1", "172.15.0.1", "100.128.0.1", "11.0.0.1", "192.169.0.1", "2001:db8::1" })
            Check(!LinkNetwork.IsTrusted(IPAddress.Parse(bad)), "public address is refused: " + bad);

        var feed = new AlertFeed();
        for (int i = 0; i < 250; i++) feed.Add("info", "t" + i, "x");
        IReadOnlyList<LinkAlert> all = feed.After(0, 500);
        Check(all.Count == 200 && all[0].Id == 51 && all[^1].Id == 250 && feed.LastId == 250, "the alert journal keeps the newest 200 entries in order");
        long before = feed.LastId;
        Task<IReadOnlyList<LinkAlert>> waiting = feed.WaitAfterAsync(before, TimeSpan.FromSeconds(10), default);
        await Task.Delay(150);
        feed.Add("warn", "nowy", "alert");
        Check((await waiting).Count == 1 && feed.After(before)[0].Level == "warn", "long-polling returns as soon as an alert arrives");
        Check((await feed.WaitAfterAsync(feed.LastId, TimeSpan.FromMilliseconds(120), default)).Count == 0, "long-polling times out quietly");

        string deviceDirectory = Path.Combine(directory, "devices");
        var devices = new LinkDeviceStore(deviceDirectory);
        (LinkDeviceInfo device, string token) = devices.Add("Telefon testowy");
        Check(devices.Validate(token)?.Id == device.Id && devices.Validate(token + "x") == null, "a token validates only for the phone it was issued to");
        Check(!File.ReadAllText(Path.Combine(deviceDirectory, "devices.json")).Contains(token), "only the hash of the token is stored on disk");
        Check(new LinkDeviceStore(deviceDirectory).Validate(token)?.Name == "Telefon testowy", "paired phones survive a restart");
        Check(devices.Remove(device.Id) && new LinkDeviceStore(deviceDirectory).Validate(token) == null, "removing a phone revokes its token");
        for (int i = 0; i < 12; i++) devices.Add("Telefon " + i);
        Check(devices.List().Count == 10, "at most ten phones; the least recently used one is replaced");

        string certDirectory = Path.Combine(directory, "cert");
        using (var first = new LinkCertificate(certDirectory))
        using (var second = new LinkCertificate(certDirectory))
            Check(first.Fingerprint == second.Fingerprint && first.Fingerprint.Length == 64, "the certificate is created once and reused (the phone keeps trusting it)");

        // ---- the server, over real TLS
        var alerts = new AlertFeed();
        var ui = new FakeUi();
        var engine = new FakeEngine();
        var tasks = new TaskService(Path.Combine(directory, "tasks"));
        var memory = new ConversationMemoryService(Path.Combine(directory, "memory"));
        var api = new LinkApi(engine, new FakeMonitor(), tasks, memory, alerts,
            () => new LinkInfo("9.9", "ready", "gotowy", 1, "qwen3:1.7b", ["qwen3:1.7b"], true, "Wszystko działa samo"), () => ["https://127.0.0.1/"]);
        using var service = new LinkService(api, ui, () => new LinkSettings { Port = 0, Discovery = false }, Path.Combine(directory, "link"));
        await service.StartAsync(IPAddress.Loopback);
        Check(service.IsRunning && service.Port > 0 && service.Fingerprint.Length == 64, "the link starts and reports its certificate fingerprint");

        // static phone UI, served from the exe itself
        Check(LinkWebAssets.Count >= 8, "the phone UI is embedded in the exe");
        Check(Encoding.UTF8.GetString(LinkWebAssets.Find("/app.js")!.Bytes).Contains("api/pair/request"), "the embedded script is the real phone app");
        using var anonymous = Client(service);
        using (HttpResponseMessage index = await anonymous.GetAsync("/"))
        {
            string html = await index.Content.ReadAsStringAsync();
            Check(index.IsSuccessStatusCode && index.Content.Headers.ContentType?.MediaType == "text/html" && html.Contains("Sentinel X"), "the page is served over HTTPS");
            Check(index.Headers.Contains("Content-Security-Policy") && index.Headers.Contains("X-Frame-Options"), "the page carries its security headers");
            string etag = index.Headers.ETag?.Tag ?? "";
            using var cached = new HttpRequestMessage(HttpMethod.Get, "/");
            cached.Headers.TryAddWithoutValidation("If-None-Match", etag);
            using HttpResponseMessage notModified = await anonymous.SendAsync(cached);
            Check(etag.Length > 0 && notModified.StatusCode == HttpStatusCode.NotModified, "unchanged assets are answered with 304");
        }
        Check((await anonymous.GetAsync("/nie-ma.js")).StatusCode == HttpStatusCode.NotFound, "an unknown file is a 404");
        Check((await Get(anonymous, "api/hello")).Json.GetProperty("fingerprint").GetString() == service.Fingerprint, "the hello answer shows the pinned fingerprint");
        using var wrongToken = Client(service, "zly-token");
        Check((await Get(anonymous, "api/state")).Status == 401 && (await Get(wrongToken, "api/state")).Status == 401, "no token or a wrong token is refused");

        // ---- pairing: request → code on the PC → one click → token, once
        byte[] fingerprint = Convert.FromHexString(service.Fingerprint);
        string clientNonce = LinkDeviceStore.Base64Url(RandomNumberGenerator.GetBytes(16));
        var request = await Post(anonymous, "api/pair/request", new { device = "Telefon z testu", nonce = clientNonce });
        Check(request.Status == 200, "a phone may ask to be paired");
        string id = request.Json.GetProperty("id").GetString()!;
        string serverNonce = request.Json.GetProperty("nonce").GetString()!;
        string sas = request.Json.GetProperty("sas").GetString()!;
        string expected = LinkService.ComputeSas(fingerprint, LinkService.DecodeNonce(clientNonce)!, LinkService.DecodeNonce(serverNonce)!);
        Check(sas == expected && ui.Last is { } shown && shown.Sas == expected && shown.DeviceName == "Telefon z testu",
            "the window on the PC shows the same code the phone derives itself");
        Check((await Post(anonymous, "api/pair/request", new { device = "Drugi", nonce = clientNonce })).Status == 429, "a second request is refused while one is waiting for the user");
        Check((await Get(anonymous, "api/pair/status?id=" + id)).Json.GetProperty("state").GetString() == "pending", "the request waits for the click");
        ui.Decide!(true);
        var approved = await Get(anonymous, "api/pair/status?id=" + id);
        Check(approved.Json.GetProperty("state").GetString() == "approved", "after the click the phone is approved");
        string phoneToken = approved.Json.GetProperty("token").GetString()!;
        Check((await Get(anonymous, "api/pair/status?id=" + id)).Json.GetProperty("state").GetString() == "expired", "the token is handed over exactly once");

        var deniedRequest = await Post(anonymous, "api/pair/request", new { device = "Obcy", nonce = LinkDeviceStore.Base64Url(RandomNumberGenerator.GetBytes(16)) });
        Check(deniedRequest.Status == 200, "a new request is possible after the previous one was answered");
        ui.Decide!(false);
        Check((await Get(anonymous, "api/pair/status?id=" + deniedRequest.Json.GetProperty("id").GetString())).Json.GetProperty("state").GetString() == "denied", "a refused request stays refused");
        Check((await Post(anonymous, "api/pair/request", new { device = "x", nonce = "zly" })).Status == 400, "a malformed request is rejected");

        // ---- the paired phone
        using var phone = Client(service, phoneToken);
        var state = await Get(phone, "api/state");
        Check(state.Status == 200 && Math.Abs(state.Json.GetProperty("metrics").GetProperty("cpu").GetDouble() - 23.4) < 0.01
              && state.Json.GetProperty("pc").GetProperty("version").GetString() == "9.9" && state.Json.GetProperty("engine").GetProperty("state").GetString() == "ready",
            "the state shows measurements, the engine and the version");

        List<(string Name, JsonElement Data)> events = await PostSse(phone, "api/chat", new { text = "daj strumień" });
        Check(events[0].Name == "start" && events.Count(e => e.Name == "delta") == 3 && events[^1].Name == "done", "chat streams start, deltas and a final answer");
        Check(events[^1].Data.GetProperty("text").GetString() == "Jeden dwa trzy" && events[^1].Data.GetProperty("status").GetString() == "verified", "the final answer carries its proof status");
        Check(engine.Calls[^1].FromVoice, "commands from the phone are treated like voice: risky actions can only be confirmed on the PC");
        List<(string Name, JsonElement Data)> plain = await PostSse(phone, "api/chat", new { text = "ile ram" });
        Check(plain[^1].Name == "done" && plain[^1].Data.GetProperty("text").GetString() == "echo: ile ram", "a deterministic command answers in one piece");
        Check((await Post(phone, "api/chat", new { text = "  " })).Status == 400, "an empty message is refused");

        Check((await Post(phone, "api/control", new { action = "stop" })).Json.GetProperty("stopped").GetBoolean() && engine.IsStopped, "the phone can trigger the emergency stop");
        Check(!(await Post(phone, "api/control", new { action = "resume" })).Json.GetProperty("stopped").GetBoolean(), "and resume");

        var added = await Post(phone, "api/tasks", new { title = "Kupić mleko", priority = "wysoki", due = DateTimeOffset.Now.AddDays(1).ToString("O") });
        Check(added.Status == 200, "a task can be added from the phone");
        string taskId = added.Json.GetProperty("id").GetString()!;
        Check((await Get(phone, "api/tasks")).Json.GetProperty("tasks").EnumerateArray().Any(t => t.GetProperty("id").GetString() == taskId && t.GetProperty("priority").GetString() == "wysoki"), "it shows up in the same task list as on the PC");
        Check(tasks.GetTasks().Any(t => t.Id == taskId), "the PC's task service holds it");
        await Post(phone, "api/tasks/status", new { id = taskId, status = "zrobione" });
        Check(!(await Get(phone, "api/tasks")).Json.GetProperty("tasks").EnumerateArray().Any(t => t.GetProperty("id").GetString() == taskId), "a finished task leaves the list");
        Check((await Post(phone, "api/reminders", new { text = "za późno", at = DateTimeOffset.Now.AddMinutes(-5).ToString("O") })).Status == 400, "a reminder in the past is refused with the PC's own reason");
        var reminder = await Post(phone, "api/reminders", new { text = "Odebrać paczkę", at = DateTimeOffset.Now.AddHours(2).ToString("O") });
        Check(reminder.Status == 200 && (await Get(phone, "api/tasks")).Json.GetProperty("reminders").GetArrayLength() == 1, "a future reminder is accepted");
        await Post(phone, "api/reminders/delete", new { id = reminder.Json.GetProperty("id").GetString() });
        Check((await Get(phone, "api/tasks")).Json.GetProperty("reminders").GetArrayLength() == 0, "and can be deleted");

        Check((await Post(phone, "api/notes", new { text = "Testowa notatka z telefonu 123" })).Json.GetProperty("result").GetString() == "Added", "a note can be saved from the phone");
        Check((await Post(phone, "api/notes", new { text = "Testowa notatka z telefonu 123" })).Json.GetProperty("result").GetString() == "Duplicate", "a duplicate is recognised");
        Check((await Get(phone, "api/notes?q=telefonu")).Json.GetProperty("notes").GetArrayLength() == 1 && memory.GetNotes().Any(n => n.Source == "phone"), "notes are searchable and marked as coming from the phone");

        alerts.Add("warn", "Wysokie obciążenie", "CPU 97%");
        Check((await Get(phone, "api/alerts?after=0")).Json.GetProperty("alerts").EnumerateArray().Any(a => a.GetProperty("title").GetString() == "Wysokie obciążenie"), "alerts reach the phone");
        long last = alerts.LastId;
        Task<(int Status, JsonElement Json)> longPoll = Get(phone, $"api/alerts?after={last}&wait=15");
        await Task.Delay(250);
        alerts.Add("info", "Przypomnienie", "Odebrać paczkę");
        Task finished = await Task.WhenAny(longPoll, Task.Delay(TimeSpan.FromSeconds(8)));
        Check(ReferenceEquals(finished, longPoll) && longPoll.Result.Json.GetProperty("alerts")[0].GetProperty("title").GetString() == "Przypomnienie", "a waiting phone is answered the moment an alert is raised");

        // ---- robustness: garbage must not hurt the service
        using (var raw = new TcpClient())
        {
            await raw.ConnectAsync(IPAddress.Loopback, service.Port);
            await raw.GetStream().WriteAsync(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\n\r\n")); // plain text to a TLS port
        }
        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, service.Port);
            using var tls = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
            await tls.AuthenticateAsClientAsync("127.0.0.1");
            await tls.WriteAsync(Encoding.ASCII.GetBytes("GET /" + new string('a', 20000) + " HTTP/1.1\r\n"));
            await tls.FlushAsync();
            byte[] sink = new byte[64];
            int read;
            try { read = await tls.ReadAsync(sink); }
            catch (IOException) { read = 0; }
            Check(read == 0, "an oversized request head is dropped without an answer");
        }
        Check((await Get(anonymous, "api/hello")).Status == 200, "the service is still healthy after garbage");

        // ---- unpairing
        var listed = await Get(phone, "api/devices");
        Check(listed.Json.GetProperty("devices").EnumerateArray().Any(d => d.GetProperty("current").GetBoolean() && d.GetProperty("name").GetString() == "Telefon z testu"), "the phone sees itself in the list");
        Check((await Post(phone, "api/unpair", new { })).Status == 200 && (await Get(phone, "api/state")).Status == 401, "after unpairing the token stops working");
        Check(service.Devices.All(d => d.Name != "Telefon z testu"), "the PC forgot the phone");

        // ---- autopilot: switched on once for an existing settings file, never again after the user decides
        string settingsDirectory = Path.Combine(directory, "settings");
        var settingsStore = new AppSettingsService(settingsDirectory);
        settingsStore.Settings.Startup.StartWithWindows = false;
        settingsStore.Settings.Watch.Enabled = false;
        settingsStore.Settings.Startup.AutopilotApplied = false;
        settingsStore.Save();
        using var engineService = new EngineService(new EngineStore(Path.Combine(directory, "care-engine"), Path.Combine(directory, "care-bundled")), new EngineDownloader(), () => false, () => 8);
        using var care = new CareService(new SettingsService(settingsStore), service, engineService, alerts, tasks);
        care.ApplyAutopilotOnce();
        Check(settingsStore.Settings.Startup.StartWithWindows && settingsStore.Settings.Watch.Enabled && settingsStore.Settings.Link.Enabled, "the autopilot switches on start-with-Windows, load watch and the phone link");
        Check(new AppSettingsService(settingsDirectory).Settings.Startup.AutopilotApplied, "the one-time flag is saved");
        settingsStore.Settings.Startup.StartWithWindows = false;
        settingsStore.Save();
        care.ApplyAutopilotOnce();
        Check(!settingsStore.Settings.Startup.StartWithWindows, "after the user's own choice the autopilot never overrides it");
        Check(care.BuildLinkInfo().Version == SentinelX.Core.AppConstants.Version, "the phone gets the real app version");

        // ---- the phone section of the settings
        var fields = SettingsCatalog.Create(settingsStore);
        var phoneSwitch = fields.Single(f => f.Section == "Telefon" && f.Label == "Połączenie z telefonem");
        Check(phoneSwitch.Write("False") == null && !settingsStore.Settings.Link.Enabled, "the phone link can be switched off in the settings");
        settingsStore.ResetSection("Telefon");
        Check(settingsStore.Settings.Link.Enabled && settingsStore.Settings.Link.Port == 43180, "resetting the phone section restores the defaults");
        Check(fields.Any(f => f.Section == "AI" && f.Label == "Pobieraj silnik AI automatycznie"), "automatic engine downloads can be switched off");

        await service.StopAsync();
        Check(!service.IsRunning, "the link stops cleanly");
    }
}
