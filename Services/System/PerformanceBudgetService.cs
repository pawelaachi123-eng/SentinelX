using System;
using System.Diagnostics;
using System.Threading;

namespace SentinelX.Services.System;

/// <summary>
/// 2.0 · Monitoruje zużycie zasobów przez Sentinela i włącza tryb oszczędny
/// gdy wyjdziemy poza budżet CPU. Zmiana Throttling jest sygnalizowana zdarzeniami.
/// </summary>
public sealed class PerformanceBudgetService : IDisposable
{
    private readonly Process self = Process.GetCurrentProcess();
    private readonly Func<bool> isGaming;
    private readonly Timer timer;
    private DateTime lastWallTime;
    private TimeSpan lastCpuTime;
    private double selfCpuPercent;
    private long selfMemoryMb;
    private volatile bool throttling;
    public event Action? BudgetExceeded;
    public event Action? BudgetRestored;

    public bool IsThrottling => throttling;
    public double SelfCpuPercent => selfCpuPercent;
    public long SelfMemoryMb => selfMemoryMb;

    public PerformanceBudgetService(Func<bool> isGaming, int intervalMs = 2500)
    {
        this.isGaming = isGaming;
        self.Refresh();
        lastWallTime = DateTime.UtcNow;
        lastCpuTime = self.TotalProcessorTime;
        timer = new Timer(_ => Sample(), null, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(intervalMs));
    }

    private void Sample()
    {
        try
        {
            self.Refresh();
            DateTime now = DateTime.UtcNow;
            TimeSpan nowCpu = self.TotalProcessorTime;
            double wallMs = (now - lastWallTime).TotalMilliseconds;
            double cpuDeltaMs = (nowCpu - lastCpuTime).TotalMilliseconds;
            lastWallTime = now;
            lastCpuTime = nowCpu;
            if (wallMs <= 0) return;
            double pct = Math.Clamp((cpuDeltaMs / (wallMs * Environment.ProcessorCount)) * 100, 0, 100);
            // EMA dla gładszego odczytu
            selfCpuPercent = selfCpuPercent == 0 ? pct : selfCpuPercent * 0.6 + pct * 0.4;
            selfMemoryMb = self.WorkingSet64 / (1024 * 1024);

            double budget = isGaming() ? 25.0 : 50.0;
            bool wasThrottling = throttling;
            bool nowOver = selfCpuPercent > budget + 5; // histereza 5%
            bool nowOk = selfCpuPercent < budget - 5;
            if (nowOver && !wasThrottling) { throttling = true; BudgetExceeded?.Invoke(); }
            else if (nowOk && wasThrottling) { throttling = false; BudgetRestored?.Invoke(); }
        }
        catch { /* ignore */ }
    }

    public void Dispose()
    {
        timer.Dispose();
        self.Dispose();
    }
}
