using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SentinelX.Core;
using SentinelX.Services.Base;
using SentinelX.Services.Maintenance;

namespace SentinelX.Services.Agent;

/// <summary>Independent headless Agent: owns the Base connection and the Agent queue
/// (single writer), serves the UI over an authenticated named pipe, and writes a
/// heartbeat file a watchdog can verify. No windows, no microphone.</summary>
public sealed class AgentHost : IAsyncDisposable
{
    private readonly string root;
    private readonly string token;
    private readonly ServiceProvider provider;
    private readonly WindowsBaseService direct;
    private readonly Mutex mutex;
    private readonly AgentPipeServer pipe;
    private readonly CancellationTokenSource stop = new();
    private readonly Task pipeTask;
    private readonly Task beatTask;
    private readonly string logPath;
    private bool stopped;

    private AgentHost(string root, string token, ServiceProvider provider, Mutex mutex)
    {
        this.root = root;
        this.token = token;
        this.provider = provider;
        this.mutex = mutex;
        direct = provider.GetRequiredService<WindowsBaseService>();
        direct.Headless = true;
        pipe = new AgentPipeServer(AgentAuth.PipeName(root), () => token, DispatchAsync);
        logPath = Path.Combine(AppPaths.LogsDirectory, "agent.log");
        pipeTask = pipe.RunAsync(stop.Token);
        beatTask = BeatAsync(stop.Token);
    }

    public static async Task<AgentHost> StartAsync()
    {
        string root = AppPaths.Root;
        var mutex = new Mutex(true, AgentAuth.MutexName(root), out bool created);
        if (!created)
        {
            mutex.Dispose();
            throw new AgentException("already_running");
        }
        string token = AgentAuth.CreateToken();
        AgentAuth.SaveToken(root, token, value => ProtectedData.Protect(value, null, DataProtectionScope.CurrentUser));
        var provider = ServiceLocator.BuildAgent();
        var host = new AgentHost(root, token, provider, mutex);
        host.Log("agent started pid=" + Environment.ProcessId + " version=" + AppConstants.SemanticVersion);
        try { RunHealth.BeginRun(root, "agent"); } catch { }
        try
        {
            host.direct.Start();
        }
        catch (Exception e)
        {
            host.Log("base start: " + e.GetType().Name);
        }
        await host.WriteHeartbeatAsync(CancellationToken.None);
        return host;
    }

    private async Task<object> DispatchAsync(AgentRequest request, CancellationToken cancel)
    {
        try
        {
            switch (request.Op)
            {
                case "hello":
                case "ping":
                    return new { version = AppConstants.SemanticVersion, pid = Environment.ProcessId };
                case "status":
                    return StatusObject();
                case "tasks":
                    return await direct.TasksAsync();
                case "config":
                    return (object?)direct.PublicConfiguration ?? new { };
                case "pair":
                {
                    var identity = request.Payload.Deserialize<BaseIdentity>(AgentProtocol.Json)
                        ?? throw new AgentException("identity");
                    identity.Validate();
                    await direct.PairAsync(identity, cancel);
                    Log("pair base=" + identity.BaseId + " device=" + identity.DeviceId);
                    return new { paired = true };
                }
                case "unpair":
                    await direct.UnpairAsync();
                    Log("unpair");
                    return new { unpaired = true };
                case "request":
                {
                    string type = request.Payload.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                        ? t.GetString() ?? "" : "";
                    if (type.Length is < 1 or > 128) throw new AgentException("type");
                    JsonElement data = request.Payload.TryGetProperty("data", out var d) ? d : JsonSerializer.SerializeToElement(new { });
                    return await direct.RequestAsync(type, data, cancel);
                }
                case "repair-queue":
                {
                    string backup = await direct.RepairQueueAsync();
                    Log("repair-queue");
                    return backup;
                }
                case "health":
                    return new { queueError = direct.QueueStorageErrorText, paired = direct.PublicConfiguration != null, version = AppConstants.SemanticVersion };
                case "diagnose":
                    return await provider.GetRequiredService<MaintenanceService>().DiagnoseAsync("", cancel);
                case "stop":
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(500);
                        await StopAsync();
                    });
                    return new { stopping = true };
                default:
                    throw new AgentException("unknown_op");
            }
        }
        catch (Sx4Exception e)
        {
            throw new AgentException(e.Code);
        }
    }

    private object StatusObject()
    {
        var gaming = provider.GetRequiredService<GamingModeService>();
        return new
        {
            status = direct.Status,
            version = AppConstants.SemanticVersion,
            pid = Environment.ProcessId,
            gaming = gaming.IsGaming(),
            config = direct.PublicConfiguration,
            capabilities = direct.Capabilities,
            metrics = Metrics()
        };
    }

    private object Metrics()
    {
        var monitor = provider.GetRequiredService<SystemMonitor>();
        static double? Finite(double x) => double.IsFinite(x) ? x : null;
        static float? Finite32(float x) => float.IsFinite(x) ? x : null;
        return new
        {
            cpu = Finite32(monitor.GetCpuUsage()),
            ramUsedGb = Finite(monitor.GetUsedRamGB()),
            ramTotalGb = Finite(monitor.GetTotalRamGB()),
            gpu = Finite32(monitor.GetGpuUsagePercent()),
            uptimeMs = Environment.TickCount64
        };
    }

    private async Task BeatAsync(CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            int interval = 15;
            try
            {
                if (provider.GetRequiredService<GamingModeService>().IsGaming()) interval = 60;
            }
            catch { }
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(interval), cancel);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { break; }
            try
            {
                await WriteHeartbeatAsync(cancel);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException) { }
            try
            {
                await provider.GetRequiredService<OllamaSupervisor>().ReleaseIdleModelsAsync(30, cancel);
            }
            catch (Exception e) when (e is IOException or OperationCanceledException) { }
            if (!direct.OwnsBase)
            {
                try { direct.Start(); } catch { }
                if (direct.OwnsBase) Log("base loops acquired after the other process released them");
            }
        }
    }

    private async Task WriteHeartbeatAsync(CancellationToken cancel)
    {
        var gaming = provider.GetRequiredService<GamingModeService>();
        object beat = new
        {
            time = DateTimeOffset.UtcNow,
            pid = Environment.ProcessId,
            version = AppConstants.SemanticVersion,
            status = direct.Status,
            gaming = gaming.IsGaming(),
            metrics = Metrics()
        };
        string path = AgentAuth.HeartbeatPath(root);
        Directory.CreateDirectory(AgentAuth.AgentDirectory(root));
        string temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(beat, AgentProtocol.Json), cancel);
        File.Move(temp, path, true);
    }

    private void Log(string line)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogsDirectory);
            if (File.Exists(logPath) && new FileInfo(logPath).Length > 1024 * 1024)
                File.Move(logPath, logPath + ".previous", true);
            File.AppendAllText(logPath, DateTimeOffset.Now.ToString("O") + " " + line + "\n");
        }
        catch { }
    }

    public async Task StopAsync()
    {
        if (stopped) return;
        stopped = true;
        Log("agent stopping");
        try { RunHealth.EndRun(root, "agent"); } catch { }
        stop.Cancel();
        try { await pipeTask; } catch (OperationCanceledException) { } catch (Exception) { }
        try { await beatTask; } catch (OperationCanceledException) { } catch (Exception) { }
        await pipe.DisposeAsync();
        try { await direct.StopAsync(); } catch { }
        try { provider.Dispose(); } catch { }
        try
        {
            string path = AgentAuth.TokenPath(root);
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
        try { mutex.ReleaseMutex(); } catch { }
        mutex.Dispose();
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
