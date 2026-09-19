using NAudio.Wave;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace SentinelX;

public static class AudioSignalMath
{
    // Estimated speech-to-noise ratio from calibrated noise power, not a model confidence score.
    public static float EstimateSnrDb(float totalRms, float noiseRms)
    {
        if (!float.IsFinite(totalRms) || !float.IsFinite(noiseRms) || totalRms <= 0 || noiseRms <= 0) return 0;
        double noisePower = (double)noiseRms * noiseRms;
        return (float)Math.Clamp(10 * Math.Log10(Math.Max(noisePower * 0.001, (double)totalRms * totalRms - noisePower) / noisePower), -30, 80);
    }
    public static float Rms(float[] samples)
    {
        if (samples.Length == 0) return 0;
        double sum = 0;
        foreach (float sample in samples) if (float.IsFinite(sample)) sum += (double)sample * sample;
        return (float)Math.Sqrt(sum / samples.Length);
    }

    public static float PlaybackRms(byte[] buffer, int count, WaveFormat format)
    {
        if (count <= 0 || count > buffer.Length) return 0;
        WaveFormatEncoding encoding = format.Encoding;
        if (format is WaveFormatExtensible ext)
        {
            if (ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71")) encoding = WaveFormatEncoding.IeeeFloat;
            else if (ext.SubFormat == new Guid("00000001-0000-0010-8000-00aa00389b71")) encoding = WaveFormatEncoding.Pcm;
            else return 0;
        }
        int bytes = format.BitsPerSample / 8;
        if (bytes is < 1 or > 4) return 0;
        double sum = 0;
        int samples = 0;
        for (int i = 0; i + bytes <= count; i += bytes)
        {
            float value;
            if (encoding == WaveFormatEncoding.IeeeFloat && bytes == 4) value = BitConverter.ToSingle(buffer, i);
            else if (encoding != WaveFormatEncoding.Pcm) return 0;
            else if (bytes == 2) value = BitConverter.ToInt16(buffer, i) / 32768f;
            else if (bytes == 3) value = ((buffer[i] << 8 | buffer[i + 1] << 16 | buffer[i + 2] << 24) >> 8) / 8388608f;
            else if (bytes == 4) value = (float)(BitConverter.ToInt32(buffer, i) / 2147483648.0);
            else value = (buffer[i] - 128) / 128f;
            if (!float.IsFinite(value)) continue;
            sum += Math.Clamp((double)value * value, 0, 1);
            samples++;
        }
        return samples > 0 ? (float)Math.Sqrt(sum / samples) : 0;
    }
}

public static class VoiceTranscriptFilter
{
    private static readonly Regex WakeWord = new(@"(?<!\p{L})(?:sentinel|sentynel|sentynelu|centinel|centynel|centenel|santinel|sentnel)(?:\s*x)?(?!\p{L})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly string[] Hallucinations = ["dziękuję za uwagę", "dziekuje za uwage", "dziękuję za oglądanie", "dziekuje za ogladanie", "napisy stworzone przez", "thank you for watching", "thanks for watching", "subtitles by"];
    public static string Clean(string? text) => Regex.Replace(text ?? "", @"\s+", " ").Trim();
    public static bool ContainsWakeWord(string text) => WakeWord.IsMatch(text ?? "");
    public static bool ContainsWakeWord(string text, string mode) => mode == "Strict"
        ? Regex.IsMatch(text ?? "", @"(?<!\p{L})sentinel(?:\s*x)?(?!\p{L})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
        : ContainsWakeWord(text) || (mode == "Forgiving" && Regex.IsMatch(text ?? "", @"(?<!\p{L})(?:sentinelu|centinelu|sentinelka|senty nel)(?!\p{L})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

    public static string? RejectionReason(string text, int samples)
    {
        text = Clean(text);
        if (text.Length == 0) return "empty_transcript";
        if (!text.Any(char.IsLetter)) return "no_words";
        string normalized = text.TrimEnd('.', '!', '?', ' ', '…').ToLowerInvariant();
        if (Hallucinations.Contains(normalized)) return "known_asr_hallucination";
        if (text.Length > Math.Max(90, samples / 16000.0 * 50)) return "implausible_transcript_length";
        string[] words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 8 && words.Distinct().Count() <= 2) return "repeated_words";
        return null;
    }
    public static bool IsSuspicious(string text, int samples)
    {
        return RejectionReason(text, samples) != null;
    }
}
