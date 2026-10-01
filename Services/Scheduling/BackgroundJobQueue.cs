using System.Linq;
 using System.Threading;
using System.Collections.Concurrent;

namespace SentinelX.Services.Scheduling;

/// <summary>
/// Lekka, asynchroniczna kolejka z priorytetami i ograniczoną pojemnością.
/// Wykorzystuje kanały TPL, ale by trzymać zależności na minimalnym poziomie,
/// używa ConcurrentQueue + SemaphoreSlim. Bez pętli blokujących wątek.
/// </summary>
public sealed class BackgroundJobQueue : IDisposable
{
    private readonly int maxCapacity;
    private readonly PriorityQueue<Func<CancellationToken, Task>, int> heap = new();
    private readonly SemaphoreSlim available = new(0);
    private readonly object gate = new();
    private readonly CancellationTokenSource disposal = new();
    private int queued;

    public int QueuedCount { get { lock (gate) return queued; } }

    public BackgroundJobQueue(int maxCapacity = 64)
    {
        this.maxCapacity = maxCapacity;
    }

    /// <summary>Dodaj zadanie. Zwraca false jeśli kolejka jest pełna i zadanie nie jest krytyczne.</summary>
    public bool Enqueue(Func<CancellationToken, Task> job, JobPriority priority)
    {
        ArgumentNullException.ThrowIfNull(job);
        lock (gate)
        {
            if (queued >= maxCapacity && priority < JobPriority.Critical)
                return false;
            heap.Enqueue(job, -(int)priority); // wyższy priorytet = niższa wartość w PriorityQueue
            queued++;
        }
        available.Release();
        return true;
    }

    /// <summary>Czeka na kolejne zadanie i je zwraca. Anuluje się przy Dispose.</summary>
    public async Task<Func<CancellationToken, Task>?> DequeueAsync(CancellationToken token)
    {
        while (!disposal.IsCancellationRequested)
        {
            await available.WaitAsync(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
            lock (gate)
            {
                if (heap.TryDequeue(out var job, out _))
                {
                    queued = Math.Max(0, queued - 1);
                    return job;
                }
            }
        }
        return null;
    }

    public void Dispose()
    {
        try { disposal.Cancel(); } catch { }
        available.Dispose();
        disposal.Dispose();
    }
}
