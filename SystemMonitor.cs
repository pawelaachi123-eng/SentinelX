using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace SentinelX;

/// <summary>Thread-safe sampling. Unavailable measurements are NaN, never a false 0%.</summary>
public class SystemMonitor : IDisposable
{
    private readonly object gate = new();
    private ulong previousIdle, previousKernel, previousUser;
    private long cpuSampleTick = long.MinValue;
    private long memorySampleTick = long.MinValue;
    private bool primed;
    private float cpu = float.NaN;
    private float gpu = float.NaN;
    private long gpuSampleTick = long.MinValue;
    private readonly Dictionary<string, CounterSample> gpuSamples = new();
    private bool gpuSampling, disposed;
    private MEMORYSTATUSEX? memory;

    public string LastCpuError { get; private set; } = "Trwa pierwszy pomiar CPU.";
    public string LastGpuError { get; private set; } = "Pomiar GPU nie był jeszcze wykonany.";
    public string LastMemoryError { get; private set; } = string.Empty;
    public bool IsCpuAvailable { get { lock (gate) return float.IsFinite(cpu); } }
    public bool IsMemoryAvailable { get { lock (gate) return memory != null; } }

    public SystemMonitor() => GetCpuUsage();

    /// <summary>2.0 · Per-core CPU usage. Returns null when PerformanceCounter unavailable.</summary>
    public IReadOnlyList<double>? GetPerCoreCpu()
    {
        try
        {
            int cores = Environment.ProcessorCount;
            var values = new double[cores];
            if (!OperatingSystem.IsWindows()) return null;
            // Build counters lazily
            lock (perCoreGate)
            {
                if (perCoreCounters == null)
                {
                    perCoreCounters = new PerformanceCounter[cores];
                    perCoreNextSample = new long[cores];
                    for (int i = 0; i < cores; i++)
                    {
                        try { perCoreCounters[i] = new PerformanceCounter("Processor", "% User Time", i.ToString()); }
                        catch { perCoreCounters[i] = null; }
                    }
                    // Prime counters
                    foreach (var c in perCoreCounters) c?.NextValue();
                    // Return null first run so next call has a valid delta
                    return null;
                }
            }
            for (int i = 0; i < perCoreCounters.Length; i++)
            {
                var c = perCoreCounters[i];
                if (c == null) values[i] = double.NaN;
                else
                {
                    try { values[i] = (float)Math.Clamp(c.NextValue() + 0, 0, 100); }
                    catch { values[i] = double.NaN; }
                }
            }
            return values;
        }
        catch { return null; }
    }
    private PerformanceCounter[]? perCoreCounters;
    private long[]? perCoreNextSample;
    private readonly object perCoreGate = new();

    public float GetCpuUsage()
    {
        lock (gate)
        {
            long now = Environment.TickCount64;
            if (cpuSampleTick != long.MinValue && now - cpuSampleTick < 250) return cpu;
            cpuSampleTick = now;
            try
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Pomiar wymaga Windows.");
                if (!GetSystemTimes(out var idle, out var kernel, out var user))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                ulong i = idle.Value, k = kernel.Value, u = user.Value;
                if (primed && k >= previousKernel && u >= previousUser && i >= previousIdle)
                {
                    ulong total = (k - previousKernel) + (u - previousUser);
                    ulong idleDelta = i - previousIdle;
                    if (total > 0)
                    {
                        cpu = (float)Math.Clamp(100d * (total - Math.Min(total, idleDelta)) / total, 0, 100);
                        LastCpuError = string.Empty;
                    }
                }
                previousIdle = i; previousKernel = k; previousUser = u; primed = true;
            }
            catch (Exception ex)
            {
                cpu = float.NaN; primed = false; LastCpuError = ex.Message;
            }
            return cpu;
        }
    }

    public double GetTotalRamGB() => GetMemoryStatus() is { } value ? value.TotalPhysical / 1073741824d : double.NaN;
    public double GetAvailableRamGB() => GetMemoryStatus() is { } value ? value.AvailablePhysical / 1073741824d : double.NaN;
    public double GetUsedRamGB() => GetMemoryStatus() is { } value ? (value.TotalPhysical - value.AvailablePhysical) / 1073741824d : double.NaN;
    public double GetRamUsagePercent() => GetMemoryStatus() is { } value ? 100d * (value.TotalPhysical - value.AvailablePhysical) / value.TotalPhysical : double.NaN;

    public float GetGpuUsagePercent()
    {
        lock (gate)
        {
            long now = Environment.TickCount64;
            if (disposed || gpuSampling || gpuSampleTick != long.MinValue && now - gpuSampleTick < 2000) return gpu;
            gpuSampleTick = now;
            gpuSampling = true;
            _ = Task.Run(SampleGpu);
            return gpu;
        }
    }

    private void SampleGpu()
    {
        try
        {
            var data = new PerformanceCounterCategory("GPU Engine").ReadCategory()["Utilization Percentage"];
            var engines = new Dictionary<string, float>();
            var current = new Dictionary<string, CounterSample>();
            foreach (InstanceData item in data.Values)
            {
                string name = item.InstanceName;
                if (!name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase) && !name.Contains("engtype_Compute", StringComparison.OrdinalIgnoreCase)) continue;
                current[name] = item.Sample;
                if (!gpuSamples.TryGetValue(name, out var before)) continue;
                float value = CounterSample.Calculate(before, item.Sample);
                if (!float.IsFinite(value) || value < 0) continue;
                int adapter = name.IndexOf("luid_", StringComparison.Ordinal);
                string key = adapter >= 0 ? name[adapter..] : name;
                engines[key] = engines.GetValueOrDefault(key) + value;
            }
            gpuSamples.Clear(); foreach (var item in current) gpuSamples[item.Key] = item.Value;
            lock (gate)
            {
                if (disposed) return;
                gpu = engines.Count > 0 ? Math.Clamp(engines.Values.Max(), 0, 100) : float.NaN;
                LastGpuError = engines.Count > 0 ? "" : "Oczekiwanie na dwa pomiary licznika GPU.";
            }
        }
        catch (Exception ex) { lock (gate) { gpu = float.NaN; LastGpuError = ex.Message; } }
        finally { lock (gate) gpuSampling = false; }
    }

    private MEMORYSTATUSEX? GetMemoryStatus()
    {
        lock (gate)
        {
            long now = Environment.TickCount64;
            if (memorySampleTick != long.MinValue && now - memorySampleTick < 250) return memory;
            memorySampleTick = now;
            try
            {
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Pomiar wymaga Windows.");
                var value = new MEMORYSTATUSEX();
                if (!GlobalMemoryStatusEx(value)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (value.TotalPhysical == 0 || value.AvailablePhysical > value.TotalPhysical)
                    throw new InvalidOperationException("System zwrócił nieprawidłowy pomiar pamięci.");
                memory = value; LastMemoryError = string.Empty;
            }
            catch (Exception ex) { memory = null; LastMemoryError = ex.Message; }
            return memory;
        }
    }

    public void Dispose() { lock (gate) disposed = true; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint Low, High;
        public readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private sealed class MEMORYSTATUSEX
    {
        public uint Length = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
        public uint MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile;
        public ulong TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out NativeFileTime idle, out NativeFileTime kernel, out NativeFileTime user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX buffer);
}
