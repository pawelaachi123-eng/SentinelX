using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;

namespace SentinelX;

public sealed class WhisperFallbackAsrService : IDisposable
{
    private readonly string modelPath;
    private WhisperFactory? factory;
    private WhisperProcessor? processor;
    private readonly SemaphoreSlim recognitionLock = new(1, 1);
    private volatile bool disposed;
    public bool IsReady => !disposed && processor != null;
    public WhisperFallbackAsrService(string modelPath) => this.modelPath = modelPath;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await recognitionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (processor != null) return;
            await Task.Run(() =>
            {
                if (!File.Exists(modelPath)) throw new FileNotFoundException("Brak lokalnego modelu Whisper.", modelPath);
                factory = WhisperFactory.FromPath(modelPath);
                try
                {
                    processor = factory.CreateBuilder().WithLanguage("pl")
                        .WithThreads(Math.Clamp(Environment.ProcessorCount / 2, 1, 8))
                        .WithNoContext().WithTemperature(0).WithNoSpeechThreshold(0.6f).Build();
                }
                catch { factory.Dispose(); factory = null; throw; }
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
            var local = processor ?? throw new InvalidOperationException("Whisper nie został zainicjalizowany.");
            var text = new StringBuilder();
            // Float samples avoid re-encoding/quantizing audio into an intermediate WAV file.
            await foreach (var segment in local.ProcessAsync(samples, cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(segment.Text)) text.Append(segment.Text.Trim()).Append(' ');
            }
            cancellationToken.ThrowIfCancellationRequested();
            return text.ToString().Trim();
        }
        finally { recognitionLock.Release(); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        recognitionLock.Wait();
        try { processor?.Dispose(); processor = null; factory?.Dispose(); factory = null; }
        finally { recognitionLock.Release(); }
    }
}
