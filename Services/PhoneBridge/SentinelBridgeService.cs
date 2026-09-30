using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SentinelX.Services.PhoneBridge;

/// <summary>Opt-in TLS bridge bound only to individual private IPv4 addresses; it has no cloud relay, shell, or arbitrary-script endpoint.</summary>
public sealed class SentinelBridgeService : IDisposable
{
    public const int Port = 43179;
    private const int MaxRequestBytes = 16 * 1024;
    private const int MaxConnections = 8;
    private const int MaxPairedPhones = 5;
    private static readonly TimeSpan PairingLifetime = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 8
    };

    private readonly object gate = new();
    private readonly PhoneSkillExecutor skills;
    private readonly ActionHistoryService? history;
    private readonly string dataDirectory;
    private readonly SemaphoreSlim connectionSlots = new(MaxConnections, MaxConnections);
    private readonly Dictionary<string, ReplayWindow> replay = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RateWindow> rates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> pairingAttempts = new(StringComparer.Ordinal);
    private List<PairedPhone> pairedPhones = [];
    private List<TcpListener> listeners = [];
    private List<Task> acceptTasks = [];
    private CancellationTokenSource? shutdown;
    private X509Certificate2? certificate;
    private string? pairingCode;
    private DateTimeOffset pairingExpiresAt;
    private string status = "Wyłączony. Bridge uruchamia się wyłącznie na jawne polecenie użytkownika.";
    private string[] endpoints = [];

    public event Action? StateChanged;
    public bool IsRunning { get { lock (gate) return shutdown != null; } }
    public string Status { get { lock (gate) return status; } }
    public string PairingCode { get { lock (gate) return pairingCode ?? ""; } }
    public string CertificateFingerprint { get { lock (gate) return certificate == null ? "" : Fingerprint(certificate); } }
    public IReadOnlyList<string> Endpoints { get { lock (gate) return endpoints.ToArray(); } }
    public int PairedPhoneCount { get { lock (gate) return pairedPhones.Count; } }
    public IReadOnlyList<string> PairedPhoneNames { get { lock (gate) return pairedPhones.Select(x => x.Name).ToArray(); } }

    public SentinelBridgeService(PhoneSkillExecutor skills, ActionHistoryService? history = null, string? dataDirectory = null)
    {
        this.skills = skills;
        this.history = history;
        this.dataDirectory = dataDirectory ?? Path.Combine(AppPaths.Root, "PhoneBridge");
        LoadPairedPhones();
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (shutdown != null) return Task.CompletedTask;
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Sentinel Bridge wymaga Windows i ochrony DPAPI bieżącego użytkownika.");
            IPAddress[] privateAddresses = GetPrivateIpv4Addresses();
            if (privateAddresses.Length == 0) throw new InvalidOperationException("Nie znaleziono prywatnego adresu IPv4. Bridge nie nasłuchuje na interfejsach publicznych.");
            X509Certificate2 cert = LoadOrCreateCertificate();
            var started = new List<TcpListener>();
            try
            {
                foreach (IPAddress address in privateAddresses)
                {
                    var listener = new TcpListener(address, Port);
                    try { listener.Start(8); started.Add(listener); }
                    catch (SocketException) { listener.Stop(); }
                }
                if (started.Count == 0) throw new InvalidOperationException($"Nie udało się związać portu {Port} z żadnym prywatnym adresem; sprawdź, czy port nie jest zajęty.");
                var stop = new CancellationTokenSource();
                certificate = cert;
                listeners = started;
                endpoints = started.Select(x => $"{((IPEndPoint)x.LocalEndpoint).Address}:{Port}").ToArray();
                shutdown = stop;
                pairingCode = MakePairingCode();
                pairingExpiresAt = DateTimeOffset.UtcNow + PairingLifetime;
                pairingAttempts.Clear();
                status = $"Oczekiwanie na bezpieczne parowanie · {endpoints.Length} prywatny adres(y) · {pairedPhones.Count} sparowany telefon(y).";
                acceptTasks = started.Select(x => AcceptLoopAsync(x, stop.Token)).ToList();
            }
            catch
            {
                foreach (TcpListener listener in started) listener.Stop();
                cert.Dispose();
                throw;
            }
        }
        RaiseStateChanged();
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        List<TcpListener> oldListeners;
        List<Task> loops;
        CancellationTokenSource? oldStop;
        X509Certificate2? oldCertificate;
        lock (gate)
        {
            oldStop = shutdown;
            if (oldStop == null) return;
            shutdown = null;
            oldListeners = listeners; listeners = [];
            loops = acceptTasks; acceptTasks = [];
            oldCertificate = certificate; certificate = null;
            pairingCode = null; endpoints = [];
            status = "Bridge zatrzymany. Sparowane telefony pozostają zapisane; nowy kod parowania będzie jednorazowy.";
        }
        oldStop.Cancel();
        foreach (TcpListener listener in oldListeners) listener.Stop();
        try { await Task.WhenAll(loops).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException or TimeoutException) { }
        oldCertificate?.Dispose();
        oldStop.Dispose();
        RaiseStateChanged();
    }

    public bool RevokeAllPhones()
    {
        bool success;
        lock (gate)
        {
            List<PairedPhone> old = pairedPhones;
            pairedPhones = [];
            try { SavePairedPhonesLocked(); success = true; }
            catch
            {
                pairedPhones = old;
                status = "Nie udało się zapisać cofnięcia parowań; dotychczasowe telefony nadal są autoryzowane.";
                success = false;
            }
            if (success)
            {
                replay.Clear(); rates.Clear();
                status = IsRunning ? "Wszystkie autoryzacje telefonów cofnięto. Sparuj telefon ponownie." : "Wszystkie autoryzacje telefonów cofnięto.";
            }
        }
        RaiseStateChanged();
        return success;
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                if (!connectionSlots.Wait(0)) { client.Dispose(); continue; }
                _ = HandleAndReleaseAsync(client, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (SocketException) when (token.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (token.IsCancellationRequested) { }
    }

    private async Task HandleAndReleaseAsync(TcpClient client, CancellationToken serviceToken)
    {
        try { await HandleClientAsync(client, serviceToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or SocketException or AuthenticationException or OperationCanceledException or JsonException or ObjectDisposedException or CryptographicException or InvalidOperationException) { }
        finally { client.Dispose(); connectionSlots.Release(); }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken serviceToken)
    {
        if (client.Client.RemoteEndPoint is not IPEndPoint remote || !IsPrivateIpv4(remote.Address)) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(serviceToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        using var tls = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        X509Certificate2? cert;
        lock (gate) cert = certificate;
        if (cert == null) return;
        await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = cert,
            ClientCertificateRequired = false,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
        }, timeout.Token).ConfigureAwait(false);
        string? line = await ReadLineBoundedAsync(tls, MaxRequestBytes, timeout.Token).ConfigureAwait(false);
        if (line == null) return;
        BridgeRequest? request = JsonSerializer.Deserialize<BridgeRequest>(line, JsonOptions);
        if (request == null) return;
        BridgeResponse response = await DispatchAsync(request, remote.Address, timeout.Token).ConfigureAwait(false);
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response, JsonOptions) + "\n");
        await tls.WriteAsync(bytes, timeout.Token).ConfigureAwait(false);
        await tls.FlushAsync(timeout.Token).ConfigureAwait(false);
    }

    private async Task<BridgeResponse> DispatchAsync(BridgeRequest request, IPAddress remoteAddress, CancellationToken token)
    {
        if (request.ProtocolVersion != 1) return Fail("FAILED", "Wersja protokołu nie jest obsługiwana.");
        if (request.Type == "pair") return Pair(request, remoteAddress);
        if (request.Type != "execute") return Fail("FAILED", "Nieznany typ żądania.");
        if (!TryAuthenticate(request, remoteAddress, out string authError)) return Fail("FAILED", authError);
        if (!Regex.IsMatch(request.Intent ?? "", @"^[A-Z0-9_]{1,48}$", RegexOptions.CultureInvariant)) return Fail("FAILED", "Nieprawidłowy identyfikator skilla.");
        if (request.Parameters == null || request.Parameters.Count > 10 || request.Parameters.Any(x =>
                !Regex.IsMatch(x.Key, @"^[a-zA-Z][a-zA-Z0-9_]{0,31}$", RegexOptions.CultureInvariant) ||
                x.Value == null || x.Value.Length > 512 || x.Value.Any(char.IsControl)))
            return Fail("FAILED", "Parametry żądania przekraczają bezpieczny limit.");
        string? actionId = history?.CreateActionId();
        string safeCommand = SensitiveDataRedactor.Redact(request.Intent + " " + string.Join(" ", request.Parameters.Select(x => x.Key + "=" + x.Value)));
        if (actionId != null) history!.AddRunning(actionId, "PHONE_BRIDGE", safeCommand);
        try
        {
            PhoneSkillResult result = await skills.ExecuteAsync(request.Intent, request.Parameters, token).ConfigureAwait(false);
            if (actionId != null)
            {
                ActionExecutionResult audit = result.Status switch
                {
                    "SUCCESS" => ActionExecutionResult.VerifiedSuccess(result.Message, result.Evidence),
                    "UNVERIFIED" or "PARTIAL" or "PENDING" => ActionExecutionResult.UnverifiedSuccess(result.Message, result.Evidence),
                    _ => ActionExecutionResult.Failure(result.Message, result.Evidence)
                };
                history!.AddResult(actionId, "PHONE_BRIDGE", safeCommand, audit);
            }
            return new BridgeResponse { Status = result.Status, Message = Limit(result.Message, 1200), Evidence = Limit(result.Evidence, 500) };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            const string message = "Połączenie lub akcja zostały anulowane; wynik nie jest potwierdzony.";
            if (actionId != null) history!.AddResult(actionId, "PHONE_BRIDGE", safeCommand, ActionExecutionResult.UnverifiedSuccess(message, "Anulowanie może nastąpić po wysłaniu akcji."));
            return Fail("UNVERIFIED", message);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            const string message = "Wystąpił błąd lokalnej umiejętności. Szczegóły nie zostały wysłane do telefonu.";
            if (actionId != null) history!.AddResult(actionId, "PHONE_BRIDGE", safeCommand, ActionExecutionResult.Failure(message));
            return Fail("FAILED", message);
        }
    }

    private BridgeResponse Pair(BridgeRequest request, IPAddress remote)
    {
        string ip = remote.ToString();
        lock (gate)
        {
            int tries = pairingAttempts.GetValueOrDefault(ip);
            if (tries >= 5) return Fail("FAILED", "Parowanie zostało czasowo zablokowane dla tego adresu.");
            if (!pairingAttempts.ContainsKey(ip) && pairingAttempts.Count >= 128) return Fail("FAILED", "Osiągnięto limit prób parowania. Uruchom bridge ponownie na komputerze.");
            pairingAttempts[ip] = tries + 1;
            if (pairingCode == null || DateTimeOffset.UtcNow > pairingExpiresAt || string.IsNullOrWhiteSpace(request.PairingCode))
                return Fail("FAILED", "Kod parowania wygasł lub nie jest aktywny. Uruchom bridge ponownie na komputerze.");
            byte[] supplied = SHA256.HashData(Encoding.UTF8.GetBytes(request.PairingCode.Trim().ToUpperInvariant()));
            byte[] expected = SHA256.HashData(Encoding.UTF8.GetBytes(pairingCode));
            if (!CryptographicOperations.FixedTimeEquals(supplied, expected)) return Fail("FAILED", "Nieprawidłowy kod parowania.");
            string name = SafeDeviceName(request.DeviceName);
            if (name.Length == 0) return Fail("FAILED", "Podaj nazwę telefonu zawierającą 2–32 bezpieczne znaki.");
            if (pairedPhones.Count >= MaxPairedPhones) return Fail("FAILED", "Osiągnięto limit sparowanych telefonów. Cofnij stare parowania na komputerze.");
            byte[] tokenBytes = RandomNumberGenerator.GetBytes(32);
            string phoneToken = Convert.ToBase64String(tokenBytes);
            string deviceId = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
            pairedPhones.Add(new PairedPhone { Id = deviceId, Name = name, TokenHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(phoneToken))), AddedUtc = DateTimeOffset.UtcNow });
            try { SavePairedPhonesLocked(); }
            catch
            {
                pairedPhones.RemoveAll(x => x.Id == deviceId);
                CryptographicOperations.ZeroMemory(tokenBytes);
                return Fail("FAILED", "Nie udało się bezpiecznie zapisać parowania. Telefon nie został sparowany.");
            }
            pairingCode = null;
            CryptographicOperations.ZeroMemory(tokenBytes);
            status = $"Telefon „{name}” sparowano. Kod został unieważniony; ruch jest szyfrowany przez TLS.";
            RaiseStateChanged();
            return new BridgeResponse { Status = "PAIRED", Message = "Telefon sparowano. Token pokaże się tylko raz; aplikacja przechowuje go w Android Keystore.", DeviceId = deviceId, Token = phoneToken };
        }
    }

    private bool TryAuthenticate(BridgeRequest request, IPAddress remote, out string error)
    {
        error = "Nieprawidłowe uwierzytelnienie.";
        if (!IsPrivateIpv4(remote) || string.IsNullOrWhiteSpace(request.DeviceId) || request.DeviceId.Length > 40 ||
            string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 128) return false;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (request.TimestampUnixSeconds < now - 120 || request.TimestampUnixSeconds > now + 120)
        { error = "Zegar telefonu jest poza dozwolonym zakresem; zsynchronizuj datę i spróbuj ponownie."; return false; }
        if (request.Nonce == null || request.Nonce.Length is < 20 or > 80 || !Regex.IsMatch(request.Nonce, @"^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)) return false;
        byte[] actual = SHA256.HashData(Encoding.UTF8.GetBytes(request.Token));
        lock (gate)
        {
            PairedPhone? phone = pairedPhones.FirstOrDefault(x => x.Id == request.DeviceId);
            if (phone == null) return false;
            byte[] expected;
            try { expected = Convert.FromBase64String(phone.TokenHash); }
            catch (FormatException) { return false; }
            if (expected.Length != 32 || !CryptographicOperations.FixedTimeEquals(actual, expected)) return false;
            string key = phone.Id;
            if (!rates.TryGetValue(key, out RateWindow? rate) || DateTimeOffset.UtcNow - rate.StartedUtc >= TimeSpan.FromMinutes(1))
                rates[key] = rate = new RateWindow(DateTimeOffset.UtcNow);
            if (++rate.Count > 30) { error = "Limit 30 poleceń na minutę został osiągnięty."; return false; }
            if (!replay.TryGetValue(key, out ReplayWindow? window)) replay[key] = window = new ReplayWindow();
            if (!window.TryAdd(request.Nonce)) { error = "Odrzucono powtórzone żądanie."; return false; }
            return true;
        }
    }

    private void LoadPairedPhones()
    {
        string path = Path.Combine(dataDirectory, "phones.dpapi");
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 16 * 1024) return;
            byte[] protectedBytes = File.ReadAllBytes(path);
            byte[] clear = CurrentUserProtection.Unprotect(protectedBytes);
            CryptographicOperations.ZeroMemory(protectedBytes);
            pairedPhones = JsonSerializer.Deserialize<List<PairedPhone>>(clear, JsonOptions) ?? [];
            CryptographicOperations.ZeroMemory(clear);
            pairedPhones = pairedPhones.Where(x => x != null && x.Id is { Length: > 0 and <= 40 } && x.Name is { Length: >= 2 and <= 32 } &&
                x.TokenHash is { Length: > 0 and <= 128 } && Convert.TryFromBase64String(x.TokenHash, new byte[32], out int written) && written == 32)
                .Take(MaxPairedPhones).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or ArgumentException)
        { pairedPhones = []; }
    }

    private void SavePairedPhonesLocked()
    {
        Directory.CreateDirectory(dataDirectory);
        string path = Path.Combine(dataDirectory, "phones.dpapi");
        string temp = path + ".tmp";
        byte[] clear = JsonSerializer.SerializeToUtf8Bytes(pairedPhones, JsonOptions);
        byte[] protectedBytes = CurrentUserProtection.Protect(clear);
        CryptographicOperations.ZeroMemory(clear);
        try { File.WriteAllBytes(temp, protectedBytes); File.Move(temp, path, true); }
        finally { CryptographicOperations.ZeroMemory(protectedBytes); try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
    }

    private X509Certificate2 LoadOrCreateCertificate()
    {
        Directory.CreateDirectory(dataDirectory);
        string path = Path.Combine(dataDirectory, "bridge-cert.dpapi");
        if (File.Exists(path) && new FileInfo(path).Length <= 128 * 1024)
        {
            try
            {
                byte[] protectedBytes = File.ReadAllBytes(path);
                byte[] clear = CurrentUserProtection.Unprotect(protectedBytes);
                CryptographicOperations.ZeroMemory(protectedBytes);
                StoredCertificate? stored = JsonSerializer.Deserialize<StoredCertificate>(clear, JsonOptions);
                CryptographicOperations.ZeroMemory(clear);
                if (stored != null && stored.Pfx.Length is > 0 and < 64 * 1024)
                {
                    byte[] pfx = Convert.FromBase64String(stored.Pfx);
                    try
                    {
                        var loaded = new X509Certificate2(pfx, stored.Password, X509KeyStorageFlags.EphemeralKeySet);
                        if (loaded.NotAfter.ToUniversalTime() > DateTime.UtcNow.AddDays(7)) return loaded;
                        loaded.Dispose();
                    }
                    finally { CryptographicOperations.ZeroMemory(pfx); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or ArgumentException or FormatException) { }
        }
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=SentinelX Phone Bridge", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        var usages = new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, critical: false));
        using X509Certificate2 created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(5));
        string password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        byte[] pfxBytes = created.Export(X509ContentType.Pfx, password);
        byte[] clearBytes = JsonSerializer.SerializeToUtf8Bytes(new StoredCertificate { Pfx = Convert.ToBase64String(pfxBytes), Password = password }, JsonOptions);
        byte[] protectedFile = CurrentUserProtection.Protect(clearBytes);
        CryptographicOperations.ZeroMemory(pfxBytes); CryptographicOperations.ZeroMemory(clearBytes);
        string temp = path + ".tmp";
        try { File.WriteAllBytes(temp, protectedFile); File.Move(temp, path, true); }
        finally { CryptographicOperations.ZeroMemory(protectedFile); try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
        byte[] pfxForServer = created.Export(X509ContentType.Pfx, password);
        try { return new X509Certificate2(pfxForServer, password, X509KeyStorageFlags.EphemeralKeySet); }
        finally { CryptographicOperations.ZeroMemory(pfxForServer); }
    }

    private static async Task<string?> ReadLineBoundedAsync(Stream stream, int limit, CancellationToken token)
    {
        using var buffer = new MemoryStream(1024);
        byte[] one = new byte[1];
        while (buffer.Length <= limit)
        {
            int count = await stream.ReadAsync(one.AsMemory(0, 1), token).ConfigureAwait(false);
            if (count == 0) return buffer.Length == 0 ? null : Encoding.UTF8.GetString(buffer.ToArray());
            if (one[0] == (byte)'\n') return Encoding.UTF8.GetString(buffer.ToArray()).TrimEnd('\r');
            buffer.WriteByte(one[0]);
        }
        throw new IOException("Żądanie przekroczyło limit 16 KiB.");
    }

    private static IPAddress[] GetPrivateIpv4Addresses()
    {
        var result = new HashSet<IPAddress>();
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            try
            {
                foreach (UnicastIPAddressInformation item in nic.GetIPProperties().UnicastAddresses)
                    if (item.Address.AddressFamily == AddressFamily.InterNetwork && IsPrivateIpv4(item.Address)) result.Add(item.Address);
            }
            catch (NetworkInformationException) { }
        }
        return result.ToArray();
    }

    private static bool IsPrivateIpv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] b = address.GetAddressBytes();
        return b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168;
    }

    private static string MakePairingCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        byte[] random = RandomNumberGenerator.GetBytes(12);
        var result = new StringBuilder(12);
        foreach (byte b in random) result.Append(alphabet[b % alphabet.Length]);
        CryptographicOperations.ZeroMemory(random);
        return result.ToString();
    }

    private static string SafeDeviceName(string? name)
    {
        string value = (name ?? "").Trim();
        return value.Length is >= 2 and <= 32 && Regex.IsMatch(value, @"^[\p{L}\p{N} _-]+$", RegexOptions.CultureInvariant) ? value : "";
    }
    private static string Fingerprint(X509Certificate2 cert) => Convert.ToHexString(SHA256.HashData(cert.RawData));
    private static string Limit(string? text, int length) => (text ?? "").Length <= length ? text ?? "" : (text ?? "")[..length];
    private static BridgeResponse Fail(string status, string message) => new() { Status = status, Message = message };
    private void RaiseStateChanged() { try { StateChanged?.Invoke(); } catch (Exception ex) { AppLog.Write(ex); } }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();

    public sealed class BridgeRequest
    {
        public int ProtocolVersion { get; set; }
        public string Type { get; set; } = "";
        public string PairingCode { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string DeviceId { get; set; } = "";
        public string Token { get; set; } = "";
        public string Intent { get; set; } = "";
        public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.Ordinal);
        public string Nonce { get; set; } = "";
        public long TimestampUnixSeconds { get; set; }
    }

    public sealed class BridgeResponse
    {
        public string Status { get; set; } = "FAILED";
        public string Message { get; set; } = "";
        public string Evidence { get; set; } = "";
        public string? DeviceId { get; set; }
        public string? Token { get; set; }
    }

    private sealed class PairedPhone
    {
        public PairedPhone() { }
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string TokenHash { get; set; } = "";
        public DateTimeOffset AddedUtc { get; set; }
    }
    private sealed class StoredCertificate
    {
        public StoredCertificate() { }
        public string Pfx { get; set; } = "";
        public string Password { get; set; } = "";
    }
    private sealed class ReplayWindow
    {
        private readonly HashSet<string> seen = new(StringComparer.Ordinal);
        private readonly Queue<string> order = new();
        public bool TryAdd(string nonce)
        {
            if (nonce.Length is < 20 or > 80 || !Regex.IsMatch(nonce, @"^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant) || !seen.Add(nonce)) return false;
            order.Enqueue(nonce);
            while (order.Count > 512) seen.Remove(order.Dequeue());
            return true;
        }
    }
    private sealed class RateWindow(DateTimeOffset start)
    {
        public DateTimeOffset StartedUtc { get; } = start;
        public int Count { get; set; }
    }

    private static class CurrentUserProtection
    {
        [StructLayout(LayoutKind.Sequential)] private struct DataBlob { public int Length; public IntPtr Data; }
        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref DataBlob input, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref DataBlob input, out IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
        private const uint UiForbidden = 0x1;
        public static byte[] Protect(byte[] clear) => Transform(clear, protect: true);
        public static byte[] Unprotect(byte[] encrypted) => Transform(encrypted, protect: false);
        private static byte[] Transform(byte[] input, bool protect)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI wymaga Windows.");
            GCHandle pin = GCHandle.Alloc(input, GCHandleType.Pinned);
            var source = new DataBlob { Length = input.Length, Data = pin.AddrOfPinnedObject() };
            DataBlob output = default;
            IntPtr description = IntPtr.Zero;
            try
            {
                bool ok = protect
                    ? CryptProtectData(ref source, "SentinelX Phone Bridge", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output)
                    : CryptUnprotectData(ref source, out description, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output);
                if (!ok) throw new CryptographicException(Marshal.GetLastWin32Error());
                var result = new byte[output.Length];
                Marshal.Copy(output.Data, result, 0, output.Length);
                return result;
            }
            finally
            {
                pin.Free();
                if (output.Data != IntPtr.Zero) LocalFree(output.Data);
                if (description != IntPtr.Zero) LocalFree(description);
            }
        }
    }
}
