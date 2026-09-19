using System;
using System.Collections.Generic;

namespace SentinelX;

public sealed class EnhancedAudioFrame
{
    public float[] RawSamples { get; init; } = Array.Empty<float>();
    public float[] Samples { get; init; } = Array.Empty<float>();
    public float RawRms { get; init; }
    public float EnhancedRms { get; init; }
    public float Gain { get; init; }
    public float NoiseFloor { get; init; }
    public float GateLevel { get; init; }
    public float Peak { get; init; }
    public float RawPeak { get; init; }
    public bool IsCalibrating { get; init; }
    public bool IsClipping { get; init; }
}

/// <summary>16 kHz mono front end. Metering always uses the unmodified microphone signal.</summary>
public sealed class AudioEnhancementService
{
    private const int SampleRate = 16000;
    private const int CalibrationSamples = SampleRate * 2;
    private readonly List<float> calibrationLevels = new();
    private int calibrationSamples;
    private float currentGain = 1;
    private float noiseFloor = 0.001f;
    private float previousInput, previousHighPass;
    private VoiceRuntimeOptions options = new();
    private float appliedGain = 1;
    public float CurrentGain => appliedGain;
    public float NoiseFloor => noiseFloor;
    public bool IsCalibrating => calibrationSamples < CalibrationSamples;
    public float EffectiveGateLevel => Math.Max(0.00035f, noiseFloor * options.NoiseGateMultiplier);

    public void Configure(VoiceRuntimeOptions value) => options = value;

    public void Reset()
    {
        currentGain = appliedGain = 1;
        noiseFloor = 0.001f;
        previousInput = previousHighPass = 0;
        BeginCalibration();
    }

    /// <summary>Call while listening; keep quiet for two seconds. No audio is saved.</summary>
    public void BeginCalibration()
    {
        calibrationSamples = 0;
        calibrationLevels.Clear();
        currentGain = appliedGain = 1;
    }

    public EnhancedAudioFrame ProcessPcm16(byte[] buffer, int bytesRecorded, bool learnNoise = true)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (bytesRecorded < 0 || bytesRecorded > buffer.Length || (bytesRecorded & 1) != 0)
            throw new ArgumentOutOfRangeException(nameof(bytesRecorded), "PCM16 wymaga pełnych próbek po 2 bajty.");
        var samples = new float[bytesRecorded / 2];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = (short)(buffer[i * 2] | (buffer[i * 2 + 1] << 8)) / 32768f;
        return ProcessSamples(samples, learnNoise);
    }

    public EnhancedAudioFrame ProcessSamples(float[] samples, bool learnNoise = true)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Length == 0) return new EnhancedAudioFrame { Gain = currentGain, IsCalibrating = IsCalibrating };
        double sum = 0;
        float peak = 0;
        foreach (float sample in samples)
        {
            if (!float.IsFinite(sample)) throw new ArgumentException("Audio zawiera nieprawidłowe próbki.", nameof(samples));
            sum += (double)sample * sample;
            peak = Math.Max(peak, Math.Abs(sample));
        }
        float rms = (float)Math.Sqrt(sum / samples.Length);
        bool calibrating = IsCalibrating;
        if (calibrating && learnNoise)
        {
            calibrationLevels.Add(rms);
            calibrationSamples += samples.Length;
            if (!IsCalibrating)
            {
                calibrationLevels.Sort();
                // A lower quantile tolerates a brief cough during quiet calibration.
                noiseFloor = Math.Clamp(calibrationLevels[(calibrationLevels.Count - 1) / 4], 0.00005f, 0.1f);
                calibrationLevels.Clear();
            }
        }
        else if (!calibrating && learnNoise && rms < noiseFloor * 1.6f)
        {
            float rate = 1 - MathF.Exp(-samples.Length / (SampleRate * (rms < noiseFloor ? 1f : 8f)));
            noiseFloor = Math.Clamp(noiseFloor + (rms - noiseFloor) * rate, 0.00005f, 0.1f);
        }

        float gate = EffectiveGateLevel;
        bool aboveNoise = !calibrating && rms > gate;
        // Never normalize background noise to speech volume. Quiet speech may get a little more help.
        float desiredGain = aboveNoise ? Math.Clamp(options.GainTargetRms / Math.Max(rms, 0.0005f), 0.5f, options.MaximumGain) : 1f;
        float smoothing = 1 - MathF.Exp(-samples.Length / (SampleRate * (desiredGain < currentGain ? 0.08f : 0.25f)));
        currentGain += (desiredGain - currentGain) * smoothing;
        appliedGain = aboveNoise ? Math.Clamp(currentGain, 0.5f, options.MaximumGain) : Math.Min(1, currentGain);
        var output = new float[samples.Length];
        double enhancedSum = 0;
        float enhancedPeak = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float highPass = samples[i] - previousInput + 0.98f * previousHighPass;
            previousInput = samples[i];
            previousHighPass = highPass;
            float value = Math.Clamp(highPass * appliedGain, -0.98f, 0.98f);
            output[i] = value;
            enhancedSum += (double)value * value;
            enhancedPeak = Math.Max(enhancedPeak, Math.Abs(value));
        }
        return new EnhancedAudioFrame
        {
            RawSamples = samples, Samples = output, RawRms = rms, EnhancedRms = (float)Math.Sqrt(enhancedSum / samples.Length),
            Gain = appliedGain, NoiseFloor = noiseFloor, GateLevel = gate, Peak = enhancedPeak,
            RawPeak = peak, IsCalibrating = calibrating, IsClipping = peak >= 0.995f
        };
    }

    public static float RmsToDbfs(float rms) => rms > 0 && float.IsFinite(rms) ? Math.Max(-96, 20 * MathF.Log10(rms)) : -96;
    public static float RmsToMeter(float rms) => Math.Clamp((RmsToDbfs(rms) + 60) / 60, 0, 1);
}

