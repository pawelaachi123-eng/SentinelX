using SherpaOnnx;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SentinelX;

public sealed class Qwen3AsrService : IDisposable
{
    private readonly string modelDirectory;
    private readonly SemaphoreSlim recognitionLock = new(1, 1);
    private OfflineRecognizer? recognizer;
    private volatile bool disposed;
    public bool IsReady => !disposed && recognizer != null;
    public Qwen3AsrService(string modelDirectory) => this.modelDirectory = modelDirectory;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await recognitionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (recognizer != null) return;
            recognizer = await Task.Run(() =>
            {
                string frontend = Path.Combine(modelDirectory, "conv_frontend.onnx");
                string encoder = Path.Combine(modelDirectory, "encoder.int8.onnx");
                string decoder = Path.Combine(modelDirectory, "decoder.int8.onnx");
                string tokenizer = Path.Combine(modelDirectory, "tokenizer");
                if (!File.Exists(frontend) || !File.Exists(encoder) || !File.Exists(decoder) || !Directory.Exists(tokenizer))
                    throw new FileNotFoundException("Brakuje plików modelu Qwen3-ASR.");
                var config = new OfflineRecognizerConfig();
                config.ModelConfig.Qwen3Asr.ConvFrontend = frontend;
                config.ModelConfig.Qwen3Asr.Encoder = encoder;
                config.ModelConfig.Qwen3Asr.Decoder = decoder;
                config.ModelConfig.Qwen3Asr.Tokenizer = tokenizer;
                config.ModelConfig.Qwen3Asr.Hotwords = string.Empty;
                config.ModelConfig.Tokens = string.Empty;
                config.ModelConfig.NumThreads = Math.Clamp(Environment.ProcessorCount / 3, 1, 6);
                config.ModelConfig.Debug = 0;
                config.ModelConfig.Provider = "cpu";
                return new OfflineRecognizer(config);
            }, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally { recognitionLock.Release(); }
    }

    public async Task<string> TranscribeAsync(float[] samples, CancellationToken cancellationToken = default)
    {
        if (samples.Length == 0) return string.Empty;
        await recognitionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var local = recognizer ?? throw new InvalidOperationException("Qwen nie został zainicjalizowany.");
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = local.CreateStream();
                stream.AcceptWaveform(16000, samples);
                local.Decode(stream);
                // sherpa's synchronous native decoder has no cancellation API: discard cancelled results.
                cancellationToken.ThrowIfCancellationRequested();
                return stream.Result.Text?.Trim() ?? string.Empty;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { recognitionLock.Release(); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        recognitionLock.Wait();
        try { recognizer?.Dispose(); recognizer = null; }
        finally { recognitionLock.Release(); }
        // Do not dispose the semaphore while callers may still be waiting to observe disposed.
    }
}
