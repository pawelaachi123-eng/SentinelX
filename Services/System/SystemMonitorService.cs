using System.Diagnostics;
using System.IO;
using SentinelX.Models;
using SentinelX.Services.Settings;

namespace SentinelX.Services.Monitoring;

/// <summary>Sequential sampling on one worker; lifecycle and observer failures cannot create duplicate loops.</summary>
public sealed class SystemMonitorService(SystemMonitor monitor, Services.Gaming.IGamingService gaming,
    Services.Network.INetworkService network, ISettingsService settings) : ISystemMonitorService, IDisposable
{
    private const int MaxTrackedProcesses = 4096;
    private readonly object lifecycleGate = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<int, (DateTime Start, TimeSpan Cpu)> previous = [];
    private long previousTick;
    private Task? loop;
    private SystemSnapshot current = SystemSnapshot.Empty;
    private bool disposed;

    public SystemSnapshot Current => Volatile.Read(ref current);
    public event Action<SystemSnapshot>? Updated;

    public void Start()
    {
        lock (lifecycleGate)
        {
            if (disposed || loop != null) return;
            loop = Task.Run(() => SampleLoopAsync(lifetime.Token));
        }
    }

    private async Task SampleLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var processes = ReadProcesses(token);
                var disks = new List<DiskSnapshot>();
                foreach (var disk in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
                {
                    token.ThrowIfCancellationRequested();
                    try { if (disk.IsReady) disks.Add(new(disk.Name, (disk.TotalSize - disk.TotalFreeSpace) / 1073741824d, disk.TotalSize / 1073741824d)); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                string game = gaming.GetRunningGame();
                var snapshot = new SystemSnapshot(DateTime.Now, monitor.GetCpuUsage(), monitor.GetUsedRamGB(),
                    monitor.GetTotalRamGB(), monitor.GetGpuUsagePercent(), game,
                    network.GetNetworkSummary(), disks.ToArray(), processes)
                {
                    SampleError = null,
                    GamingDetectionAvailable = gaming.DetectionAvailable
                };
                Volatile.Write(ref current, snapshot);
                Publish(snapshot);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                AppLog.Write("System", "Warning", "A system telemetry sample failed; retaining the last successful readings.", ex);
                var failed = Current with { SampleError = Limit(ex.Message, 500) };
                Volatile.Write(ref current, failed);
                Publish(failed);
            }

            if (token.IsCancellationRequested) break;
            int seconds;
            try
            {
                seconds = !Current.GamingDetectionAvailable || !string.IsNullOrEmpty(Current.Game)
                    ? settings.Current.Resources.GamingMonitorIntervalSeconds
                    : settings.Current.Resources.MonitorIntervalSeconds;
            }
            catch (Exception ex)
            {
                AppLog.Write("System", "Warning", "Could not read the telemetry interval; using the low-frequency default.", ex);
                seconds = 5;
            }
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 30)), token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
        }
    }

    private IReadOnlyList<ProcessSnapshot> ReadProcesses(CancellationToken token)
    {
        long tick = Environment.TickCount64;
        double elapsed = previousTick == 0 ? 0 : (tick - previousTick) / 1000d;
        var next = new Dictionary<int, (DateTime Start, TimeSpan Cpu)>();
        var rows = new List<ProcessSnapshot>();
        Process[] processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    int pid = process.Id;
                    string name = process.ProcessName;
                    double ram = process.WorkingSet64 / 1048576d;
                    double? cpu = null;
                    try
                    {
                        var start = process.StartTime;
                        var total = process.TotalProcessorTime;
                        if (elapsed > 0 && previous.TryGetValue(pid, out var last) && last.Start == start)
                            cpu = Math.Clamp((total - last.Cpu).TotalSeconds / elapsed / Environment.ProcessorCount * 100, 0, 100);
                        if (next.Count < MaxTrackedProcesses) next[pid] = (start, total);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or NotSupportedException) { }
                    rows.Add(new(name, pid, ram, cpu));
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException) { }
            }
        }
        finally
        {
            foreach (Process process in processes)
            {
                try { process.Dispose(); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
        previous.Clear();
        foreach (var pair in next) previous[pair.Key] = pair.Value;
        previousTick = tick;
        return rows.OrderByDescending(x => x.MemoryMb).Take(40).ToArray();
    }

    private void Publish(SystemSnapshot snapshot)
    {
        foreach (Action<SystemSnapshot> observer in Updated?.GetInvocationList() ?? [])
        {
            try { observer(snapshot); }
            catch (Exception ex) { AppLog.Write("System", "Warning", "A system-monitor observer failed.", ex); }
        }
    }

    private static string Limit(string? value, int maximum) => string.IsNullOrEmpty(value)
        ? "Nieznany błąd pomiaru."
        : value.Length <= maximum ? value : value[..maximum] + "…";

    public void Dispose()
    {
        Task? running;
        lock (lifecycleGate)
        {
            if (disposed) return;
            disposed = true;
            running = loop;
        }
        lifetime.Cancel();
        if (running == null) lifetime.Dispose();
        else _ = running.ContinueWith(_ => lifetime.Dispose(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
