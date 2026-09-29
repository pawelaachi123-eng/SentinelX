using System.Collections.Generic;

namespace SentinelX;

public sealed record PerformanceSample(DateTimeOffset CapturedAt, float CpuPercent, double RamPercent,
    double UsedRamGb, double TotalRamGb, float GpuPercent, string RunningGame);

/// <summary>Small in-memory performance ring buffer. Sampling is throttled to two seconds and never writes telemetry to disk.</summary>
public sealed class PerformanceHistoryService
{
    private const int MaximumSamples = 180; // six minutes at the normal interval
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(2);
    private readonly object gate = new();
    private readonly SystemMonitor monitor;
    private readonly GamingModeService? gaming;
    private readonly Queue<PerformanceSample> samples = new();
    private DateTimeOffset lastSampleAt;

    public PerformanceHistoryService(SystemMonitor monitor, GamingModeService? gaming = null)
    { this.monitor = monitor; this.gaming = gaming; }

    public PerformanceSample SampleIfDue(DateTimeOffset? now = null)
    {
        DateTimeOffset timestamp = now ?? DateTimeOffset.Now;
        lock (gate)
        {
            if (samples.Count > 0 && timestamp - lastSampleAt < MinimumInterval) return samples.Last();
            var sample = new PerformanceSample(timestamp, monitor.GetCpuUsage(), monitor.GetRamUsagePercent(),
                monitor.GetUsedRamGB(), monitor.GetTotalRamGB(), monitor.GetGpuUsagePercent(), gaming?.GetRunningGame() ?? "");
            samples.Enqueue(sample);
            while (samples.Count > MaximumSamples) samples.Dequeue();
            lastSampleAt = timestamp;
            return sample;
        }
    }

    public IReadOnlyList<PerformanceSample> GetRecent(TimeSpan? window = null)
    {
        DateTimeOffset cutoff = DateTimeOffset.Now - (window ?? TimeSpan.FromMinutes(6));
        lock (gate) return samples.Where(x => x.CapturedAt >= cutoff).ToArray();
    }

    public string FormatRecentWindow(TimeSpan? window = null)
    {
        var recent = GetRecent(window ?? TimeSpan.FromMinutes(3));
        if (recent.Count == 0) return "Brak próbek historycznych — monitoring działa tylko podczas uruchomionej aplikacji.";
        var cpu = recent.Where(x => float.IsFinite(x.CpuPercent)).ToArray();
        var ram = recent.Where(x => double.IsFinite(x.RamPercent)).ToArray();
        var gpu = recent.Where(x => float.IsFinite(x.GpuPercent)).ToArray();
        var text = new System.Text.StringBuilder($"Ostatnie {recent.Count} próbek od {recent[0].CapturedAt:HH:mm:ss} do {recent[^1].CapturedAt:HH:mm:ss} (bufor w RAM, ok. 6 min maks.):\n");
        var cpuPeak = cpu.OrderByDescending(x => x.CpuPercent).FirstOrDefault();
        var ramPeak = ram.OrderByDescending(x => x.RamPercent).FirstOrDefault();
        var gpuPeak = gpu.OrderByDescending(x => x.GpuPercent).FirstOrDefault();
        text.AppendLine(cpu.Length > 0 ? $"CPU: teraz {cpu[^1].CpuPercent:0}% · maksimum {cpuPeak.CpuPercent:0}% o {cpuPeak.CapturedAt:HH:mm:ss}" : "CPU: brak danych");
        text.AppendLine(ram.Length > 0 ? $"RAM: teraz {ram[^1].RamPercent:0}% · maksimum {ramPeak.RamPercent:0}% o {ramPeak.CapturedAt:HH:mm:ss}" : "RAM: brak danych");
        text.AppendLine(gpu.Length > 0 ? $"GPU (licznik Windows): teraz {gpu[^1].GpuPercent:0}% · maksimum {gpuPeak.GpuPercent:0}% o {gpuPeak.CapturedAt:HH:mm:ss}" : "GPU: brak dostępnego pomiaru licznika");
        text.AppendLine("Temperatury, VRAM, taktowania, dysk aktywny, frametime i FPS nie są w tej konfiguracji mierzone.");
        return text.ToString().Trim();
    }
}
