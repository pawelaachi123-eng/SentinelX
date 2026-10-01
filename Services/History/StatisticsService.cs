using System.Linq;
using System.Threading;
using System.Collections.Concurrent;

namespace SentinelX.Services.History;

public sealed class ToolStat
{
    public string Name { get; set; } = "";
    public long Calls;
    public long Failures;
    public long TotalDurationMs;
    public long MaxDurationMs;
    public double AvgMs => Calls == 0 ? 0 : TotalDurationMs / (double)Calls;
    public double ErrorRate => Calls == 0 ? 0 : Failures / (double)Calls;
}

public sealed class StatisticsService
{
    private readonly ConcurrentDictionary<string, ToolStat> toolStats = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> skillUsage = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<(DateTimeOffset At, string Name, long DurationMs)> aiLatency = new();
    private readonly ConcurrentQueue<(DateTimeOffset At, long DurationMs)> sttLatency = new();
    private readonly ConcurrentQueue<(DateTimeOffset At, double Cpu, long Ram)> selfSamples = new();

    public void RecordTool(string name, long durationMs, bool success)
    {
        var stat = toolStats.GetOrAdd(name, _ => new ToolStat { Name = name });
        Interlocked.Increment(ref stat.Calls);
        Interlocked.Add(ref stat.TotalDurationMs, durationMs);
        if (!success) Interlocked.Increment(ref stat.Failures);
        long cur;
        do { cur = Volatile.Read(ref stat.MaxDurationMs); }
        while (durationMs > cur && Interlocked.CompareExchange(ref stat.MaxDurationMs, durationMs, cur) != cur);
    }

    public void RecordSkill(string name) => skillUsage.AddOrUpdate(name, 1, (_, c) => c + 1);
    public void RecordAiLatency(string model, long ms)
    {
        aiLatency.Enqueue((DateTimeOffset.UtcNow, model, ms));
        while (aiLatency.Count > 500 && aiLatency.TryDequeue(out _)) { }
    }
    public void RecordSttLatency(long ms)
    {
        sttLatency.Enqueue((DateTimeOffset.UtcNow, ms));
        while (sttLatency.Count > 500 && sttLatency.TryDequeue(out _)) { }
    }
    public void RecordSelfSample(double cpu, long ram)
    {
        selfSamples.Enqueue((DateTimeOffset.UtcNow, cpu, ram));
        while (selfSamples.Count > 600 && selfSamples.TryDequeue(out _)) { }
    }

    public IReadOnlyList<ToolStat> ToolStatsSnapshot() => toolStats.Values.OrderByDescending(t => t.Calls).ToList();
    public IReadOnlyDictionary<string, long> SkillStats() => skillUsage.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
    public double? AvgAiLatencyMs(string? model = null)
    {
        var src = model == null ? aiLatency : aiLatency.Where(x => x.Name == model);
        if (!src.Any()) return null;
        return src.TakeLast(100).Average(x => (double)x.DurationMs);
    }
    public double? AvgSttLatencyMs() => sttLatency.Any() ? sttLatency.TakeLast(100).Average(x => (double)x.DurationMs) : null;
    public double TaskCompletionRate()
    {
        var ts = toolStats.Values.ToList();
        long total = ts.Sum(t => t.Calls);
        long fails = ts.Sum(t => t.Failures);
        return total == 0 ? 1.0 : 1.0 - (fails / (double)total);
    }
}
