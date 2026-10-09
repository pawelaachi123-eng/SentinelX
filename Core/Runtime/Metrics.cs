using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace SentinelX.Core.Runtime;

/// <summary>
/// SEKCJA 1 ┬Ě pozycje 43ÔÇô44 ÔÇö wbudowany profilowany licznik: liczniki, wska┼║niki i histogramy
/// (p50/p95/maks) bez zewn─Ötrznych bibliotek. Pr├│bki s─ů ograniczone (domy┼Ťlnie 512 na seri─Ö),
/// wi─Öc mierzenie nie zjada pami─Öci ÔÇö to celowe, bo miernik, kt├│ry sam przecieka, jest bezu┼╝yteczny.
/// </summary>
public sealed class Metrics
{
    private readonly object gate = new();
    private readonly Dictionary<string, long> counters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> gauges = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<double>> samples = new(StringComparer.OrdinalIgnoreCase);
    private readonly int maxSamples;

    public Metrics(int maxSamples = 512)
    {
        this.maxSamples = Math.Max(8, maxSamples);
    }

    public void Increment(string name, long by = 1)
    {
        lock (gate)
        {
            counters.TryGetValue(name, out long current);
            counters[name] = current + by;
        }
    }

    public void Set(string name, double value)
    {
        lock (gate) gauges[name] = value;
    }

    public void Observe(string name, double value)
    {
        lock (gate)
        {
            if (!samples.TryGetValue(name, out var list))
            {
                list = new List<double>();
                samples[name] = list;
            }
            list.Add(value);
            if (list.Count > maxSamples) list.RemoveAt(0);
        }
    }

    public long Counter(string name)
    {
        lock (gate) return counters.TryGetValue(name, out long value) ? value : 0;
    }

    public double? Gauge(string name)
    {
        lock (gate) return gauges.TryGetValue(name, out double value) ? value : null;
    }

    /// <summary>Mierzy czas bloku kodu i zapisuje go w histogramie. Zwolnienie obiektu ko┼äczy pomiar.</summary>
    public IDisposable Time(string name)
    {
        var watch = Stopwatch.StartNew();
        return new Timer(watch, elapsed => Observe(name, elapsed.TotalMilliseconds));
    }

    private sealed class Timer : IDisposable
    {
        private readonly Stopwatch watch;
        private readonly Action<TimeSpan> report;

        public Timer(Stopwatch watch, Action<TimeSpan> report)
        {
            this.watch = watch;
            this.report = report;
        }

        public void Dispose()
        {
            watch.Stop();
            report(watch.Elapsed);
        }
    }

    public sealed record Series(string Name, int Count, double Min, double Max, double Average, double P50, double P95);

    public IReadOnlyList<Series> Snapshot()
    {
        lock (gate)
            return samples.Select(x =>
            {
                var sorted = x.Value.OrderBy(v => v).ToArray();
                return new Series(x.Key, sorted.Length, sorted[0], sorted[^1], sorted.Average(), Percentile(sorted, 0.50), Percentile(sorted, 0.95));
            }).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static double Percentile(double[] sorted, double fraction)
    {
        if (sorted.Length == 0) return 0;
        if (sorted.Length == 1) return sorted[0];
        double position = fraction * (sorted.Length - 1);
        int low = (int)Math.Floor(position);
        int high = (int)Math.Ceiling(position);
        if (low == high) return sorted[low];
        double weight = position - low;
        return sorted[low] * (1 - weight) + sorted[high] * weight;
    }

    public void Reset()
    {
        lock (gate)
        {
            counters.Clear();
            gauges.Clear();
            samples.Clear();
        }
    }

    public string Describe(int max = 12)
    {
        var culture = CultureInfo.GetCultureInfo("pl-PL");
        List<string> lines = new();
        lock (gate)
        {
            if (counters.Count == 0 && gauges.Count == 0 && samples.Count == 0)
                return "Brak pomiar├│w ÔÇö nic jeszcze nie by┼éo mierzone (miernik nie wymy┼Ťla liczb).";
            foreach (var counter in counters.OrderByDescending(x => x.Value).Take(max))
                lines.Add("┬Ě licznik " + counter.Key + " = " + counter.Value.ToString(culture));
            foreach (var gauge in gauges.OrderBy(x => x.Key).Take(max))
                lines.Add("┬Ě wska┼║nik " + gauge.Key + " = " + gauge.Value.ToString("0.###", culture));
        }
        foreach (var series in Snapshot().Take(max))
            lines.Add("┬Ě " + series.Name + ": " + series.Count + " pr├│bek ┬Ě min " + series.Min.ToString("0.#", culture) +
                " ┬Ě p50 " + series.P50.ToString("0.#", culture) + " ┬Ě p95 " + series.P95.ToString("0.#", culture) +
                " ┬Ě maks " + series.Max.ToString("0.#", culture) + " ┬Ě ┼Ťrednia " + series.Average.ToString("0.#", culture));
        return "Pomiary:" + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }
}
