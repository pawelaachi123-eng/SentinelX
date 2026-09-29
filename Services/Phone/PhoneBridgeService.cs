using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SentinelX.Services.Phone;

/// <summary>Realny most TCP komputer ↔ Android. Protokół binarny:
/// [1 bajt rodzaju][4 bajty długości LE][dane]; rodzaje: 1 = JSON, 2 = audio od rozmówcy (PCM int16 LE 16 kHz mono),
/// 3 = mowa TTS do telefonu. Po połączeniu telefon MUSI w ≤10 s wysłać JSON „hello” z poprawnym tokenem —
/// inaczej gniazdo jest zamykane. Stany połączenia przychodzą od TelephonyManager (OFFHOOK to prawdziwy sygnał systemu).</summary>
public sealed class PhoneBridgeService : IPhoneBridge, IDisposable
{
    private const byte KindJson = 1, KindAudioUp = 2, KindAudioDown = 3;

    private readonly object gate = new();
    private TcpListener? listener;
    private TcpClient? client;
    private NetworkStream? stream;
    private string token = "";
    private string device = "";
    private PhoneCallState callState = PhoneCallState.Idle;
    private DateTime offHookSince;
    private bool disposed;
    private readonly ConcurrentQueue<(byte Kind, byte[] Payload)> outgoing = new();
    private int sending;

    public bool IsConnected { get { lock (gate) return client?.Connected == true && device.Length > 0; } }
    public string ConnectedDevice { get { lock (gate) return device; } }
    public PhoneCallState CallState { get { lock (gate) return callState; } }
    public int CallDurationSeconds
    {
        get
        {
            lock (gate) return callState == PhoneCallState.OffHook && offHookSince != default
                ? (int)(DateTime.UtcNow - offHookSince).TotalSeconds : 0;
        }
    }
    public event Action<PhoneCallState>? CallStateChanged;
    public event Action<float[]>? AudioUp;
    public event Action<string>? Log;

    public string Status()
    {
        lock (gate)
        {
            if (client?.Connected != true)
                return listener == null
                    ? "Most telefoniczny: wyłączony. Włącz poleceniem „włącz most telefoniczny”, potem połącz aplikację pomocniczą na telefonie."
                    : "Most telefoniczny: nasłuchuje, ale aplikacja pomocnicza na telefonie nie jest połączona.";
            return callState switch
            {
                PhoneCallState.OffHook => $"Most: {device} · połączenie aktywne ({CallDurationSeconds} s).",
                PhoneCallState.Dialing => $"Most: {device} · wybieranie numeru…",
                _ => $"Most: {device} · połączony, gotowy do dzwonienia.",
            };
        }
    }

    public void Start(int port, string sharedToken)
    {
        lock (gate)
        {
            if (disposed) throw new ObjectDisposedException(nameof(PhoneBridgeService));
            if (listener != null && port == (listener.LocalEndpoint as IPEndPoint)?.Port) return;
            listener?.Stop();
            token = sharedToken;
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Raise("Most telefoniczny: nasłuch na porcie " + port + " (tylko po Twoim wyraźnym poleceniu).");
            _ = Task.Run(AcceptLoopAsync);
        }
    }

    public void Stop()
    {
        TcpListener? oldListener; TcpClient? oldClient;
        lock (gate)
        {
            oldListener = listener; listener = null;
            oldClient = client; client = null; stream = null; device = "";
            SetStateLocked(PhoneCallState.Idle);
        }
        try { oldClient?.Close(); } catch { }
        try { oldListener?.Stop(); } catch { }
        Raise("Most telefoniczny: wyłączony.");
    }

    private async Task AcceptLoopAsync()
    {
        while (true)
        {
            TcpClient accepted;
            try { accepted = await listener!.AcceptTcpClientAsync(); }
            catch { return; } // listener zamknięty
            TcpClient? old;
            lock (gate)
            {
                old = client;
                client = accepted;
                stream = accepted.GetStream();
                device = "";
            }
            try { old?.Close(); } catch { }
            Raise("Most telefoniczny: nowe gniazdo, oczekuję na parowanie tokenem…");
            _ = Task.Run(() => ReadLoopAsync(accepted));
        }
    }

