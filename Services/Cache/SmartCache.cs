using System.Linq;
 using System.Threading;
using System.Collections.Concurrent;

namespace SentinelX.Services.Cache;

/// <summary>
/// Prosty, asynchroniczny LRU-TTL cache z limitem rozmiaru.
/// Działa w pamięci procesu; nie wymaga zewnętrznych zależności.
/// </summary>
public sealed class SmartCache : IDisposable
{
    private readonly ConcurrentDictionary<string, CacheEntry> entries = new();
    private readonly long maxBytes;
    private long currentBytes;
    private readonly object trimGate = new();
    private readonly Timer trimTimer;

    public SmartCache(long maxBytes = 64 * 1024 * 1024)
    {
        this.maxBytes = maxBytes;
        trimTimer = new Timer(_ => Trim(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60));
    }

    public int Count => entries.Count;

    public void Set<T>(string key, T value, TimeSpan ttl, long? sizeBytes = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        var expiry = DateTimeOffset.UtcNow + ttl;
        long size = sizeBytes ?? EstimateSize(value);
        var entry = new CacheEntry(value, expiry, size, DateTimeOffset.UtcNow);
        if (entries.TryRemove(key, out var old)) Interlocked.Add(ref currentBytes, -old.SizeBytes);
        entries[key] = entry;
        Interlocked.Add(ref currentBytes, size);
        Trim();
    }

    public bool TryGet<T>(string key, out T? value) where T : class
    {
        value = null;
        if (!entries.TryGetValue(key, out var entry)) return false;
        if (entry.ExpiresAt < DateTimeOffset.UtcNow)
        {
            Invalidate(key);
            return false;
        }
        entry.Touch();
        value = (T)entry.Value;
        return true;
    }

    public void Invalidate(string key)
    {
        if (entries.TryRemove(key, out var old)) Interlocked.Add(ref currentBytes, -old.SizeBytes);
    }

    public void InvalidatePrefix(string prefix)
    {
        foreach (var kv in entries)
        {
            if (kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                Invalidate(kv.Key);
        }
    }

    public void Clear()
    {
        entries.Clear();
        Interlocked.Exchange(ref currentBytes, 0);
    }

    private void Trim()
    {
        lock (trimGate)
        {
            // 1. Usuń wygasłe
            var now = DateTimeOffset.UtcNow;
            foreach (var kv in entries)
            {
                if (kv.Value.ExpiresAt < now) Invalidate(kv.Key);
            }
            // 2. Jeśli nadal ponad limit, usuwaj najstarsze (LRU po LastAccessedAt)
            while (Volatile.Read(ref currentBytes) > maxBytes && !entries.IsEmpty)
            {
                var oldest = entries.OrderBy(kv => kv.Value.LastAccessedAt).FirstOrDefault();
                if (oldest.Key == null) break;
                Invalidate(oldest.Key);
            }
        }
    }

    private static long EstimateSize(object value) => value switch
    {
        string s => s.Length * 2 + 24,
        byte[] b => b.LongLength + 24,
        System.Collections.ICollection c => c.Count * 64 + 48,
        _ => 256
    };

    public void Dispose() => trimTimer.Dispose();

    private sealed class CacheEntry
    {
        public object Value { get; }
        public DateTimeOffset ExpiresAt { get; }
        public long SizeBytes { get; }
        public DateTimeOffset LastAccessedAt;
        public CacheEntry(object v, DateTimeOffset exp, long size, DateTimeOffset now)
        {
            Value = v; ExpiresAt = exp; SizeBytes = size; LastAccessedAt = now;
        }
        public void Touch() => LastAccessedAt = DateTimeOffset.UtcNow;
    }
}
