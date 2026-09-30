namespace SentinelX;

/// <summary>Stateful, rate-limited threshold policy for restrained Watch notifications.</summary>
public sealed class WatcherAlertPolicy
{
    private readonly object gate = new();
    private int consecutiveHighSamples;
    private DateTimeOffset highSince;
    private DateTimeOffset lastNotice;

    public string? Observe(PerformanceSample sample, WatchSettings settings, bool explicitlyRequested, bool focusModeActive)
    {
        lock (gate)
        {
            if ((!settings.Enabled && !explicitlyRequested) || focusModeActive ||
                (explicitlyRequested && string.IsNullOrWhiteSpace(sample.RunningGame)))
            {
                ResetLocked();
                return null;
            }

            int cpuLimit = Math.Clamp(settings.CpuAlertPercent, 1, 100);
            int ramLimit = Math.Clamp(settings.RamAlertPercent, 1, 100);
            bool high = (float.IsFinite(sample.CpuPercent) && sample.CpuPercent >= cpuLimit)
                || (double.IsFinite(sample.RamPercent) && sample.RamPercent >= ramLimit)
                || (float.IsFinite(sample.GpuPercent) && sample.GpuPercent >= 98);
            if (!high)
            {
                consecutiveHighSamples = 0;
                highSince = default;
                return null;
            }

            if (consecutiveHighSamples == 0) highSince = sample.CapturedAt;
            consecutiveHighSamples++;
            TimeSpan minimum = TimeSpan.FromSeconds(Math.Clamp(settings.MinSecondsBeforeAlert, 1, 600));
            TimeSpan cooldown = TimeSpan.FromMinutes(Math.Clamp(settings.CooldownMinutes, 1, 240));
            if (consecutiveHighSamples < 3 || highSince == default || sample.CapturedAt - highSince < minimum ||
                (lastNotice != default && sample.CapturedAt - lastNotice < cooldown)) return null;

            lastNotice = sample.CapturedAt;
            consecutiveHighSamples = 0;
            highSince = default;
            string context = string.IsNullOrWhiteSpace(sample.RunningGame) ? "podczas pracy komputera" : $"podczas wykrytej gry „{sample.RunningGame}”";
            return $"Wysokie użycie zasobów utrzymało się przez co najmniej {Math.Clamp(settings.MinSecondsBeforeAlert, 1, 600)} s {context} (CPU {Format(sample.CpuPercent)}, RAM {Format(sample.RamPercent)}, GPU {Format(sample.GpuPercent)}). To korelacja, nie potwierdzona przyczyna; niczego nie zmieniłem.";
        }
    }

    public void Reset()
    {
        lock (gate) ResetLocked();
    }

    private void ResetLocked()
    {
        consecutiveHighSamples = 0;
        highSince = default;
    }

    private static string Format(double value) => double.IsFinite(value)
        ? value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%" : "niedostępne";
}
