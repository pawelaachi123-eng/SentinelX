namespace SentinelX;

public sealed record VoiceSegmentMetrics(int SampleCount, double DurationMilliseconds,
    float RawRms, float EnhancedRms, float RawPeak, float EnhancedPeak,
    float NoiseRms, float SnrDb, float AppliedGain, bool Clipping, bool CompleteRawCoverage);

/// <summary>Bounded in-memory raw samples aligned to the VAD sample clock. Never persists audio.</summary>
public sealed class VoiceSignalHistory
{
    private sealed record Frame(long Start, float[] Raw, float Noise, float Gain);
    private readonly Queue<Frame> frames = new();
    private const int RetainedSamples = 16000 * 55;
    public long SamplePosition { get; private set; }

    public void Reset() { frames.Clear(); SamplePosition = 0; }
    public void Add(EnhancedAudioFrame frame)
    {
        frames.Enqueue(new(SamplePosition, frame.RawSamples, frame.NoiseFloor, frame.Gain));
        SamplePosition += frame.Samples.Length;
        while (frames.TryPeek(out var oldest) && oldest.Start + oldest.Raw.Length < SamplePosition - RetainedSamples)
            frames.Dequeue();
    }

    public VoiceSegmentMetrics Measure(long start, float[] enhanced)
    {
        long end = start + enhanced.Length;
        double rawEnergy = 0, noiseEnergy = 0, gainSum = 0;
        float rawPeak = 0, enhancedPeak = 0;
        int rawCount = 0;
        foreach (var frame in frames)
        {
            int first = (int)Math.Max(0, start - frame.Start);
            int last = (int)Math.Min(frame.Raw.Length, end - frame.Start);
            if (last <= first) continue;
            for (int i = first; i < last; i++)
            {
                float value = frame.Raw[i];
                rawEnergy += (double)value * value;
                rawPeak = Math.Max(rawPeak, Math.Abs(value));
            }
            int count = last - first;
            rawCount += count;
            noiseEnergy += (double)frame.Noise * frame.Noise * count;
            gainSum += frame.Gain * count;
        }
        foreach (float value in enhanced) enhancedPeak = Math.Max(enhancedPeak, Math.Abs(value));
        float rawRms = rawCount > 0 ? (float)Math.Sqrt(rawEnergy / rawCount) : 0;
        float noise = rawCount > 0 ? (float)Math.Sqrt(noiseEnergy / rawCount) : 0;
        return new(enhanced.Length, enhanced.Length / 16.0, rawRms, AudioSignalMath.Rms(enhanced),
            rawPeak, enhancedPeak, noise, AudioSignalMath.EstimateSnrDb(rawRms, noise),
            rawCount > 0 ? (float)(gainSum / rawCount) : 1, rawPeak >= 0.995f, rawCount == enhanced.Length);
    }
}
