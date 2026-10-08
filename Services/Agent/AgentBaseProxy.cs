using System.Text.Json;
using SentinelX.Services.Base;

namespace SentinelX.Services.Agent;

/// <summary>UI-side IBaseControl over the Agent pipe. Never touches
/// queue.bin/identity.bin — the Agent process is the single writer.</summary>
public sealed class AgentBaseProxy : IBaseControl
{
    private readonly AgentClient client;
    private readonly object gate = new();
    private CancellationTokenSource? poll;
    private Task? loop;
    private string status = "Agent uruchamia się…";
    private BaseIdentity? config;
    private string[] capabilities = [];
    private bool disposed;

    public event Action? Changed;

    public AgentBaseProxy(AgentClient client) { this.client = client; }

    public string Status { get { lock (gate) return status; } }
    public BaseIdentity? PublicConfiguration { get { lock (gate) return config; } }
    public IReadOnlyList<string> Capabilities { get { lock (gate) return capabilities.ToArray(); } }

    public void Start()
    {
        if (loop is { IsCompleted: false } || disposed) return;
        poll = new CancellationTokenSource();
        loop = Task.Run(() => PollAsync(poll.Token));
    }

    public async Task StopAsync()
    {
        poll?.Cancel();
        if (loop != null) try { await loop; } catch (OperationCanceledException) { }
        poll?.Dispose();
        poll = null;
    }

    public async Task RefreshAsync(CancellationToken cancel)
    {
        var data = await client.SendAsync("status", new { }, cancel);
        string next = data.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "" : "";
        BaseIdentity? nextConfig = data.TryGetProperty("config", out var c) && c.ValueKind == JsonValueKind.Object
            ? c.Deserialize<BaseIdentity>(AgentProtocol.Json) : null;
        string[] nextCaps = data.TryGetProperty("capabilities", out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray() : [];
        bool changed;
        lock (gate)
        {
            changed = next != status;
            status = next;
            config = nextConfig;
            capabilities = nextCaps;
        }
        if (changed) Changed?.Invoke();
    }

    private async Task PollAsync(CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(cancel);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { break; }
            catch (Exception)
            {
                lock (gate) status = "Agent niedostępny — sprawdzam…";
                Changed?.Invoke();
            }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancel);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { break; }
        }
    }

    public async Task PairAsync(BaseIdentity next, CancellationToken cancel)
    {
        next.Validate();
        await client.SendAsync("pair", next, cancel);
        await RefreshAsync(cancel);
    }

    public async Task UnpairAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        await client.SendAsync("unpair", new { }, timeout.Token);
        await RefreshAsync(timeout.Token);
    }

    public async Task<IReadOnlyList<AgentTask>> TasksAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var data = await client.SendAsync("tasks", new { }, timeout.Token);
        return data.ValueKind == JsonValueKind.Array
            ? data.Deserialize<List<AgentTask>>(AgentProtocol.Json) ?? [] : [];
    }

    public async Task<BaseMessage> RequestAsync(string type, object data, CancellationToken cancel)
    {
        var reply = await client.SendAsync("request", new { type, data }, cancel);
        return reply.ValueKind == JsonValueKind.Object
            ? reply.Deserialize<BaseMessage>(AgentProtocol.Json) ?? throw new AgentException("protocol")
            : throw new AgentException("protocol");
    }

    public async Task<string> RepairQueueAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var data = await client.SendAsync("repair-queue", new { }, timeout.Token);
        return data.ValueKind == JsonValueKind.String ? data.GetString() ?? "" : data.ToString();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { StopAsync().GetAwaiter().GetResult(); } catch { }
    }
}
