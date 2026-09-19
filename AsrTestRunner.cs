using System.Diagnostics;
using System.IO;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Text.Json;
using NAudio.Wave;

namespace SentinelX;
internal static class AsrTestRunner
{
    internal static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        string wav = Path.Combine(directory, "polish-speech.wav");
        using (var synthesizer = new SpeechSynthesizer())
        {
            var polish = synthesizer.GetInstalledVoices().FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName == "pl") ?? throw new InvalidOperationException("Brak polskiego głosu do testu.");
            synthesizer.SelectVoice(polish.VoiceInfo.Name);
            synthesizer.SetOutputToWaveFile(wav, new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
            synthesizer.Speak("Sentinel, pokaż użycie pamięci RAM.");
        }
        float[] samples;
        using (var reader = new WaveFileReader(wav))
        {
            var provider = reader.ToSampleProvider(); var data = new List<float>(); float[] buffer = new float[16000]; int count;
            while ((count = provider.Read(buffer.AsSpan())) > 0) data.AddRange(buffer.Take(count)); samples = data.ToArray();
        }
        string models = Path.Combine(AppContext.BaseDirectory, "Models");
        using var asr = new WhisperFallbackAsrService(Path.Combine(models, "ggml-small.bin"));
        var stopwatch = Stopwatch.StartNew(); await asr.InitializeAsync(); string transcript = await asr.TranscribeAsync(samples, CancellationToken.None);
        if (!transcript.Contains("ram", StringComparison.OrdinalIgnoreCase) && !transcript.Contains("pamię", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Test ASR: nie rozpoznano treści próbki: " + transcript);
        using var vad = new SileroVadService(Path.Combine(models, "silero_vad.onnx"));
        vad.SetWakeOnlyMode(false); int segments = 0;
        foreach (float[] frame in new float[16000].Concat(samples).Concat(new float[48000]).Chunk(512)) segments += vad.Process(frame).CompletedSegments.Count;
        if (segments == 0) throw new InvalidOperationException("Test VAD: wypowiedź nie została zakończona po ciszy.");
        var diagnostics = new PcDiagnosticService();
        var services = await diagnostics.GetServicesAsync(); var security = await diagnostics.GetSecurityAsync();
        File.WriteAllText(Path.Combine(directory, "asr-results.json"), JsonSerializer.Serialize(new { transcript, elapsedMilliseconds = stopwatch.ElapsedMilliseconds, voiceActivitySegments = segments, servicesReadStatus = services.Status, securityReadStatus = security.Status, note = "Prawdziwy model Whisper Small i Silero ONNX, polska mowa syntetyczna. Mikrofon nie był nagrywany." }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
