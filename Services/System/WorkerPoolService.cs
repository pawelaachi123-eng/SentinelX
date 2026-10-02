using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace SentinelX.Services.System;

/// <summary>
/// 2.0 · Ograniczony worker pool dla zadań CPU-bound.
/// - BlockingCollection z wieloma konsumentami
/// - Priorytet przez wiele kolejek (Critical → Background)
/// - Konfigurowalna liczba workerów (domyślnie max(1, CPU-1))
/// - Automatyczna degradacja w trybie gry
/// </summary>
public sealed class WorkerPoolService : IDisposable
{
    private readonly Func<bool>? isGaming;
    private readonly Func<int>? getWorkerCount;
    private readonly BlockingCollection<PrioritizedWorkItem>[] queues;
    private readonly Thread[] workers;
    private readonly CancellationTokenSource lifetime = new();
    private int activeTasks;
    private long totalScheduled, totalCompleted;
    private volatile bool disposed;
    private readonly object workerAdjustLock = new();

    public int ActiveTasks => Volatile.Read(ref activeTasks);
    public int QueuedItems
    {
        get { int n = 0; foreach (var q in queues) n += q.Count; return n; }
    }
    public long TotalScheduled => Volatile.Read(ref totalScheduled);
    public long TotalCompleted => Volatile.Read(ref totalCompleted);
    public int TargetWorkerCount { get; private set; }
    public int ActiveWorkerCount { get; private set; }

    public WorkerPoolService(Func<bool>? isGaming = null, Func<int>? getWorkerCount = null)
    {
        this.isGaming = isGaming;
        this.getWorkerCount = getWorkerCount;
        int priorities = Enum.GetValues<TaskPriority>().Length;
        queues = new BlockingCollection<PrioritizedWorkItem>[priorities];
        for (int i = 0; i < priorities; i++) queues[i] = new BlockingCollection<PrioritizedWorkItem>();

        TargetWorkerCount = ComputeTarget();
        workers = new Thread[TargetWorkerCount];
        ActiveWorkerCount = TargetWorkerCount;
        for (int i = 0; i < TargetWorkerCount; i++) workers[i] = StartWorker($"Sentinel-Worker-{i}");
    }

    private int ComputeTarget()
    {
        if (getWorkerCount != null) return Math.Max(1, getWorkerCount());
        int c = Environment.ProcessorCount;
        if (isGaming != null && isGaming()) return Math.Max(1, Math.Min(2, c / 4));
        return Math.Max(1, c - 1);
    }

    private Thread StartWorker(string name)
    {
        var t = new Thread(WorkerLoop) { Name = name, IsBackground = true, Priority = ThreadPriority.BelowNormal };
        t.Start();
        return t;
    }

    private void WorkerLoop()
    {
        while (!disposed)
        {
            PrioritizedWorkItem? item = null;
            try
            {
                // Take from highest priority first (0 = Critical)
                for (int p = 0; p < queues.Length; p++)
                {
                    if (queues[p].TryTake(out item, 50)) break;
                }
                if (item == null) continue;
                if (item.LinkToken.IsCancellationRequested) { item.SetCanceled(); continue; }

                Interlocked.Increment(ref activeTasks);
                try
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, item.LinkToken);
                    item.Run(linked.Token).GetAwaiter().GetResult();
                    item.SetCompleted();
                }
                catch (OperationCanceledException) { item.SetCanceled(); }
                catch (Exception ex) { item.SetException(ex); }
                finally
                {
                    Interlocked.Decrement(ref activeTasks);
                    Interlocked.Increment(ref totalCompleted);
                }
            }
            catch { /* loop */ }
        }
    }

    public Task<T> EnqueueAsync<T>(Func<CancellationToken, Task<T>> work, TaskPriority priority = TaskPriority.Normal, CancellationToken cancellation = default)
    {
        var item = new PrioritizedWorkItem((int)priority, async ct => await work(ct), cancellation);
        Interlocked.Increment(ref totalScheduled);
        queues[(int)priority].Add(item, cancellation);
        return item.AsTask<T>();
    }

    public Task EnqueueAsync(Func<CancellationToken, Task> work, TaskPriority priority = TaskPriority.Normal, CancellationToken cancellation = default)
        => EnqueueAsync<object?>(async ct => { await work(ct); return null; }, priority, cancellation);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        foreach (var q in queues) q.Dispose();
        lifetime.Dispose();
    }
}

internal sealed class PrioritizedWorkItem
{
    private readonly TaskCompletionSource<object?> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Func<CancellationToken, Task<object?>> runner;
    public CancellationToken LinkToken { get; }

    public PrioritizedWorkItem(int priority, Func<CancellationToken, Task<object?>> runner, CancellationToken ct)
    {
        this.runner = runner;
        LinkToken = ct;
    }
    public async Task Run(CancellationToken ct) { var result = await runner(ct); tcs.TrySetResult(result); }
    public void SetCanceled() => tcs.TrySetCanceled();
    public void SetException(Exception ex) => tcs.TrySetException(ex);
    public void SetCompleted() { /* already set via Run */ }
    public Task<T> AsTask<T>() => tcs.Task.ContinueWith(t => (T)(object?)t.Result!, TaskContinuationOptions.OnlyOnRanToCompletion);
}
