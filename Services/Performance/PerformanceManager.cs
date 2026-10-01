using System.Linq;
 using System.Threading;
using System.Collections.Concurrent;
using System.Diagnostics;
using SentinelX.Models.Performance;

namespace SentinelX.Services.Performance;

/// <summary>
/// Monitoruje narzut SAMEGO Sentinela (nie całego systemu) i ogranicza zadania tła,
/// jeśli budżet jest przekraczany. Bazuje na Process.GetCurrentProcess().
/// </summary>
public sealed class PerformanceManager : IDisposable
{
    private readonly Process self;
    private readonly Timer timer;
    private readonly ConcurrentQueue<ComponentSample> samples = new();
    private ResourceBudget budget = new();
    private int throttled;

    public event Action<bool>? ThrottleChanged; // true = throttling ON
    public bool IsThrottled => Volatile.Read(ref throttled) == 1;

    public PerformanceManager()
    {
        self = Process.GetCurrentProcess();
        timer = new Timer(_ => Sample(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));
    }

    public void SetBudget(ResourceBudget b) => budget = b ?? new ResourceBudget();

    public ComponentSample LatestSnapshot { get; private set; }

    public IReadOnlyList<ComponentSample> Recent(int count = 60) => samples.TakeLast(count).ToList();

    private void Sample()
    {
        try
        {
            self.Refresh();
            var cpu = ReadSelfCpuPercent();
            var ram = self.WorkingSet64;
            var threads = self.Threads.Count;
            var sample = new ComponentSample(SentinelComponent.Total, cpu, ram, null, null, threads, DateTimeOffset.UtcNow);
            LatestSnapshot = sample;
            samples.Enqueue(sample);
            while (samples.Count > 300 && samples.TryDequeue(out _)) { }

            bool over = cpu > budget.MaxCpuPercent || ram > budget.MaxRamBytes;
            int was = Volatile.Read(ref throttled);
            int now = over ? 1 : 0;
            if (was != now)
            {
                Interlocked.Exchange(ref throttled, now);
                ThrottleChanged?.Invoke(over);
            }
        }
        catch { /* best effort; monitor nie może crashować aplikacji */ }
    }

    private DateTime lastCpuAt = DateTime.UtcNow;
    private TimeSpan lastCpuTime = TimeSpan.Zero;
    private double ReadSelfCpuPercent()
    {
        try
        {
            var now = DateTime.UtcNow;
            var cur = self.TotalProcessorTime;
            var dt = (now - lastCpuAt).TotalSeconds;
            var used = (cur - lastCpuTime).TotalSeconds;
            lastCpuAt = now; lastCpuTime = cur;
            if (dt <= 0) return 0;
            double cores = Math.Max(1, Environment.ProcessorCount);
            return Math.Max(0, Math.Min(100, used / dt / cores * 100));
        }
        catch { return 0; }
    }

    public void Dispose() { try { timer.Dispose(); self.Dispose(); } catch { } }
}
