using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using SentinelX.Models;
using SentinelX.Services.Settings;

namespace SentinelX.Services.Monitoring;

/// <summary>Sequential sampling on a worker, never overlapping callbacks or WMI on the UI thread.</summary>
public sealed class SystemMonitorService(SystemMonitor monitor, Services.Gaming.IGamingService gaming,
    Services.Network.INetworkService network, ISettingsService settings) : ISystemMonitorService, IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<int, (DateTime Start, TimeSpan Cpu)> previous = [];
    private long previousTick;
    private long previousNetBytes;
    private DateTime previousNetTime = DateTime.MinValue;
    private Task? loop;
    private SystemSnapshot current = SystemSnapshot.Empty;
    public SystemSnapshot Current => Volatile.Read(ref current);
    public event Action<SystemSnapshot>? Updated;
    public void Start() => loop ??= Task.Run(SampleLoopAsync);

    private async Task SampleLoopAsync()
    {
        // Prime per-core counters
        monitor.GetPerCoreCpu();
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                var processes = ReadProcesses();
                var disks = new List<DiskSnapshot>();
                foreach (var disk in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
                {
                    try { if (disk.IsReady) disks.Add(new(disk.Name, (disk.TotalSize - disk.TotalFreeSpace) / 1073741824d, disk.TotalSize / 1073741824d)); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }

                // Per-core
                var rawCores = monitor.GetPerCoreCpu();
                List<CoreSnapshot>? cores = null;
                if (rawCores != null)
                {
                    cores = new List<CoreSnapshot>(rawCores.Count);
                    foreach (var v in rawCores) cores.Add(new CoreSnapshot(double.IsFinite(v) ? v : 0, 0));
                }

                // Network bandwidth (Mbps)
                double netDown = 0, _ = 0;
                try
                {
                    long bytes = 0;
                    foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (iface.OperationalStatus == OperationalStatus.Up
                            && iface.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
                        {
                            try { bytes += iface.GetIPStatistics().BytesReceived + iface.GetIPStatistics().BytesSent; }
                            catch { }
                        }
                    }
                    var now = DateTime.UtcNow;
                    if (previousNetTime != DateTime.MinValue)
                    {
                        double secs = (now - previousNetTime).TotalSeconds;
                        if (secs > 0.1)
                        {
                            double bps = (bytes - previousNetBytes) * 8.0 / secs; // bits/s total
                            // Rough split: assume down > up typical — for simplicity show total as "down" (since we can't split cheaply)
                            netDown = bps / 1_000_000.0;
                        }
                    }
                    previousNetBytes = bytes;
                    previousNetTime = now;
                }
                catch { }

                var snapshot = new SystemSnapshot(DateTime.Now, monitor.GetCpuUsage(), monitor.GetUsedRamGB(),
                    monitor.GetTotalRamGB(), monitor.GetGpuUsagePercent(), gaming.GetRunningGame(),
                    network.GetNetworkSummary(), disks, processes, cores,
                    NetworkDownMbps: netDown);
                Volatile.Write(ref current, snapshot);
                Updated?.Invoke(snapshot);
            }
            catch (Exception ex) { AppLog.Write(ex); }
            int seconds = string.IsNullOrEmpty(Current.Game) ? settings.Current.Resources.MonitorIntervalSeconds : settings.Current.Resources.GamingMonitorIntervalSeconds;
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 30)), lifetime.Token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private IReadOnlyList<ProcessSnapshot> ReadProcesses()
    {
        long tick = Environment.TickCount64;
        double elapsed = (tick - previousTick) / 1000d;
        var next = new Dictionary<int, (DateTime Start, TimeSpan Cpu)>();
        var rows = new List<ProcessSnapshot>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
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
                        next[pid] = (start, total);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
                    rows.Add(new(name, pid, ram, cpu));
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
        previous.Clear(); foreach (var pair in next) previous[pair.Key] = pair.Value;
        previousTick = tick;
        return rows.OrderByDescending(x => x.MemoryMb).Take(40).ToArray();
    }
    public void Dispose()
    {
        lifetime.Cancel();
        // Dispose only after the loop has stopped using the token.
        if (loop is { } task) _ = task.ContinueWith(_ => lifetime.Dispose(), TaskScheduler.Default);
        else lifetime.Dispose();
    }
}
