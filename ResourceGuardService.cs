using System.Diagnostics;

namespace SentinelX;

public sealed record ResourceSnapshot(double CpuPercent, double WorkingSetMb, int Threads, TimeSpan Uptime);
public sealed class ResourceGuardService : IDisposable
{
    private readonly Process process = Process.GetCurrentProcess();
    private TimeSpan previousCpu;
    private long previousTime;
    public ResourceSnapshot Sample()
    {
        process.Refresh();
        long now = Stopwatch.GetTimestamp();
        TimeSpan cpu = process.TotalProcessorTime;
        double elapsed = previousTime == 0 ? 0 : (now - previousTime) / (double)Stopwatch.Frequency;
        double percent = elapsed > 0 ? Math.Clamp((cpu - previousCpu).TotalSeconds / elapsed / Environment.ProcessorCount * 100, 0, 100) : double.NaN;
        previousCpu = cpu; previousTime = now;
        return new(percent, process.WorkingSet64 / 1048576d, process.Threads.Count, DateTime.Now - process.StartTime);
    }
    public void Dispose() => process.Dispose();
}
