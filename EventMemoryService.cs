namespace SentinelX;

public sealed record PerformanceEventObservation(DateTimeOffset At, string Game, IReadOnlyList<string> ElevatedMetrics);

/// <summary>Short, RAM-only event markers created only after a user requests performance diagnosis.</summary>
public sealed class EventMemoryService
{
    private const int MaximumEvents = 12;
    private readonly object gate = new();
    private readonly Queue<PerformanceEventObservation> events = new();

    public string RecordPerformanceCheck(string game, IReadOnlyList<PerformanceSample> samples)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        string gameKey = string.IsNullOrWhiteSpace(game) ? "(gra niewykryta)" : game.Trim();
        var recent = samples.Where(x => now - x.CapturedAt <= TimeSpan.FromSeconds(90)).ToArray();
        string[] elevated = new[]
        {
            IsRepeated(recent, x => float.IsFinite(x.CpuPercent), x => x.CpuPercent >= 90) ? "CPU" : null,
            IsRepeated(recent, x => double.IsFinite(x.RamPercent), x => x.RamPercent >= 92) ? "RAM" : null,
            IsRepeated(recent, x => float.IsFinite(x.GpuPercent), x => x.GpuPercent >= 98) ? "GPU" : null
        }.Where(x => x != null).Cast<string>().ToArray();

        lock (gate)
        {
            while (events.Count > 0 && now - events.Peek().At > TimeSpan.FromHours(2)) events.Dequeue();
            // Repeated commands during the same short incident are not counted as separate events.
            var previous = events.LastOrDefault();
            if (previous != null && previous.Game.Equals(gameKey, StringComparison.OrdinalIgnoreCase) && now - previous.At < TimeSpan.FromSeconds(30))
                return FormatSummary(gameKey, elevated, recent.Length, duplicate: true);

            events.Enqueue(new(now, gameKey, elevated));
            while (events.Count > MaximumEvents) events.Dequeue();
            return FormatSummary(gameKey, elevated, recent.Length, duplicate: false);
        }
    }

    private string FormatSummary(string game, string[] current, int sampleCount, bool duplicate)
    {
        string prefix = $"Event Memory: {events.Count}/{MaximumEvents} user-requested incident markers in RAM; no disk log. " +
            (duplicate ? "This request is within 30 s of the previous marker and was not counted twice. " : "");
        if (current.Length == 0)
            return prefix + $"This check ({game}) found no repeated high CPU/RAM/GPU threshold in {sampleCount} recent samples, or measurements were unavailable. No recurring correlation is supported.";

        var matching = events.Where(x => x.Game.Equals(game, StringComparison.OrdinalIgnoreCase) &&
            x.ElevatedMetrics.Intersect(current, StringComparer.OrdinalIgnoreCase).Any()).ToArray();
        if (matching.Length < 2)
            return prefix + $"This check shows repeated high {string.Join("/", current)} samples. It is a single co-occurrence, not a recurring correlation or proven cause.";
        string[] shared = current.Where(metric => matching.Count(x => x.ElevatedMetrics.Contains(metric, StringComparer.OrdinalIgnoreCase)) >= 2).ToArray();
        if (shared.Length == 0)
            return prefix + "No repeated matching resource pattern across separate checks.";
        return prefix + $"Across {matching.Length} separate user-requested checks for {game}, elevated {string.Join("/", shared)} recurred in the recent samples. This is only a correlation; no FPS/frametime/hitch sensor is available, so it does not establish cause.";
    }

    private static bool IsRepeated(IReadOnlyList<PerformanceSample> samples, Func<PerformanceSample, bool> available,
        Func<PerformanceSample, bool> elevated)
    {
        int valid = samples.Count(available);
        int measured = samples.Count(x => available(x) && elevated(x));
        return valid >= 3 && measured >= 3 && measured * 2 >= valid;
    }

    public string FormatStatus()
    {
        lock (gate)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            while (events.Count > 0 && now - events.Peek().At > TimeSpan.FromHours(2)) events.Dequeue();
            if (events.Count == 0) return "Event Memory: brak zgłoszonych incydentów; działa tylko w RAM, nic nie zapisano na dysku.";
            return $"Event Memory: {events.Count}/{MaximumEvents} markerów zgłoszonych przez użytkownika w RAM (najstarszy {events.Peek().At:HH:mm:ss}); nic nie zapisano na dysku.";
        }
    }

    public void Clear() { lock (gate) events.Clear(); }
}
