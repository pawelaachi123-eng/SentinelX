using System.Collections.Generic;

namespace SentinelX;

public sealed record PerformanceSample(DateTimeOffset CapturedAt, float CpuPercent, double RamPercent,
    double UsedRamGb, double TotalRamGb, float GpuPercent, string RunningGame);

/// <summary>
/// Fixed-size, process-local performance black box. The sampler only runs while the app is alive,
/// writes no telemetry to disk, and retains at most six minutes of coarse samples.
/// </summary>
public sealed class PerformanceHistoryService : IDisposable
{
    private const int MaximumSamples = 180; // six minutes at two-second cadence
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(2);
    private readonly object gate = new();
    private readonly SystemMonitor monitor;
    private readonly GamingModeService? gaming;
    private readonly Queue<PerformanceSample> samples = new();
    private Timer? sampler;
    private DateTimeOffset lastSampleAt;
    private int sampling;
    private bool disposed;

    public PerformanceHistoryService(SystemMonitor monitor, GamingModeService? gaming = null)
    { this.monitor = monitor; this.gaming = gaming; }

    public bool IsRunning { get { lock (gate) return sampler != null; } }
    public event Action<PerformanceSample>? Sampled;

    /// <summary>Starts low-frequency sampling for the duration of the application process.</summary>
    public void Start()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            sampler ??= new Timer(SampleTick, null, TimeSpan.Zero, MinimumInterval);
        }
    }

    public PerformanceSample SampleIfDue(DateTimeOffset? now = null)
    {
        DateTimeOffset timestamp = now ?? DateTimeOffset.Now;
        PerformanceSample sample;
        bool added = false;
        lock (gate)
        {
            if (samples.Count > 0 && timestamp - lastSampleAt < MinimumInterval) return samples.Last();
            sample = new PerformanceSample(timestamp, monitor.GetCpuUsage(), monitor.GetRamUsagePercent(),
                monitor.GetUsedRamGB(), monitor.GetTotalRamGB(), monitor.GetGpuUsagePercent(), gaming?.GetRunningGame() ?? "");
            samples.Enqueue(sample);
            while (samples.Count > MaximumSamples) samples.Dequeue();
            lastSampleAt = timestamp;
            added = true;
        }
        if (added)
        {
            try { Sampled?.Invoke(sample); }
            catch (Exception ex) { AppLog.Write(ex); }
        }
        return sample;
    }

    private void SampleTick(object? state)
    {
        if (Interlocked.Exchange(ref sampling, 1) != 0) return;
        try { SampleIfDue(); }
        catch (Exception ex) { AppLog.Write(ex); }
        finally { Volatile.Write(ref sampling, 0); }
    }

    public IReadOnlyList<PerformanceSample> GetRecent(TimeSpan? window = null)
    {
        DateTimeOffset cutoff = DateTimeOffset.Now - (window ?? TimeSpan.FromMinutes(6));
        lock (gate) return samples.Where(x => x.CapturedAt >= cutoff).ToArray();
    }

    /// <summary>Returns the ordered short window around a user's recent symptom, without inventing unavailable metrics.</summary>
    public string FormatIncident(TimeSpan? lookback = null)
    {
        TimeSpan duration = lookback ?? TimeSpan.FromSeconds(45);
        var recent = GetRecent(duration);
        if (recent.Count == 0)
            return "Nie mam próbek sprzed zdarzenia. Black Box gromadzi dane tylko od uruchomienia Sentinel i tylko w RAM.";

        var text = new System.Text.StringBuilder(
            $"Black Box: {recent.Count} próbek z ostatnich {(int)duration.TotalSeconds} s, {recent[0].CapturedAt:HH:mm:ss}–{recent[^1].CapturedAt:HH:mm:ss}. Dane są tylko w RAM.\n");
        foreach (var sample in recent.TakeLast(24))
        {
            text.Append($"{sample.CapturedAt:HH:mm:ss} · CPU {Format(sample.CpuPercent)} · RAM {Format(sample.RamPercent)}");
            if (double.IsFinite(sample.UsedRamGb) && double.IsFinite(sample.TotalRamGb))
                text.Append($" ({sample.UsedRamGb:0.0}/{sample.TotalRamGb:0.0} GiB)");
            text.Append($" · GPU {Format(sample.GpuPercent)}");
            if (!string.IsNullOrWhiteSpace(sample.RunningGame)) text.Append(" · gra: " + sample.RunningGame);
            text.AppendLine();
        }
        text.Append("Brak w tym Black Box temperatur, VRAM, taktowań, dysku, sieci, FPS i frametime; korelacja zasobów nie dowodzi przyczyny.");
        return text.ToString().Trim();
    }

    public string FormatRecentWindow(TimeSpan? window = null)
    {
        var recent = GetRecent(window ?? TimeSpan.FromMinutes(3));
        if (recent.Count == 0) return "Brak próbek historycznych — Black Box działa tylko podczas uruchomionej aplikacji.";
        var cpu = recent.Where(x => float.IsFinite(x.CpuPercent)).ToArray();
        var ram = recent.Where(x => double.IsFinite(x.RamPercent)).ToArray();
        var gpu = recent.Where(x => float.IsFinite(x.GpuPercent)).ToArray();
        var text = new System.Text.StringBuilder($"Ostatnie {recent.Count} próbek od {recent[0].CapturedAt:HH:mm:ss} do {recent[^1].CapturedAt:HH:mm:ss} (bufor w RAM, maks. 6 min):\n");
        var cpuPeak = cpu.OrderByDescending(x => x.CpuPercent).FirstOrDefault();
        var ramPeak = ram.OrderByDescending(x => x.RamPercent).FirstOrDefault();
        var gpuPeak = gpu.OrderByDescending(x => x.GpuPercent).FirstOrDefault();
        text.AppendLine(cpu.Length > 0 ? $"CPU: teraz {cpu[^1].CpuPercent:0}% · maksimum {cpuPeak.CpuPercent:0}% o {cpuPeak.CapturedAt:HH:mm:ss}" : "CPU: brak danych");
        text.AppendLine(ram.Length > 0 ? $"RAM: teraz {ram[^1].RamPercent:0}% · maksimum {ramPeak.RamPercent:0}% o {ramPeak.CapturedAt:HH:mm:ss}" : "RAM: brak danych");
        text.AppendLine(gpu.Length > 0 ? $"GPU (licznik Windows): teraz {gpu[^1].GpuPercent:0}% · maksimum {gpuPeak.GpuPercent:0}% o {gpuPeak.CapturedAt:HH:mm:ss}" : "GPU: brak dostępnego pomiaru licznika");
        text.AppendLine("Temperatury, VRAM, taktowania, dysk aktywny, frametime i FPS nie są w tej konfiguracji mierzone.");
        return text.ToString().Trim();
    }

    public void Dispose()
    {
        Timer? old;
        lock (gate) { if (disposed) return; disposed = true; old = sampler; sampler = null; }
        old?.Dispose();
    }

    private static string Format(double value) => double.IsFinite(value)
        ? value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%"
        : "niedostępne";
}
