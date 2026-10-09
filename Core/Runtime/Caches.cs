using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SentinelX.Core.Runtime;

/// <summary>
/// SEKCJA 1 ┬Ě pozycja 14 ÔÇö pami─Ö─ç podr─Öczna w pami─Öci z ewolucj─ů LRU/LFU: nowy wpis wypiera ten
/// o najni┼╝szej liczbie trafie┼ä, a przy remisie ÔÇö najdawniej u┼╝ywany. Dodatkowo TTL na wpis,
/// wi─Öc ÔÇ×cacheÔÇŁ nie udaje, ┼╝e dane s─ů wieczne.
/// </summary>
public sealed class LruLfuCache<TKey, TValue> where TKey : notnull
{
    private readonly object gate = new();
    private readonly Dictionary<TKey, Entry> entries;
    private readonly Func<DateTimeOffset> clock;
    private long tick;

    public LruLfuCache(int capacity = 128, TimeSpan? defaultTtl = null, Func<DateTimeOffset>? clock = null)
    {
        Capacity = Math.Max(1, capacity);
        DefaultTtl = defaultTtl;
        this.clock = clock ?? (() => DateTimeOffset.Now);
        entries = new Dictionary<TKey, Entry>();
    }

    public int Capacity { get; }
    public TimeSpan? DefaultTtl { get; }
    public long Hits { get; private set; }
    public long Misses { get; private set; }
    public long Evictions { get; private set; }
    public long Expired { get; private set; }

    private sealed class Entry
    {
        public TValue Value = default!;
        public int Frequency;
        public long LastUsed;
        public DateTimeOffset? ExpiresAt;
    }

    public int Count
    {
        get { lock (gate) return entries.Count; }
    }

    public bool TryGet(TKey key, out TValue? value)
    {
        lock (gate)
        {
            if (entries.TryGetValue(key, out var entry) && !IsExpired(entry))
            {
                entry.Frequency++;
                entry.LastUsed = ++tick;
                Hits++;
                value = entry.Value;
                return true;
            }
            if (entry != null)
            {
                entries.Remove(key);
                Expired++;
            }
            Misses++;
            value = default;
            return false;
        }
    }

    public void Set(TKey key, TValue value, TimeSpan? ttl = null)
    {
        lock (gate)
        {
            if (entries.TryGetValue(key, out var existing))
            {
                existing.Value = value;
                existing.Frequency++;
                existing.LastUsed = ++tick;
                existing.ExpiresAt = Resolve(ttl);
                return;
            }
            entries[key] = new Entry { Value = value, Frequency = 1, LastUsed = ++tick, ExpiresAt = Resolve(ttl) };
            Trim();
        }
    }

    /// <summary>Klasyczne ÔÇ×get albo policzÔÇŁ. Fabryka jest wo┼éana poza blokad─ů wpis├│w, ale raz na raz ÔÇö
    /// to celowo prosty cache, nie koordynator pracy (od tego jest kolejka zada┼ä).</summary>
    public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory, TimeSpan? ttl = null)
    {
        if (TryGet(key, out var value) && value is not null) return value;
        var created = factory(key);
        Set(key, created, ttl);
        return created;
    }

    public bool Remove(TKey key)
    {
        lock (gate) return entries.Remove(key);
    }

    public void Clear()
    {
        lock (gate)
        {
            entries.Clear();
            tick = 0;
        }
    }

    public IReadOnlyList<(TKey Key, int Frequency, long LastUsed)> Snapshot()
    {
        lock (gate)
            return entries.Select(x => (x.Key, x.Value.Frequency, x.Value.LastUsed))
                .OrderByDescending(x => x.Item2).ThenByDescending(x => x.Item3).ToArray();
    }

    private DateTimeOffset? Resolve(TimeSpan? ttl)
    {
        var effective = ttl ?? DefaultTtl;
        return effective is { } span && span > TimeSpan.Zero ? clock() + span : null;
    }

    private bool IsExpired(Entry entry) => entry.ExpiresAt is { } at && at <= clock();

    private void Trim()
    {
        while (entries.Count > Capacity)
        {
            // Ewolucja LRU/LFU: najpierw najni┼╝sza cz─Östotliwo┼Ť─ç, przy remisie najstarszy dost─Öp.
            var victim = entries.OrderBy(x => x.Value.Frequency).ThenBy(x => x.Value.LastUsed).First();
            entries.Remove(victim.Key);
            Evictions++;
        }
    }

    public string Describe()
    {
        lock (gate)
        {
            double total = Hits + Misses;
            string rate = total == 0 ? "brak danych" : (Hits / total * 100).ToString("0.#", CultureInfo.GetCultureInfo("pl-PL")) + "%";
            return "Cache LRU/LFU: " + entries.Count + "/" + Capacity + " wpis├│w ┬Ě trafienia " + Hits + " ┬Ě pud┼éa " + Misses +
                " ┬Ě skuteczno┼Ť─ç " + rate + " ┬Ě wyparcia " + Evictions + " ┬Ě wygas┼ée " + Expired +
                (DefaultTtl is { } ttl ? " ┬Ě domy┼Ťlny TTL " + RetryPolicy.Describe(ttl) : " ┬Ě bez domy┼Ťlnego TTL");
        }
    }
}
