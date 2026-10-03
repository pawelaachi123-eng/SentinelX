using System.IO;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SentinelX.Core;
using SentinelX.Models;
using SentinelX.Services.Actions;
using SentinelX.Services.Automation;
using SentinelX.Services.Care;
using SentinelX.Services.Engine;
using SentinelX.Services.Link;
using SentinelX.Services.Monitoring;
using SentinelX.Services.Notifications;
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
            if (input.Contains("big stream"))
            {
                const int answerLength = 300;
                for (int i = 0; i < answerLength; i++) onDelta?.Invoke("x");
                return Task.FromResult(new IntentResult(new string('x', answerLength)));
            }
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
        public SystemSnapshot Current { get; set; } = new(DateTime.Now, 23.4, 9.1, 15.9, 12, "", "Wi-Fi test", [new("C:\\", 200, 500)], [new("proc", 1, 500, 2.5)]);
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

    private sealed class FakeNotifications : INotificationService
    {
        public event Action<AppNotification>? Published;
        public AppNotification Publish(string category, string title, string message, string severity = "info")
        {
            var item = new AppNotification(DateTimeOffset.UtcNow, severity, category, title, message);
            Published?.Invoke(item);
            return item;
        }
    }

    private sealed class FakeAutomationAction : IAutomationActionHandler
    {
        public AutomationActionDescriptor Descriptor { get; } = new("test-action", "Test action", "Tests", "Deterministic fake.", "None");
        public int Calls { get; private set; }
        public bool TryValidate(string? parameter, out string normalized, out string error)
        {
            normalized = (parameter ?? "").Trim();
            if (normalized == "approved") { error = ""; return true; }
            normalized = ""; error = "Only the explicit test parameter is accepted."; return false;
        }
        public Task<AutomationActionResult> ExecuteAsync(string normalizedParameter, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(new AutomationActionResult(true, "VERIFIED", "fake action executed", "test evidence"));
        }
    }

    private sealed class FakePcSystemActions : IPcSystemActions
    {
        public bool IsSupported => true;
        public List<string> Calls { get; } = [];
        public bool TryExecute(string action, out string message)
        {
            Calls.Add(action); message = "fake OS action accepted"; return true;
        }
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

    private static string NewRequestId() => LinkDeviceStore.Base64Url(RandomNumberGenerator.GetBytes(24));

    private static async Task<(int Status, JsonElement Json)> Send(HttpClient http, HttpMethod method, string path, object? body = null, string? requestId = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (method.Method is "POST" or "PUT" or "DELETE")
            request.Headers.TryAddWithoutValidation("X-Sentinel-Request-Id", requestId ?? NewRequestId());
        if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await http.SendAsync(request);
        string text = await response.Content.ReadAsStringAsync();
        JsonElement json = default;
        if (text.Length > 0 && text[0] == '{') { using JsonDocument doc = JsonDocument.Parse(text); json = doc.RootElement.Clone(); }
        return ((int)response.StatusCode, json);
    }

    private static Task<(int Status, JsonElement Json)> Get(HttpClient http, string path) => Send(http, HttpMethod.Get, path);
    private static Task<(int Status, JsonElement Json)> Post(HttpClient http, string path, object body, string? requestId = null) =>
        Send(http, HttpMethod.Post, path, body, requestId);

    private static async Task<List<(string Name, JsonElement Data)>> PostSse(HttpClient http, string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        request.Headers.TryAddWithoutValidation("X-Sentinel-Request-Id", NewRequestId());
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
        var hostileAlert = feed.Add("warn", "Title" + (char)13 + (char)10 + new string('x', 130), "Body" + (char)13 + (char)10 + new string('y', 700));
        Check(hostileAlert.Title.Length == 120 && hostileAlert.Text.Length == 600 &&
              !hostileAlert.Title.Any(char.IsControl) && !hostileAlert.Text.Any(char.IsControl),
            "phone notifications strip control characters and enforce field-size limits at the journal boundary");

        string deviceDirectory = Path.Combine(directory, "devices");
        var devices = new LinkDeviceStore(deviceDirectory);
        var reportedCapabilities = new LinkPhoneCapabilities(Notifications: true, VoiceInput: false, WakeOnLan: true);
        (LinkDeviceInfo device, string token) = devices.Add("Telefon testowy", reportedCapabilities);
        Check(devices.Validate(token)?.Id == device.Id && devices.Validate(token + "x") == null, "a token validates only for the phone it was issued to");
        Check(!File.ReadAllText(Path.Combine(deviceDirectory, "devices.json")).Contains(token), "only the hash of the token is stored on disk");
        Check(new LinkDeviceStore(deviceDirectory).Validate(token)?.Name == "Telefon testowy"
              && new LinkDeviceStore(deviceDirectory).Validate(token)?.Capabilities == reportedCapabilities,
            "paired phones and their informational capabilities survive a restart");
        string? rotated = devices.RotateToken(device.Id, out string rotateError);
        Check(rotated != null && rotateError.Length == 0 && devices.Validate(token) == null && devices.Validate(rotated)?.Id == device.Id
              && new LinkDeviceStore(deviceDirectory).Validate(rotated)?.Id == device.Id,
            "token rotation persists a fresh credential and immediately invalidates the old one");
        Check(devices.Remove(device.Id) && new LinkDeviceStore(deviceDirectory).Validate(rotated!) == null, "removing a phone revokes its rotated token");

        string idleDirectory = Path.Combine(directory, "idle-devices");
        var idleDevices = new LinkDeviceStore(idleDirectory);
        (LinkDeviceInfo idleDevice, string idleToken) = idleDevices.Add("Telefon nieużywany");
        Check(idleDevices.Validate(idleToken, DateTimeOffset.UtcNow.AddDays(91)) == null && idleDevices.List().All(x => x.Id != idleDevice.Id)
              && new LinkDeviceStore(idleDirectory).List().Count == 0,
            "paired sessions expire after 90 days of inactivity and are durably removed before authentication");

        string legacyDeviceDirectory = Path.Combine(directory, "legacy-devices");
        Directory.CreateDirectory(legacyDeviceDirectory);
        string legacyTimestamp = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        File.WriteAllText(Path.Combine(legacyDeviceDirectory, "devices.json"),
            "[{\"Id\":\"legacy\",\"Name\":\"Stary telefon\",\"TokenHash\":\"" + new string('a', 64) +
            "\",\"AddedAt\":\"" + legacyTimestamp + "\",\"LastSeen\":\"" + legacyTimestamp + "\"}]");
        Check(new LinkDeviceStore(legacyDeviceDirectory).List().Single().Capabilities == LinkPhoneCapabilities.None,
            "a pre-capabilities device store remains readable after upgrade");

        for (int i = 0; i < 12; i++) devices.Add("Telefon " + i);
        Check(devices.List().Count == 10, "at most ten phones; the least recently used one is replaced");

        string blockedRoot = Path.Combine(directory, "not-a-directory");
        if (Directory.Exists(blockedRoot)) Directory.Delete(blockedRoot, true);
        if (File.Exists(blockedRoot)) File.Delete(blockedRoot);
        File.WriteAllText(blockedRoot, "test fixture");
        var blockedStore = new LinkDeviceStore(Path.Combine(blockedRoot, "nested"));
        bool addFailed = false;
        try { blockedStore.Add("Niezapisany telefon"); }
        catch (IOException) { addFailed = true; }
        Check(addFailed && blockedStore.List().Count == 0 && blockedStore.LastError != null,
            "a phone is not paired or issued a bearer token when the credential store cannot be written");

        string revocationRoot = Path.Combine(directory, "revocation");
        string preservedDeviceDirectory = revocationRoot + ".preserved";
        if (Directory.Exists(revocationRoot)) Directory.Delete(revocationRoot, true);
        if (File.Exists(revocationRoot)) File.Delete(revocationRoot);
        if (Directory.Exists(preservedDeviceDirectory)) Directory.Delete(preservedDeviceDirectory, true);
        if (File.Exists(preservedDeviceDirectory)) File.Delete(preservedDeviceDirectory);
        var revocationStore = new LinkDeviceStore(revocationRoot);
        (LinkDeviceInfo revocable, string revocationToken) = revocationStore.Add("Telefon do odłączenia");
        Directory.Move(revocationRoot, preservedDeviceDirectory);
        File.WriteAllText(revocationRoot, "test fixture blocks the store directory");
        Check(!revocationStore.Remove(revocable.Id) && revocationStore.Validate(revocationToken)?.Id == revocable.Id && revocationStore.LastError != null,
            "failed credential persistence is surfaced instead of reporting a durable revocation");
        Check(new LinkDeviceStore(preservedDeviceDirectory).Validate(revocationToken)?.Id == revocable.Id,
            "the old credential remains explicitly recoverable after a failed revocation write");
        Check(!revocationStore.RemoveAll() && revocationStore.List().Count == 1,
            "disconnect-all also reports a failed durable write and preserves the in-memory view");

        string corruptDeviceDirectory = Path.Combine(directory, "corrupt-devices");
        if (Directory.Exists(corruptDeviceDirectory)) Directory.Delete(corruptDeviceDirectory, true);
        Directory.CreateDirectory(corruptDeviceDirectory);
        File.WriteAllText(Path.Combine(corruptDeviceDirectory, "devices.json"), "{ invalid");
        var corruptStore = new LinkDeviceStore(corruptDeviceDirectory);
        Check(corruptStore.LastError != null && Directory.GetFiles(corruptDeviceDirectory, "devices.json.corrupt-*").Length == 1,
            "an unreadable credential store is quarantined instead of silently replaced");
        (LinkDeviceInfo recoveredDevice, string recoveredToken) = corruptStore.Add("Nowy telefon");
        Check(new LinkDeviceStore(corruptDeviceDirectory).Validate(recoveredToken)?.Id == recoveredDevice.Id,
            "a fresh pairing works after the damaged credential file has been preserved");

        string oversizedDeviceDirectory = Path.Combine(directory, "oversized-devices");
        if (Directory.Exists(oversizedDeviceDirectory)) Directory.Delete(oversizedDeviceDirectory, true);
        Directory.CreateDirectory(oversizedDeviceDirectory);
        File.WriteAllText(Path.Combine(oversizedDeviceDirectory, "devices.json"), new string('x', 256 * 1024 + 1));
        var oversizedStore = new LinkDeviceStore(oversizedDeviceDirectory);
        Check(oversizedStore.LastError != null && Directory.GetFiles(oversizedDeviceDirectory, "devices.json.corrupt-*").Length == 1,
            "an oversized device store is detected and preserved before any new credentials can overwrite it");

        string certDirectory = Path.Combine(directory, "cert");
        using (var first = new LinkCertificate(certDirectory))
        using (var second = new LinkCertificate(certDirectory))
            Check(first.Fingerprint == second.Fingerprint && first.Fingerprint.Length == 64, "the certificate is created once and reused (the phone keeps trusting it)");

        // ---- the server, over real TLS
        var alerts = new AlertFeed();
        var ui = new FakeUi();
        var engine = new FakeEngine();
        var fakeMonitor = new FakeMonitor();
        var tasks = new TaskService(Path.Combine(directory, "tasks"));
        var memory = new ConversationMemoryService(Path.Combine(directory, "memory"));
        var fakeAutomationAction = new FakeAutomationAction();
        var automationRegistry = new AutomationActionRegistry([fakeAutomationAction]);
        using var automations = new AutomationService(automationRegistry, new FakeNotifications(), Path.Combine(directory, "http-automations"));
        using var fileTransfers = new PhoneFileTransferService(Path.Combine(directory, "http-file-transfers"));
        var fakePcActions = new FakePcSystemActions();
        var api = new LinkApi(engine, fakeMonitor, tasks, memory, alerts,
            () => new LinkInfo("9.9", "ready", "gotowy", 1, "qwen3:1.7b", ["qwen3:1.7b"], true, "Wszystko działa samo"),
            () => ["https://127.0.0.1/"], automations, fileTransfers, fakePcActions);
        string linkDirectory = Path.Combine(directory, "link");
        if (Directory.Exists(linkDirectory)) Directory.Delete(linkDirectory, true);
        if (File.Exists(linkDirectory)) File.Delete(linkDirectory);
        using var service = new LinkService(api, ui, () => new LinkSettings { Port = 0, Discovery = false }, linkDirectory);
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
        JsonElement hello = (await Get(anonymous, "api/hello")).Json;
        Check(hello.GetProperty("fingerprint").GetString() == service.Fingerprint && hello.GetProperty("api").GetInt32() == LinkProtocol.CurrentVersion,
            "the typed hello contract retains API v1 and the pinned fingerprint");
        Check(hello.GetProperty("version").GetString() == AppConstants.Version
              && hello.GetProperty("capabilities").EnumerateArray().Any(x => x.GetString() == "assistantControl")
              && hello.GetProperty("capabilities").EnumerateArray().Any(x => x.GetString() == "fileTransfer")
              && hello.GetProperty("capabilities").EnumerateArray().Any(x => x.GetString() == "automations")
              && hello.GetProperty("capabilities").EnumerateArray().Any(x => x.GetString() == "powerControl"),
            "the hello contract reports the current app version and implemented, not placeholder, controller capabilities");
        using var wrongToken = Client(service, "zly-token");
        Check((await Get(anonymous, "api/state")).Status == 401 && (await Get(wrongToken, "api/state")).Status == 401, "no token or a wrong token is refused");

        string legacyNonce = LinkDeviceStore.Base64Url(RandomNumberGenerator.GetBytes(16));
        var legacyPairRequest = await Post(anonymous, "api/pair/request", new { device = "Klient API v1", nonce = legacyNonce });
        Check(legacyPairRequest.Status == 200 && ui.Last?.Capabilities == LinkPhoneCapabilities.None,
            "an API v1 client that omits the additive capabilities field can still request pairing");
        string legacyPairId = legacyPairRequest.Json.GetProperty("id").GetString()!;
        ui.Decide!(false);
        Check((await Get(anonymous, "api/pair/status?id=" + legacyPairId)).Json.GetProperty("state").GetString() == "denied",
            "the legacy pairing request follows the existing PC consent flow");

        // ---- pairing: request → code on the PC → one click → token, once
        byte[] fingerprint = Convert.FromHexString(service.Fingerprint);
        string clientNonce = LinkDeviceStore.Base64Url(RandomNumberGenerator.GetBytes(16));
        string[] phoneCapabilities = ["notifications", "wakeOnLan", "futureCapability"];
        var request = await Post(anonymous, "api/pair/request", new { device = "Telefon z testu", nonce = clientNonce, capabilities = phoneCapabilities });
        Check(request.Status == 200, "a phone may ask to be paired");
        string id = request.Json.GetProperty("id").GetString()!;
        string serverNonce = request.Json.GetProperty("nonce").GetString()!;
        string sas = request.Json.GetProperty("sas").GetString()!;
        string expected = LinkService.ComputeSas(fingerprint, LinkService.DecodeNonce(clientNonce)!, LinkService.DecodeNonce(serverNonce)!);
        Check(sas == expected && ui.Last is { } shown && shown.Sas == expected && shown.DeviceName == "Telefon z testu"
              && shown.Capabilities == new LinkPhoneCapabilities(Notifications: true, VoiceInput: false, WakeOnLan: true),
            "the typed pairing consent shows the shared code and validated informational phone capabilities");
        Check((await Post(anonymous, "api/pair/request", new { device = "Drugi", nonce = clientNonce })).Status == 429, "a second request is refused while one is waiting for the user");
        Check((await Get(anonymous, "api/pair/status?id=" + id)).Json.GetProperty("state").GetString() == "pending", "the request waits for the click");
        string blockedDevicePath = Path.Combine(linkDirectory, "devices.json");
        Directory.CreateDirectory(blockedDevicePath); // Make the final rename fail after the one-time token has been generated.
        ui.Decide!(true);
        var persistenceDenied = await Get(anonymous, "api/pair/status?id=" + id);
        Check(persistenceDenied.Json.GetProperty("state").GetString() == "denied" && !persistenceDenied.Json.TryGetProperty("token", out _)
              && service.Devices.Count == 0 && service.DeviceStoreError != null,
            "a pairing consent cannot issue a bearer token unless its hash is durably persisted");
        Directory.Delete(blockedDevicePath);

        var retryRequest = await Post(anonymous, "api/pair/request", new { device = "Telefon z testu", nonce = clientNonce, capabilities = phoneCapabilities });
        Check(retryRequest.Status == 200, "pairing can be retried after the credential store is repaired");
        id = retryRequest.Json.GetProperty("id").GetString()!;
        ui.Decide!(true);
        var approved = await Get(anonymous, "api/pair/status?id=" + id);
        Check(approved.Json.GetProperty("state").GetString() == "approved", "after durable storage and the click the phone is approved");
        string phoneToken = approved.Json.GetProperty("token").GetString()!;
        Check(service.Devices.Single().Capabilities == new LinkPhoneCapabilities(Notifications: true, VoiceInput: false, WakeOnLan: true),
            "the PC stores only allowlisted, non-authorizing companion capability hints");
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
            "the typed state contract shows measurements, the engine and the version");
        fakeMonitor.Current = fakeMonitor.Current with { GamingDetectionAvailable = false };
        var unavailableGameState = await Get(phone, "api/state");
        Check(unavailableGameState.Json.GetProperty("metrics").GetProperty("game").GetString() == "Niedostępne",
            "the paired phone does not present unavailable game detection as a confirmed no-game result");
        Check(state.Json.GetProperty("capabilities").EnumerateArray().Any(x => x.GetString() == "notes")
              && state.Json.GetProperty("capabilities").EnumerateArray().Any(x => x.GetString() == "fileTransfer")
              && !state.Json.GetProperty("capabilities").EnumerateArray().Any(x => x.GetString() == "wakeOnLan"),
            "the PC capability list describes real host endpoints and does not claim phone-only functions");

        // ---- API mutation replay protection: one request ID is consumed durably per paired device.
        string replayId = NewRequestId();
        Check((await Send(phone, HttpMethod.Post, "api/control", new { action = "stop" }, replayId)).Status == 200
              && (await Send(phone, HttpMethod.Post, "api/control", new { action = "stop" }, replayId)).Status == 409
              && new LinkReplayGuard(linkDirectory).TryConsume(service.Devices.Single().Id, replayId) == ReplayDecision.Replayed,
            "authenticated changes reject a repeated request ID and retain the replay decision on disk");
        engine.Resume();

        // ---- automation API uses only registered, typed actions.
        var automationList = await Get(phone, "api/automations");
        Check(automationList.Status == 200 && automationList.Json.GetProperty("available").GetBoolean()
              && automationList.Json.GetProperty("actions").EnumerateArray().Any(x => x.GetProperty("id").GetString() == "test-action"),
            "the phone sees the actual allowlisted PC automation catalog");
        var rejectedAutomation = await Post(phone, "api/automations/save", new
        {
            name = "Niebezpieczna", trigger = "manual", scheduleTime = "", enabled = false,
            actions = new[] { new { actionId = "shell", parameter = "cmd.exe /c calc" } }
        });
        Check(rejectedAutomation.Status == 400 && fakeAutomationAction.Calls == 0,
            "the phone API rejects shell-like automation actions without executing them");
        var savedAutomation = await Post(phone, "api/automations/save", new
        {
            name = "Test z telefonu", trigger = "manual", scheduleTime = "", enabled = false,
            actions = new[] { new { actionId = "test-action", parameter = "approved" } }
        });
        Check(savedAutomation.Status == 200, "a valid rule is validated and saved by the existing automation service");
        string ruleId = savedAutomation.Json.GetProperty("rule").GetProperty("id").GetString()!;
        Check((await Post(phone, "api/automations/run", new { id = ruleId })).Json.GetProperty("status").GetString() == "SUCCESS"
              && fakeAutomationAction.Calls == 1,
            "a user-requested phone run executes only the validated registered action and records success");
        Check((await Get(phone, "api/automations")).Json.GetProperty("history").GetArrayLength() == 1,
            "automation history is available to the controller after execution");
        Check((await Post(phone, "api/automations/delete", new { id = ruleId })).Status == 200,
            "the phone can delete its saved rule through the typed service API");
        Check((await Post(phone, "api/automations/run", new { id = ruleId }, "missing-request-id")).Status == 400,
            "a side-effecting automation run requires a fresh request ID");

        // ---- bounded file transfer: ordered chunks, owner scope, hash check, cancellation and safe storage.
        byte[] uploadContent = new byte[PhoneFileTransferService.ChunkBytes + 137];
        RandomNumberGenerator.Fill(uploadContent);
        string uploadHash = Convert.ToHexString(SHA256.HashData(uploadContent)).ToLowerInvariant();
        Check((await Post(phone, "api/files/upload/start", new { name = "../escape.txt", size = 1, sha256 = uploadHash })).Status == 400,
            "file transfer rejects path traversal before creating a destination");
        var startedUpload = await Post(phone, "api/files/upload/start", new { name = "capture.bin", size = uploadContent.LongLength, sha256 = uploadHash });
        Check(startedUpload.Status == 200 && startedUpload.Json.GetProperty("chunkBytes").GetInt32() == PhoneFileTransferService.ChunkBytes,
            "the PC starts only bounded, chunked transfers");
        string uploadId = startedUpload.Json.GetProperty("id").GetString()!;
        var firstChunk = await Post(phone, "api/files/upload/chunk", new
        {
            id = uploadId, index = 0, data = Convert.ToBase64String(uploadContent, 0, PhoneFileTransferService.ChunkBytes)
        });
        Check(firstChunk.Status == 200 && !firstChunk.Json.GetProperty("done").GetBoolean(), "upload progress advances one ordered chunk at a time");
        var finalChunk = await Post(phone, "api/files/upload/chunk", new
        {
            id = uploadId, index = 1, data = Convert.ToBase64String(uploadContent, PhoneFileTransferService.ChunkBytes, 137)
        });
        string fileId = finalChunk.Json.GetProperty("file").GetProperty("id").GetString()!;
        Check(finalChunk.Status == 200 && finalChunk.Json.GetProperty("done").GetBoolean()
              && fileTransfers.List("not-this-phone").Count == 0
              && File.Exists(Path.Combine(fileTransfers.FilesDirectory, fileId + ".bin")),
            "a completed transfer is indexed under an opaque ID in the dedicated store and isolated by phone owner");
        using (HttpResponseMessage downloadResponse = await phone.GetAsync("api/files/download?id=" + Uri.EscapeDataString(fileId)))
        {
            byte[] downloaded = await downloadResponse.Content.ReadAsByteArrayAsync();
            Check(downloadResponse.IsSuccessStatusCode && downloadResponse.Content.Headers.ContentType?.MediaType == "application/octet-stream"
                  && downloaded.SequenceEqual(uploadContent) && Convert.ToHexString(SHA256.HashData(downloaded)).Equals(uploadHash, StringComparison.OrdinalIgnoreCase),
                "the authenticated download streams exact content whose SHA-256 matches the source");
        }
        byte[] corruptContent = Encoding.UTF8.GetBytes("integrity check");
        var corruptUpload = await Post(phone, "api/files/upload/start", new { name = "tampered.txt", size = corruptContent.LongLength, sha256 = new string('0', 64) });
        string corruptId = corruptUpload.Json.GetProperty("id").GetString()!;
        var integrityFailure = await Post(phone, "api/files/upload/chunk", new { id = corruptId, index = 0, data = Convert.ToBase64String(corruptContent) });
        Check(integrityFailure.Status == 422 && fileTransfers.List(service.Devices.Single().Id).Count == 1,
            "a hash mismatch is rejected and never appears in the user's file list");
        var pendingUpload = await Post(phone, "api/files/upload/start", new { name = "cancel-me.bin", size = 50, sha256 = Convert.ToHexString(SHA256.HashData(new byte[50])) });
        string pendingId = pendingUpload.Json.GetProperty("id").GetString()!;
        Check((await Post(phone, "api/files/upload/cancel", new { id = pendingId })).Status == 200
              && !File.Exists(Path.Combine(fileTransfers.FilesDirectory, pendingId + ".bin")),
            "cancel removes an incomplete file without exposing it to downloads");
        Check((await Post(phone, "api/files/delete", new { id = fileId })).Status == 200
              && (await Get(phone, "api/files")).Json.GetProperty("files").GetArrayLength() == 0,
            "the owner can remove a completed transfer from its history");

        // ---- sensitive PC power requests need a fresh, one-use, device-bound confirmation challenge.
        var preparedPower = await Post(phone, "api/power/prepare", new { action = "shutdown" });
        string powerChallenge = preparedPower.Json.GetProperty("challenge").GetString()!;
        Check(preparedPower.Status == 200, "a paired phone can request a short-lived power-action challenge");
        Check((await Post(phone, "api/power/execute", new { action = "restart", challenge = powerChallenge })).Status == 409
              && fakePcActions.Calls.Count == 0,
            "a mismatched confirmation cannot trigger a PC operation");
        string powerRequestId = NewRequestId();
        var powerExecuted = await Send(phone, HttpMethod.Post, "api/power/execute", new { action = "shutdown", challenge = powerChallenge }, powerRequestId);
        var powerReplay = await Send(phone, HttpMethod.Post, "api/power/execute", new { action = "shutdown", challenge = powerChallenge }, powerRequestId);
        Check(powerExecuted.Status == 200 && powerReplay.Status == 409 && fakePcActions.Calls.SequenceEqual(["shutdown"]),
            "the prepared power action is consumed once and duplicate execution is rejected before reaching Windows");

        Check((await Post(phone, "api/chat", new { text = (string?)null })).Status == 400
              && (await Post(phone, "api/tasks", new { title = (string?)null, priority = (string?)null, due = (string?)null })).Status == 400,
            "typed request contracts treat explicit null fields as invalid input instead of throwing");
        List<(string Name, JsonElement Data)> events = await PostSse(phone, "api/chat", new { text = "daj stream (strumień)" });
        Check(events[0].Name == "start" && events.Count(e => e.Name == "delta") == 3 && events[^1].Name == "done", "chat streams start, deltas and a final answer (got: " + string.Join(",", events.Select(e => e.Name)) + ")");
        Check(events[^1].Data.GetProperty("text").GetString() == "Jeden dwa trzy" && events[^1].Data.GetProperty("status").GetString() == "verified", "the final answer carries its proof status");
        List<(string Name, JsonElement Data)> saturated = await PostSse(phone, "api/chat", new { text = "daj big stream" });
        Check(saturated.Count(e => e.Name == "delta") == 128
              && saturated[^1].Name == "done"
              && saturated[^1].Data.GetProperty("text").GetString() == new string('x', 300),
            "a saturated streaming queue remains bounded to 128 deltas while the final event preserves the full answer");
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
            byte[] sink = new byte[64];
            int read;
            try
            {
                await tls.WriteAsync(Encoding.ASCII.GetBytes("GET /" + new string('a', 20000) + " HTTP/1.1\r\n"));
                await tls.FlushAsync();
                read = await tls.ReadAsync(sink);
            }
            catch (IOException) { read = 0; } // the server hangs up as soon as the head is too large — possibly while we are still sending
            Check(read == 0, "an oversized request head is dropped without an answer");
        }
        Check((await Get(anonymous, "api/hello")).Status == 200, "the service is still healthy after garbage");

        // ---- token rotation invalidates the old credential immediately but keeps the paired device identity.
        var rotation = await Post(phone, "api/devices/rotate", new { });
        string rotatedPhoneToken = rotation.Json.GetProperty("token").GetString()!;
        using var oldPhoneCredential = Client(service, phoneToken);
        phone.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", rotatedPhoneToken);
        Check(rotation.Status == 200 && rotatedPhoneToken != phoneToken
              && (await Get(oldPhoneCredential, "api/state")).Status == 401
              && (await Get(phone, "api/state")).Status == 200,
            "the controller rotates a saved token, revokes the prior token and retains the same paired identity");

        // ---- unpairing
        var listed = await Get(phone, "api/devices");
        JsonElement pairedDevices = listed.Json.GetProperty("devices");
        Check(pairedDevices.EnumerateArray().Any(d => d.GetProperty("current").GetBoolean() && d.GetProperty("name").GetString() == "Telefon z testu"),
            "the phone sees itself in the list");
        Check(pairedDevices[0].GetProperty("capabilities").EnumerateArray().Any(x => x.GetString() == "notifications")
              && pairedDevices[0].GetProperty("capabilities").EnumerateArray().Any(x => x.GetString() == "wakeOnLan"),
            "the paired-device response preserves its typed capability identifiers");
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
