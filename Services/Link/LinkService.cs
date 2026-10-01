using System.IO;
using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SentinelX.Core;

namespace SentinelX.Services.Link;

/// <summary>The PC side of the phone connection: a small HTTPS server on the home network. Starts by itself, asks the user for one click
/// when a new phone appears, and never reaches the internet. A phone is trusted only after it is approved on the PC; from then on it
/// authenticates with a 256-bit token whose hash is all the PC remembers.</summary>
public sealed class LinkService : IDisposable
{
    public const int DefaultPort = 43180;
    public const int DiscoveryPort = 43181;
    private const int MaxConnections = 32;
    private static readonly TimeSpan PairLifetime = TimeSpan.FromSeconds(120);

    private sealed class PairEntry
    {
        public string Id = "";
        public string Device = "";
        public string Remote = "";
        public string Sas = "";
        public string ServerNonce = "";
        public DateTimeOffset CreatedAt;
        public DateTimeOffset ExpiresAt;
        public PairingState State = PairingState.Pending;
        public string? Token;
        public string? DeviceId;
    }

    private sealed class Bucket
    {
        public int Count;
        public DateTimeOffset Start;
    }

    private readonly LinkApi api;
    private readonly ILinkApprovalUi approvalUi;
    private readonly Func<LinkSettings> settings;
    private readonly string directory;
    private readonly object gate = new();
    private readonly SemaphoreSlim slots = new(MaxConnections, MaxConnections);
    private readonly Dictionary<string, PairEntry> pairs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Bucket> pairRates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Bucket> pollRates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Bucket> authFailures = new(StringComparer.Ordinal);
    private readonly LinkDeviceStore devices;
    private LinkCertificate? certificate;
    private TcpListener? listener;
    private UdpClient? discovery;
    private CancellationTokenSource? shutdown;
    private Task? acceptLoop;
    private Task? discoveryLoop;
    private string status = "Wyłączone.";
    private bool disposed;

    internal LinkService(LinkApi api, ILinkApprovalUi approvalUi, Func<LinkSettings> settings, string? directory = null)
    {
        this.api = api;
        this.approvalUi = approvalUi;
        this.settings = settings;
        this.directory = directory ?? Path.Combine(AppPaths.Root, "Link");
        devices = new LinkDeviceStore(this.directory);
        devices.Changed += OnDevicesChanged;
    }

    /// <summary>Raised when the running state, the port or the list of phones changes (always off the UI thread).</summary>
    public event Action? Changed;

    public bool IsRunning { get { lock (gate) return shutdown != null; } }
    public int Port { get; private set; }
    public string Fingerprint => certificate?.Fingerprint ?? "";
    public string Status { get { lock (gate) return status; } }
    public IReadOnlyList<string> Urls => IsRunning ? LinkNetwork.BuildUrls(Port) : [];
    public IReadOnlyList<LinkDeviceInfo> Devices => devices.List();
    public bool RemoveDevice(string id) => devices.Remove(id);
    public void RemoveAllDevices() => devices.RemoveAll();

    // ------------------------------------------------------------------ lifecycle

    /// <summary>Starts listening. <paramref name="bindAddress"/> is only for tests; the app listens on all adapters and then
    /// refuses every peer that is not on a private network.</summary>
    public async Task StartAsync(IPAddress? bindAddress = null)
    {
        lock (gate) { if (shutdown != null || disposed) return; }
        LinkCertificate cert = certificate ?? await Task.Run(() => new LinkCertificate(directory)).ConfigureAwait(false);
        TcpListener bound = Bind(bindAddress ?? IPAddress.Any, settings().Port);
        UdpClient? udp = settings().Discovery && bindAddress == null ? TryOpenDiscovery() : null;
        var stop = new CancellationTokenSource();
        lock (gate)
        {
            if (shutdown != null || disposed)
            {
                bound.Stop();
                udp?.Dispose();
                stop.Dispose();
                if (certificate == null) cert.Dispose();
                return;
            }
            certificate = cert;
            listener = bound;
            discovery = udp;
            shutdown = stop;
            Port = ((IPEndPoint)bound.LocalEndpoint).Port;
            acceptLoop = Task.Run(() => AcceptLoopAsync(bound, cert, stop.Token));
            if (udp != null) discoveryLoop = Task.Run(() => DiscoveryLoopAsync(udp, cert.Fingerprint, stop.Token));
            status = "Działa · port " + Port;
        }
        RaiseChanged();
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? stop;
        TcpListener? tcp;
        UdpClient? udp;
        var loops = new List<Task>();
        lock (gate)
        {
            stop = shutdown;
            if (stop == null) return;
            shutdown = null;
            tcp = listener; listener = null;
            udp = discovery; discovery = null;
            if (acceptLoop != null) loops.Add(acceptLoop);
            if (discoveryLoop != null) loops.Add(discoveryLoop);
            acceptLoop = null; discoveryLoop = null;
            status = "Zatrzymane.";
        }
        stop.Cancel();
        try { tcp?.Stop(); } catch (SocketException) { }
        try { udp?.Dispose(); } catch (ObjectDisposedException) { }
        try { await Task.WhenAll(loops).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException or TimeoutException) { }
        stop.Dispose();
        RaiseChanged();
    }