    private async Task ReadLoopAsync(TcpClient connection)
    {
        var buffer = new byte[65536];
        try
        {
            NetworkStream net = connection.GetStream();
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (connection.Connected)
            {
                int kind = net.ReadByte();
                if (kind < 0) break;
                if (!await ReadExactlyAsync(net, buffer, 4)) break;
                int length = BitConverter.ToInt32(buffer, 0);
                if (length is < 0 or > 4_000_000) break;
                if (buffer.Length < length) buffer = new byte[Math.Max(length, buffer.Length * 2)];
                if (!await ReadExactlyAsync(net, buffer, length)) break;

                if (kind == KindJson)
                {
                    string message = Encoding.UTF8.GetString(buffer, 0, length);
                    if (!HandleJson(message))
                    {
                        Raise("Most: odrzucono połączenie — niepoprawny token parowania.");
                        break;
                    }
                    if (DateTime.UtcNow > deadline && device.Length == 0) break;
                }
                else if (kind == KindAudioUp && device.Length > 0)
                {
                    var samples = new float[length / 2];
                    for (int i = 0; i < samples.Length; i++)
                        samples[i] = BitConverter.ToInt16(buffer, i * 2) / 32768f;
                    AudioUp?.Invoke(samples);
                }
            }
        }
        catch (Exception ex) { Raise("Most: gniazdo przerwane (" + ex.GetType().Name + ")."); }
        finally
        {
            lock (gate) { if (client == connection) { client = null; stream = null; device = ""; SetStateLocked(PhoneCallState.Idle); } }
            try { connection.Close(); } catch { }
        }
    }

    private bool HandleJson(string message)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(message);
            string type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            if (type == "hello")
            {
                string given = doc.RootElement.TryGetProperty("token", out var tk) ? tk.GetString() ?? "" : "";
                if (token.Length == 0 || given != token) return false;
                string name = doc.RootElement.TryGetProperty("device", out var dv) ? dv.GetString() ?? "telefon" : "telefon";
                lock (gate) device = name;
                Raise("Most: sparowano z „" + name + "”. Można dzwonić z karty SIM telefonu.");
                SendJson("""{"type":"welcome"}""");
                return true;
            }
            if (type == "call_state")
            {
                string state = doc.RootElement.TryGetProperty("state", out var st) ? st.GetString() ?? "" : "";
                int seconds = doc.RootElement.TryGetProperty("duration", out var du) && du.TryGetInt32(out int s) ? s : 0;
                var mapped = state switch
                {
                    "DIALING" => PhoneCallState.Dialing,
                    "OFFHOOK" => PhoneCallState.OffHook,
                    "IDLE" => PhoneCallState.Ended,
                    "FAILED" => PhoneCallState.Failed,
                    _ => PhoneCallState.Idle,
                };
                lock (gate)
                {
                    callState = mapped;
                    offHookSince = mapped == PhoneCallState.OffHook
                        ? DateTime.UtcNow.AddSeconds(-seconds)
                        : default;
                }
                CallStateChanged?.Invoke(mapped);
            }
            return true;
        }
        catch { return false; }
    }

    public async Task DialAsync(string number, string callId)
    {
        if (!IsConnected) throw new InvalidOperationException("Most telefoniczny nie jest połączony z aplikacją pomocniczą.");
        SendJson(System.Text.Json.JsonSerializer.Serialize(new { type = "dial", number, callId }));
        await Task.CompletedTask;
    }

    public void SendTtsAudio(float[] samples)
    {
        if (!IsConnected || samples.Length == 0) return;
        var payload = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short clipped = Math.Clamp((short)Math.Round(samples[i] * 32767f), short.MinValue, short.MaxValue);
            payload[i * 2] = (byte)clipped;
            payload[i * 2 + 1] = (byte)((uint)clipped >> 8);
        }
        SendFrame(KindAudioDown, payload);
    }

    public void EndCall() { if (IsConnected) SendJson("""{"type":"end_call"}"""); }

    private void SendJson(string json) => SendFrame(KindJson, Encoding.UTF8.GetBytes(json));

    private void SendFrame(byte kind, byte[] payload)
    {
        outgoing.Enqueue((kind, payload));
        if (Interlocked.CompareExchange(ref sending, 1, 0) != 0) return;
        _ = Task.Run(async () =>
        {
            try
            {
                while (outgoing.TryDequeue(out var frame))
                {
                    NetworkStream? net;
                    lock (gate) net = stream;
                    if (net == null) return;
                    var head = new byte[5];
                    head[0] = frame.Kind;
                    BitConverter.GetBytes(frame.Payload.Length).CopyTo(head, 1);
                    await net.WriteAsync(head);
                    await net.WriteAsync(frame.Payload);
                    await net.FlushAsync();
                }
            }
            catch (Exception ex) { Raise("Most: nie udało się wysłać danych (" + ex.GetType().Name + ")."); }
            finally { Interlocked.Exchange(ref sending, 0); }
        });
    }

    private void SetStateLocked(PhoneCallState state)
    {
        callState = state;
        offHookSince = state == PhoneCallState.OffHook ? DateTime.UtcNow : default;
    }

    private void Raise(string message) { try { Log?.Invoke(message); } catch { } }

    private static async Task<bool> ReadExactlyAsync(NetworkStream net, byte[] buffer, int length)
    {
        int read = 0;
        while (read < length)
        {
            int chunk = await net.ReadAsync(buffer.AsMemory(read, length - read));
            if (chunk <= 0) return false;
            read += chunk;
        }
        return true;
    }

    public void Dispose()
    {
        lock (gate) disposed = true;
        Stop();
    }
}
