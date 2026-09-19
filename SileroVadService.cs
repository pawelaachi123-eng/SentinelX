using SherpaOnnx;
using System.IO;

namespace SentinelX;

public sealed record VadSpeechSegment(long StartSample, float[] Samples);

public sealed class VadProcessResult
{
    public bool SpeechDetected { get; init; }
    public IReadOnlyList<VadSpeechSegment> Segments { get; init; } = Array.Empty<VadSpeechSegment>();
    public IReadOnlyList<float[]> CompletedSegments => Segments.Select(x => x.Samples).ToArray();
}

/// <summary>Feeds complete 32 ms frames to Silero and preserves native sample offsets.</summary>
public sealed class SileroVadService : IDisposable
{
    public const int SampleRate = 16000;
    private const int WindowSize = 512;
    private readonly object syncRoot = new();
    private readonly string modelPath;
    private readonly float[] pendingWindow = new float[WindowSize];
    private int pendingCount;
    private VoiceActivityDetector? detector;
    private bool wakeOnlyMode;
    private VoiceRuntimeOptions options;

    public SileroVadService(string modelPath, VoiceRuntimeOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        if (!File.Exists(modelPath)) throw new FileNotFoundException("Nie znaleziono modelu Silero VAD.", modelPath);
        this.modelPath = modelPath;
        this.options = options ?? new();
        CreateDetector();
    }

    public bool Configure(VoiceRuntimeOptions value)
    {
        lock (syncRoot)
        {
            bool changed = options.VadThreshold != value.VadThreshold ||
                options.EndOfSpeechMilliseconds != value.EndOfSpeechMilliseconds ||
                options.MinimumSpeechMilliseconds != value.MinimumSpeechMilliseconds ||
                options.MaximumSpeechSeconds != value.MaximumSpeechSeconds;
            options = value;
            if (changed) CreateDetector();
            return changed;
        }
    }

    public bool SetWakeOnlyMode(bool enabled)
    {
        lock (syncRoot)
        {
            if (wakeOnlyMode == enabled) return false;
            // Wake filtering happens on the transcript. Keep the configured VAD threshold exact.
            // Switching UI state must not discard a half-spoken sentence.
            wakeOnlyMode = enabled;
            return true;
        }
    }

    public VadProcessResult Process(float[] samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        lock (syncRoot)
        {
            if (detector == null) return new();
            var completed = new List<VadSpeechSegment>();
            for (int offset = 0; offset < samples.Length;)
            {
                int count = Math.Min(WindowSize - pendingCount, samples.Length - offset);
                Array.Copy(samples, offset, pendingWindow, pendingCount, count);
                offset += count;
                pendingCount += count;
                if (pendingCount < WindowSize) continue;
                detector.AcceptWaveform(pendingWindow);
                pendingCount = 0;
                DrainCompletedSegments(completed);
            }
            return new() { SpeechDetected = detector.IsSpeechDetected(), Segments = completed };
        }
    }

    private void DrainCompletedSegments(List<VadSpeechSegment> destination)
    {
        if (detector == null) return;
        while (!detector.IsEmpty())
        {
            SpeechSegment segment = detector.Front();
            if (segment.Samples is { Length: > 0 })
                destination.Add(new(segment.Start, (float[])segment.Samples.Clone()));
            detector.Pop();
        }
    }

    public IReadOnlyList<float[]> Flush()
    {
        lock (syncRoot)
        {
            if (detector == null) return Array.Empty<float[]>();
            if (pendingCount > 0)
            {
                Array.Clear(pendingWindow, pendingCount, WindowSize - pendingCount);
                detector.AcceptWaveform(pendingWindow);
                pendingCount = 0;
            }
            detector.Flush();
            var completed = new List<VadSpeechSegment>();
            DrainCompletedSegments(completed);
            return completed.Select(x => x.Samples).ToArray();
        }
    }

    public void Reset()
    {
        lock (syncRoot) { pendingCount = 0; detector?.Reset(); }
    }

    private void CreateDetector()
    {
        var config = new VadModelConfig();
        config.SileroVad.Model = modelPath;
        config.SileroVad.Threshold = options.VadThreshold;
        config.SileroVad.MinSpeechDuration = options.MinimumSpeechMilliseconds / 1000f;
        config.SileroVad.MinSilenceDuration = options.EndOfSpeechMilliseconds / 1000f;
        config.SileroVad.MaxSpeechDuration = options.MaximumSpeechSeconds;
        config.SileroVad.WindowSize = WindowSize;
        config.SampleRate = SampleRate;
        config.Debug = 0;
        var replacement = new VoiceActivityDetector(config, options.MaximumSpeechSeconds + 5);
        detector?.Dispose();
        detector = replacement;
        pendingCount = 0;
    }

    public void Dispose()
    {
        lock (syncRoot) { pendingCount = 0; detector?.Dispose(); detector = null; }
    }
}