    public void Dispose()
    {
        if (disposed) return;
        StopAsync().GetAwaiter().GetResult();
        disposed = true;
        devices.Changed -= OnDevicesChanged;
        certificate?.Dispose();
        slots.Dispose();
    }

    private static TcpListener Bind(IPAddress address, int requestedPort)
    {
        if (requestedPort == 0)
        {
            var single = new TcpListener(address, 0);
            single.Start(16);
            return single;
        }
        int first = Math.Clamp(requestedPort, 1024, 65500);
        SocketException? last = null;
        for (int offset = 0; offset < 10; offset++)
        {
            var candidate = new TcpListener(address, first + offset);
            try { candidate.Start(16); return candidate; }
            catch (SocketException ex) { last = ex; candidate.Stop(); }
        }
        throw new InvalidOperationException($"Nie udało się otworzyć portu dla telefonu ({first}–{first + 9}): {last?.Message}");
    }

    private static UdpClient? TryOpenDiscovery()
    {
        try
        {
            var udp = new UdpClient(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            udp.EnableBroadcast = true;
            return udp;
        }
        catch (SocketException) { return null; } // port taken: pairing by typing the address still works
    }

    // ------------------------------------------------------------------ connections

    private async Task AcceptLoopAsync(TcpListener tcp, LinkCertificate cert, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client = await tcp.AcceptTcpClientAsync(token).ConfigureAwait(false);
                if (!slots.Wait(0)) { client.Dispose(); continue; }
                _ = HandleAndReleaseAsync(client, cert, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            _ = Task.Run(StopAsync); // dead listener: reset the state so the caretaker starts it again
        }
    }

    private async Task HandleAndReleaseAsync(TcpClient client, LinkCertificate cert, CancellationToken serviceToken)
    {
        try { await HandleClientAsync(client, cert, serviceToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or SocketException or AuthenticationException or OperationCanceledException or ObjectDisposedException or InvalidDataException or JsonException) { }
        catch (Exception ex) { AppLog.Write(ex); }
        finally
        {
            client.Dispose();
            try { slots.Release(); } catch (ObjectDisposedException) { }
        }
    }

    private async Task HandleClientAsync(TcpClient client, LinkCertificate cert, CancellationToken serviceToken)
    {
        if (client.Client.RemoteEndPoint is not IPEndPoint remote || !LinkNetwork.IsTrusted(remote.Address)) return;
        client.NoDelay = true;
        using var headToken = CancellationTokenSource.CreateLinkedTokenSource(serviceToken);
        headToken.CancelAfter(TimeSpan.FromSeconds(15));
        using var tls = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = cert.Certificate,
            ClientCertificateRequired = false,
            EnabledSslProtocols = SslProtocols.None,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
        }, headToken.Token).ConfigureAwait(false);
        LinkRequest? request = await LinkHttp.ReadAsync(tls, remote.Address, headToken.Token).ConfigureAwait(false);
        if (request == null) return;

        var response = new LinkResponse(tls, headOnly: request.Method == "HEAD");
        using var work = CancellationTokenSource.CreateLinkedTokenSource(serviceToken);
        work.CancelAfter(TimeSpan.FromMinutes(15));
        try { await DispatchAsync(request, response, cert, work.Token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not (IOException or SocketException or ObjectDisposedException or AuthenticationException or OperationCanceledException))
        {
            AppLog.Write(ex);
            if (!response.Started)
            {
                try { await response.WriteJsonAsync(500, new { error = "Błąd wewnętrzny komputera." }, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception inner) when (inner is IOException or ObjectDisposedException or SocketException) { }
            }
        }
    }

    // ------------------------------------------------------------------ routing

    private async Task DispatchAsync(LinkRequest req, LinkResponse res, LinkCertificate cert, CancellationToken ct)
    {
        if (!req.Path.StartsWith("/api/", StringComparison.Ordinal))
        {
            if (req.Method is "GET" or "HEAD") await ServeStaticAsync(req, res, ct).ConfigureAwait(false);
            else await res.WriteJsonAsync(405, new { error = "Metoda niedozwolona." }, ct).ConfigureAwait(false);
            return;
        }

        switch (req.Path)
        {
            case "/api/hello" when req.Method is "GET" or "HEAD":
                await res.WriteJsonAsync(200, new { app = "SentinelX", api = 1, version = AppConstants.Version, name = Environment.MachineName, fingerprint = cert.Fingerprint }, ct).ConfigureAwait(false);
                return;
            case "/api/pair/request" when req.Method == "POST":
                await PairRequestAsync(req, res, cert, ct).ConfigureAwait(false);
                return;
            case "/api/pair/status" when req.Method == "GET":
                await PairStatusAsync(req, res, ct).ConfigureAwait(false);
                return;
        }

        LinkDeviceInfo? device = Authenticate(req);
        if (device == null)
        {
            await res.WriteJsonAsync(401, new { error = "unauthorized" }, ct).ConfigureAwait(false);
            return;
        }
        switch (req.Path)
        {
            case "/api/devices" when req.Method == "GET":
                await res.WriteJsonAsync(200, new
                {
                    devices = devices.List().Select(d => new
                    {
                        id = d.Id, name = d.Name, addedAt = d.AddedAt.ToString("O", CultureInfo.InvariantCulture),
                        lastSeen = d.LastSeen.ToString("O", CultureInfo.InvariantCulture), current = d.Id == device.Id
                    }).ToArray()
                }, ct).ConfigureAwait(false);
                return;
            case "/api/unpair" when req.Method == "POST":
                devices.Remove(device.Id);
                await res.WriteJsonAsync(200, new { ok = true }, ct).ConfigureAwait(false);
                return;
        }
        await api.HandleAsync(req, res, ct).ConfigureAwait(false);
    }

    private static async Task ServeStaticAsync(LinkRequest req, LinkResponse res, CancellationToken ct)
    {
        LinkAsset? asset = LinkWebAssets.Find(req.Path);
        if (asset == null)
        {
            await res.WriteAsync(404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Nie znaleziono."), ct).ConfigureAwait(false);
            return;
        }
        if (req.Headers.TryGetValue("If-None-Match", out string? tag) && tag == asset.ETag)
        {
            await res.WriteAsync(304, asset.ContentType, [], ct, "no-cache", asset.ETag).ConfigureAwait(false);
            return;
        }
        await res.WriteAsync(200, asset.ContentType, asset.Bytes, ct, "no-cache", asset.ETag).ConfigureAwait(false);
    }

    private LinkDeviceInfo? Authenticate(LinkRequest req)
    {
        string ip = req.Remote.ToString();
        lock (gate) { if (OverLimitLocked(authFailures, ip, 30, TimeSpan.FromMinutes(1))) return null; }
        LinkDeviceInfo? device = devices.Validate(req.Bearer);
        if (device == null) lock (gate) CountLocked(authFailures, ip, TimeSpan.FromMinutes(1));
        return device;
    }

    // ------------------------------------------------------------------ pairing

    private async Task PairRequestAsync(LinkRequest req, LinkResponse res, LinkCertificate cert, CancellationToken ct)
    {
        JsonElement body = req.Json();
        string device = SafeName(Str(body, "device"));
        byte[]? clientNonce = DecodeNonce(Str(body, "nonce"));
        if (clientNonce == null)
        {
            await res.WriteJsonAsync(400, new { error = "Niepoprawna prośba o parowanie." }, ct).ConfigureAwait(false);
            return;
        }

        PairEntry entry;
        byte[] serverNonce = RandomNumberGenerator.GetBytes(16);
        string ip = req.Remote.ToString();
        string? refusal = null;
        lock (gate)
        {
            PruneLocked();
            if (OverLimitLocked(pairRates, ip, 6, TimeSpan.FromMinutes(1))) refusal = "Za dużo prób parowania. Spróbuj za minutę.";
            else if (pairs.Values.Any(p => p.State == PairingState.Pending && p.ExpiresAt > DateTimeOffset.Now))
                refusal = "Na komputerze czeka już inna prośba o parowanie. Zatwierdź ją albo poczekaj chwilę.";
            CountLocked(pairRates, ip, TimeSpan.FromMinutes(1));
            entry = new PairEntry
            {
                Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant(),
                Device = device, Remote = ip, ServerNonce = LinkDeviceStore.Base64Url(serverNonce),
                CreatedAt = DateTimeOffset.Now, ExpiresAt = DateTimeOffset.Now + PairLifetime,
                Sas = ComputeSas(cert.FingerprintBytes, clientNonce, serverNonce)
            };
            if (refusal == null) pairs[entry.Id] = entry;
        }
        if (refusal != null)
        {
            await res.WriteJsonAsync(429, new { error = refusal }, ct).ConfigureAwait(false);
            return;
        }

        try { approvalUi.ShowRequest(new PairingRequestInfo(entry.Id, entry.Device, ip, entry.Sas, entry.ExpiresAt), approved => Decide(entry.Id, approved)); }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            lock (gate) entry.State = PairingState.Denied;
            await res.WriteJsonAsync(500, new { error = "Komputer nie mógł pokazać okna zgody. Uruchom Sentinel X na pulpicie i spróbuj ponownie." }, ct).ConfigureAwait(false);
            return;
        }
        string requestId = entry.Id;
        _ = Task.Run(async () =>
        {
            await Task.Delay(PairLifetime + TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            ExpirePending(requestId);
        });
        RaiseChanged();
        await res.WriteJsonAsync(200, new { id = entry.Id, nonce = entry.ServerNonce, expiresIn = (int)PairLifetime.TotalSeconds, sas = entry.Sas }, ct).ConfigureAwait(false);
    }

    private async Task PairStatusAsync(LinkRequest req, LinkResponse res, CancellationToken ct)
    {
        string ip = req.Remote.ToString();
        object payload;
        int code = 200;
        bool closeUi = false;
        string id = req.Q("id");
        lock (gate)
        {
            if (OverLimitLocked(pollRates, ip, 240, TimeSpan.FromMinutes(1))) { payload = new { error = "Za dużo zapytań." }; code = 429; }
            else
            {
                CountLocked(pollRates, ip, TimeSpan.FromMinutes(1));
                if (!pairs.TryGetValue(id, out PairEntry? entry)) { payload = new { error = "Nieznana prośba." }; code = 404; }
                else
                {
                    if (entry.State == PairingState.Pending && DateTimeOffset.Now > entry.ExpiresAt) { entry.State = PairingState.Expired; closeUi = true; }
                    switch (entry.State)
                    {
                        case PairingState.Approved when entry.Token != null:
                            payload = new { state = "approved", token = entry.Token, deviceId = entry.DeviceId };
                            entry.Token = null; // delivered exactly once
                            break;
                        case PairingState.Approved: payload = new { state = "expired" }; break;
                        case PairingState.Denied: payload = new { state = "denied" }; break;
                        case PairingState.Expired: payload = new { state = "expired" }; break;
                        default: payload = new { state = "pending" }; break;
                    }
                }
            }
        }
        if (closeUi) SafeCloseUi(id);
        await res.WriteJsonAsync(code, payload, ct).ConfigureAwait(false);
    }

    private void Decide(string id, bool approved)
    {
        lock (gate)
        {
            if (!pairs.TryGetValue(id, out PairEntry? entry) || entry.State != PairingState.Pending) return;
            if (DateTimeOffset.Now > entry.ExpiresAt) entry.State = PairingState.Expired;
            else if (!approved) entry.State = PairingState.Denied;
            else
            {
                (LinkDeviceInfo device, string token) = devices.Add(entry.Device);
                entry.DeviceId = device.Id;
                entry.Token = token;
                entry.State = PairingState.Approved;
            }
        }
        RaiseChanged();
    }

    private void ExpirePending(string id)
    {
        bool expired = false;
        lock (gate)
        {
            if (pairs.TryGetValue(id, out PairEntry? entry) && entry.State == PairingState.Pending) { entry.State = PairingState.Expired; expired = true; }
        }
        if (expired) { SafeCloseUi(id); RaiseChanged(); }
    }

    private void SafeCloseUi(string id)
    {
        try { approvalUi.CloseRequest(id); }
        catch (Exception ex) { AppLog.Write(ex); }
    }

    /// <summary>Six digits both sides derive from the certificate fingerprint and two fresh nonces. If someone sits between the phone and the PC,
    /// the phone sees a different certificate and therefore a different code than the PC shows.</summary>
    internal static string ComputeSas(byte[] fingerprint, byte[] clientNonce, byte[] serverNonce)
    {
        byte[] data = new byte[fingerprint.Length + clientNonce.Length + serverNonce.Length];
        Buffer.BlockCopy(fingerprint, 0, data, 0, fingerprint.Length);
        Buffer.BlockCopy(clientNonce, 0, data, fingerprint.Length, clientNonce.Length);
        Buffer.BlockCopy(serverNonce, 0, data, fingerprint.Length + clientNonce.Length, serverNonce.Length);
        uint value = BinaryPrimitives.ReadUInt32BigEndian(SHA256.HashData(data));
        return (value % 1_000_000u).ToString("D6", CultureInfo.InvariantCulture);
    }

    internal static byte[]? DecodeNonce(string text)
    {
        if (text.Length is < 16 or > 40) return null;
        string base64 = text.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        try
        {
            byte[] bytes = Convert.FromBase64String(base64);
            return bytes.Length == 16 ? bytes : null;
        }
        catch (FormatException) { return null; }
    }

    private static string SafeName(string name)
    {
        string trimmed = name.Trim();
        return Regex.IsMatch(trimmed, @"^[\p{L}\p{N} _.\-]{1,32}$", RegexOptions.CultureInvariant) ? trimmed : "Telefon";
    }

    private static string Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";

    // ------------------------------------------------------------------ limits

    private static bool OverLimitLocked(Dictionary<string, Bucket> map, string key, int limit, TimeSpan window) =>
        map.TryGetValue(key, out Bucket? bucket) && DateTimeOffset.Now - bucket.Start < window && bucket.Count >= limit;

    private static void CountLocked(Dictionary<string, Bucket> map, string key, TimeSpan window)
    {
        if (!map.TryGetValue(key, out Bucket? bucket) || DateTimeOffset.Now - bucket.Start >= window)
            map[key] = bucket = new Bucket { Start = DateTimeOffset.Now };
        bucket.Count++;
    }

    private void PruneLocked()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        foreach (string key in pairs.Where(p => now - p.Value.CreatedAt > TimeSpan.FromMinutes(10)).Select(p => p.Key).ToArray()) pairs.Remove(key);
        foreach (Dictionary<string, Bucket> map in new[] { pairRates, pollRates, authFailures })
        {
            if (map.Count < 256) continue;
            foreach (string key in map.Where(p => now - p.Value.Start > TimeSpan.FromMinutes(2)).Select(p => p.Key).ToArray()) map.Remove(key);
        }
    }

    // ------------------------------------------------------------------ discovery (UDP)

    /// <summary>Answers the phone's "where is my PC?" broadcast with name, port and certificate fingerprint. Anyone on the LAN can ask;
    /// the answer contains nothing secret and pairing still needs a click on the PC.</summary>
    private async Task DiscoveryLoopAsync(UdpClient udp, string fingerprint, CancellationToken token)
    {
        byte[] probe = Encoding.ASCII.GetBytes("SXLINK1?");
        int window = 0;
        DateTimeOffset windowStart = DateTimeOffset.Now;
        try
        {
            while (!token.IsCancellationRequested)
            {
                UdpReceiveResult received = await udp.ReceiveAsync(token).ConfigureAwait(false);
                if (!LinkNetwork.IsTrusted(received.RemoteEndPoint.Address) || !received.Buffer.AsSpan().SequenceEqual(probe)) continue;
                if (DateTimeOffset.Now - windowStart > TimeSpan.FromSeconds(1)) { windowStart = DateTimeOffset.Now; window = 0; }
                if (++window > 20) continue;
                byte[] reply = JsonSerializer.SerializeToUtf8Bytes(new { app = "SentinelX", api = 1, name = Environment.MachineName, port = Port, fingerprint });
                await udp.SendAsync(reply, reply.Length, received.RemoteEndPoint).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException) { }
    }

    private void OnDevicesChanged() => RaiseChanged();

    private void RaiseChanged()
    {
        try { Changed?.Invoke(); }
        catch (Exception ex) { AppLog.Write(ex); }
    }
}
