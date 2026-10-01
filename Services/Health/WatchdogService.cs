using System.Collections.Concurrent;
 using System.Threading;

namespace SentinelX.Services.Health;

/// <summary>
/// Lekki watchdog dla usług. Każda usługa okresowo odbija "ping" (ReportAlive);
/// jeśli nie odbije w zadanym oknie, wywoływana jest akcja restartu. Awaria jednego
/// workera nie zabija aplikacji — restart jest kontrolowany.
/// </summary>
public sealed class WatchdogService : IDisposable
{
    private readonly ConcurrentDictionary<string, WorkerState> workers = new();
    private readonly Timer timer;

    public WatchdogService()
    {
        timer = new Timer(_ => Scan(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public void Register(string name, TimeSpan timeout, Func<CancellationToken, Task> restartAction)
    {
        workers[name] = new WorkerState(timeout, restartAction) { LastSeen = DateTimeOffset.UtcNow };
    }

    public void ReportAlive(string name)
    {
        if (workers.TryGetValue(name, out var w))
        {
            w.LastSeen = DateTimeOffset.UtcNow;
            Interlocked.Exchange(ref w.consecutiveFailures, 0);
        }
    }

    public IReadOnlyDictionary<string, WorkerHealth> Snapshot()
    {
        var now = DateTimeOffset.UtcNow;
        return workers.ToDictionary(
            kv => kv.Key,
            kv => new WorkerHealth(
                kv.Value.LastSeen,
                now - kv.Value.LastSeen > kv.Value.Timeout,
                kv.Value.FaultCount,
                kv.Value.LastError),
            StringComparer.OrdinalIgnoreCase);
    }

    private void Scan()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kv in workers)
        {
            if (now - kv.Value.LastSeen <= kv.Value.Timeout) continue;
            kv.Value.MarkFault("watchdog timeout");
            _ = Task.Run(async () =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await kv.Value.Restart(cts.Token).ConfigureAwait(false);
                    kv.Value.LastSeen = DateTimeOffset.UtcNow;
                }
                catch (Exception ex) { kv.Value.MarkFault(ex.Message); }
            });
        }
    }

    public void Dispose() => timer.Dispose();
}

public sealed record WorkerHealth(DateTimeOffset LastSeen, bool IsLate, int Faults, string? LastError);

internal sealed class WorkerState
{
    public TimeSpan Timeout { get; }
    public Func<CancellationToken, Task> RestartAction { get; }
    public DateTimeOffset LastSeen;
    public int FaultCount;
    public int consecutiveFailures;
    public string? LastError;
    public WorkerState(TimeSpan t, Func<CancellationToken, Task> restart) { Timeout = t; RestartAction = restart; }
    public void MarkFault(string err) { Interlocked.Increment(ref FaultCount); LastError = err; }
    public Task Restart(CancellationToken ct) => RestartAction(ct);
}
