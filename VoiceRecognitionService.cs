using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SentinelX;

public sealed class VoiceRecognitionService : IDisposable
{
    private readonly VoiceModelManager modelManager;
    private readonly Func<bool> externalNetworkAllowed;
    private readonly AudioEnhancementService audioEnhancer = new();
    private readonly object audioLock = new();
    private readonly SemaphoreSlim initializeLock = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Channel<PendingSegment> queue = Channel.CreateBounded<PendingSegment>(new BoundedChannelOptions(5)
    { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
    private readonly Task worker;
    private readonly Func<VoiceSettings>? settingsProvider;
    private readonly VoiceUtteranceLogService utteranceLog = new();
    private readonly VoiceSignalHistory signalHistory = new();
    private VoiceRuntimeOptions options = new();
    private DateTimeOffset audioTimelineStart;
    private CaptureSession? session;
    private SileroVadService? vadService;
    private Qwen3AsrService? qwenService;
    private WhisperFallbackAsrService? whisperService;
    private WaveInEvent? microphone;
    private WasapiLoopbackCapture? playbackMonitor;
    private volatile bool disposed, initialized, isProcessing, wakeOnlyMode, speechDetected, recognitionSuppressed;
    private int generation, droppedSegments;
    private long lastRecognitionMilliseconds;
    private volatile float systemAudioLevel;
    private sealed class CaptureSession
    {
        private readonly CancellationTokenSource source;
        private int references = 1;
        public CancellationToken Token { get; }
        public CaptureSession(CancellationToken lifetimeToken)
        {
            source = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
            Token = source.Token;
        }
        public void AddReference() => Interlocked.Increment(ref references);
        public void Release() { if (Interlocked.Decrement(ref references) == 0) source.Dispose(); }
        public void Retire() { source.Cancel(); Release(); }
    }
    private record PendingSegment(float[] Samples, int Generation, CaptureSession Session,
        bool WakeOnly, VoiceRuntimeOptions Options, VoiceUtteranceRecord Record, long EnqueuedAt)
    {
        public CancellationToken Token => Session.Token;
    }

    public VoiceRecognitionService(Func<VoiceSettings>? settingsProvider = null, Func<bool>? externalNetworkAllowed = null)
    {
        this.settingsProvider = settingsProvider;
        this.externalNetworkAllowed = externalNetworkAllowed ?? (() => true);
        modelManager = new VoiceModelManager(() => this.externalNetworkAllowed());
        ApplySettings();
        worker = Task.Run(ProcessQueueAsync);
    }
    public bool IsListening { get; private set; }
    public bool IsReady => initialized;
    public bool HasLocalModels => modelManager.IsSileroReady() && (modelManager.IsQwenReady() || modelManager.IsWhisperReady());
    public bool IsProcessing => isProcessing;
    public bool IsWakeOnlyMode => wakeOnlyMode;
    public int SelectedDeviceNumber { get; private set; }
    public float CurrentAudioLevel { get; private set; }
    public float CurrentRawAudioLevel { get; private set; }
    public float CurrentEnhancedRms { get; private set; }
    public float CurrentPlaybackRms => systemAudioLevel;
    public float CurrentRawPeak { get; private set; }
    public float CurrentEnhancedPeak { get; private set; }
    public float CurrentSnrDb => AudioSignalMath.EstimateSnrDb(CurrentRawAudioLevel, CurrentNoiseFloor);
    public float CurrentGain => audioEnhancer.CurrentGain;
    public float CurrentSpeechThreshold { get; private set; }
    public float CurrentRawDbfs => AudioEnhancementService.RmsToDbfs(CurrentRawAudioLevel);
    public float CurrentNoiseFloor => audioEnhancer.NoiseFloor;
    public bool IsCalibrating => IsListening && audioEnhancer.IsCalibrating;
    public bool SpeechDetected => speechDetected;
    public bool IsClipping { get; private set; }
    public int QueueDepth => queue.Reader.CanCount ? queue.Reader.Count : 0;
    public int DroppedSegments => Volatile.Read(ref droppedSegments);
    public long LastRecognitionMilliseconds => Interlocked.Read(ref lastRecognitionMilliseconds);
    public string LastTranscript { get; private set; } = "";
    public string LastDecision { get; private set; } = "Brak wypowiedzi";
    public string? LastRecognitionError { get; private set; }
    public string SelectedDeviceName { get; private set; } = "";
    public string UtteranceLogPath => utteranceLog.LogPath;
    public float SystemAudioLevel => systemAudioLevel;
    public string CurrentAsrEngine => qwenService?.IsReady == true ? "Qwen3-ASR 0.6B INT8" : whisperService?.IsReady == true ? "Whisper Small PL" : "Brak gotowego modelu ASR";
    public event Action<float>? AudioLevelChanged;
    public event Action<string>? SpeechRecognized;
    public event Action<string>? StatusChanged;
    public event Action<string>? ErrorOccurred;

    public bool RecognitionSuppressed
    {
        get => recognitionSuppressed;
        set
        {
            lock (audioLock)
            {
                if (recognitionSuppressed == value || disposed) return;
                recognitionSuppressed = value;
                ResetAudioPipelineLocked();
                RotateSessionLocked();
            }
        }
    }

    private void RotateSessionLocked()
    {
        Interlocked.Increment(ref generation);
        session?.Retire();
        session = IsListening ? new CaptureSession(lifetime.Token) : null;
    }

    public void ApplySettings()
    {
        var updated = VoiceRuntimeOptions.FromSettings(settingsProvider?.Invoke());
        lock (audioLock)
        {
            ThrowIfDisposed();
            bool reset = vadService?.Configure(updated) == true;
            options = updated;
            audioEnhancer.Configure(updated);
            if (reset) ResetAudioPipelineLocked();
        }
    }

    private void ResetAudioPipelineLocked()
    {
        vadService?.Reset();
        signalHistory.Reset();
        speechDetected = false;
    }

    public async Task InstallQwenAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        StopListening();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        await initializeLock.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            await modelManager.EnsureSileroAsync(EmitStatus, linked.Token).ConfigureAwait(false);
            await modelManager.EnsureQwenAsync(EmitStatus, linked.Token).ConfigureAwait(false);
            initialized = false;
        }
        finally { initializeLock.Release(); }
        await InitializeAsync(false, linked.Token).ConfigureAwait(false);
    }

    public Task InitializeAsync() => InitializeAsync(false);

    /// <summary>Local-only unless explicitly called with allowModelDownloads: true by a user action.</summary>
    public async Task InitializeAsync(bool allowModelDownloads, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        await initializeLock.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (initialized) return;
            linked.Token.ThrowIfCancellationRequested();
            if (!modelManager.IsSileroReady())
            {
                if (!allowModelDownloads) throw MissingModels();
                await modelManager.EnsureSileroAsync(EmitStatus, linked.Token).ConfigureAwait(false);
            }
            // Reuse a complete local primary model first. Never download a second large model automatically.
            if (modelManager.IsQwenReady())
            {
                try
                {
                    qwenService ??= new Qwen3AsrService(modelManager.QwenModelDirectory);
                    EmitStatus("Ładowanie lokalnego Qwen3-ASR...");
                    await qwenService.InitializeAsync(linked.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    qwenService?.Dispose(); qwenService = null;
                    EmitStatus("Qwen niedostępny: " + ex.Message);
                }
            }
            if (qwenService?.IsReady != true || modelManager.IsWhisperReady())
            {
                if (!modelManager.IsWhisperReady())
                {
                    if (!allowModelDownloads) throw MissingModels();
                    await modelManager.EnsureWhisperAsync(EmitStatus, linked.Token).ConfigureAwait(false);
                }
                whisperService ??= new WhisperFallbackAsrService(modelManager.WhisperModelPath);
                EmitStatus("Ładowanie lokalnego Whisper Small (język polski)...");
                await whisperService.InitializeAsync(linked.Token).ConfigureAwait(false);
            }
            linked.Token.ThrowIfCancellationRequested();
            vadService ??= new SileroVadService(modelManager.SileroModelPath, options);
            vadService.SetWakeOnlyMode(wakeOnlyMode);
            initialized = true;
            EmitStatus(CurrentAsrEngine + " + Silero • gotowe");
        }
        finally { initializeLock.Release(); }
    }

    private static InvalidOperationException MissingModels() => new(
        "Brak kompletnego lokalnego modelu mowy. Wybierz „Pobierz model głosu” (Whisper Small, około 488 MB). Rozpoznawanie będzie działało lokalnie; nic nie jest pobierane przy starcie aplikacji.");

    public int GetMicrophoneCount() => WaveInEvent.DeviceCount;
    public IReadOnlyList<string> GetMicrophones() => Enumerable.Range(0, WaveInEvent.DeviceCount)
        .Select(i => $"{i}: {WaveInEvent.GetCapabilities(i).ProductName}" + (IsLikelyLoopbackDevice(i) ? " [DŹWIĘK SYSTEMOWY]" : "")).ToArray();
    public int GetRecommendedMicrophoneDevice()
    {
        if (WaveInEvent.DeviceCount == 0) return -1;
        string[] preferred = ["microphone", "mikrofon", "mic", "headset", "usb"];
        return Enumerable.Range(0, WaveInEvent.DeviceCount).Where(i => !IsLikelyLoopbackDevice(i))
            .OrderByDescending(i => preferred.Any(p => WaveInEvent.GetCapabilities(i).ProductName.Contains(p, StringComparison.OrdinalIgnoreCase)))
            .DefaultIfEmpty(0).First();
    }
    public bool IsLikelyLoopbackDevice(int number)
    {
        if (number < 0 || number >= WaveInEvent.DeviceCount) return false;
        string name = WaveInEvent.GetCapabilities(number).ProductName;
        string[] words = ["stereo mix", "stereomix", "miks stereo", "what u hear", "what you hear", "wave out", "loopback", "virtual audio", "cable output", "voicemeeter output"];
        return words.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    public void SetWakeOnlyMode(bool enabled)
    {
        lock (audioLock)
        {
            wakeOnlyMode = enabled;
            vadService?.SetWakeOnlyMode(enabled);
        }
        if (IsListening) RestoreStatus();
    }

    public void CalibrateNoise()
    {
        lock (audioLock)
        {
            ThrowIfDisposed();
            audioEnhancer.BeginCalibration();
            ResetAudioPipelineLocked();
            RotateSessionLocked();
        }
        EmitStatus("Kalibracja szumu • zachowaj ciszę przez 2 sekundy");
    }

    public string GetDiagnostics() => $"Silnik: {CurrentAsrEngine}\nMikrofon: {SelectedDeviceNumber}: {SelectedDeviceName}, PCM16 mono 16000 Hz\n" +
        $"Surowy sygnał: {CurrentRawDbfs:0.0} dBFS, RMS {CurrentRawAudioLevel:0.00000}\n" +
        $"Po obróbce: RMS {CurrentEnhancedRms:0.00000}, zastosowany gain {CurrentGain:0.00}×\n" +
        $"Peak RAW: {CurrentRawPeak:0.000}; enhanced: {CurrentEnhancedPeak:0.000}; szacowane SNR: {CurrentSnrDb:0.0} dB\n" +
        $"Szum: {AudioEnhancementService.RmsToDbfs(CurrentNoiseFloor):0.0} dBFS; VAD: {SpeechDetected}; przesterowanie: {IsClipping}\n" +
        $"Próg VAD: {options.VadThreshold:0.00}; pauza kończąca zdanie: {options.EndOfSpeechMilliseconds} ms; limit wypowiedzi: {options.MaximumSpeechSeconds} s\n" +
        $"Kolejka: {QueueDepth}/5; pominięte segmenty: {DroppedSegments}; ostatni ASR: {LastRecognitionMilliseconds} ms\n" +
        $"Ostatni tekst: {(LastTranscript.Length == 0 ? "—" : LastTranscript)}\nDecyzja: {LastDecision}\n" +
        (LastRecognitionError == null ? "" : $"Błąd ASR: {LastRecognitionError}\n") +
        $"Silero: {modelManager.IsSileroReady()}; Whisper: {modelManager.IsWhisperReady()}; Qwen: {modelManager.IsQwenReady()}\n" +
        $"Log metryk: {(options.UtteranceLoggingEnabled ? utteranceLog.LogPath : "wyłączony")}; tekst w logu: {options.IncludeTranscriptInLogs}\n" +
        (utteranceLog.LastError == null ? "" : $"Błąd zapisu logu: {utteranceLog.LastError}\n") +
        $"Pominięte wpisy logu: {utteranceLog.DroppedEntries}. Audio przetwarzane w pamięci; nagrania nie są zapisywane.";

    public void StartListening(int deviceNumber = 0)
    {
        ThrowIfDisposed();
        if (!initialized) throw MissingModels();
        if (IsListening) return;
        if (WaveInEvent.DeviceCount == 0) throw new InvalidOperationException("Windows nie wykrywa mikrofonu.");
        if (deviceNumber < 0 || deviceNumber >= WaveInEvent.DeviceCount) throw new ArgumentOutOfRangeException(nameof(deviceNumber));
        if (IsLikelyLoopbackDevice(deviceNumber)) throw new InvalidOperationException("To urządzenie przechwytuje dźwięk systemowy. Wybierz fizyczny mikrofon lub mikrofon zestawu słuchawkowego.");
        ApplySettings();
        var capture = new WaveInEvent { DeviceNumber = deviceNumber, WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 32, NumberOfBuffers = 4 };
        capture.DataAvailable += MicrophoneDataAvailable;
        capture.RecordingStopped += MicrophoneRecordingStopped;
        lock (audioLock)
        {
            SelectedDeviceNumber = deviceNumber;
            SelectedDeviceName = WaveInEvent.GetCapabilities(deviceNumber).ProductName;
            audioEnhancer.Reset(); ResetAudioPipelineLocked();
            session = new CaptureSession(lifetime.Token);
            Interlocked.Increment(ref generation);
            microphone = capture;
            IsListening = true;
        }
        try { capture.StartRecording(); }
        catch (Exception ex)
        {
            StopListening();
            throw new InvalidOperationException("Nie można otworzyć mikrofonu w formacie 16 kHz mono PCM16. Sprawdź urządzenie i dostęp do mikrofonu w ustawieniach Windows. " + ex.Message, ex);
        }
        StartPlaybackMonitor();
        EmitStatus("Kalibracja mikrofonu • zachowaj ciszę przez 2 sekundy");
    }

    public void StopListening() => StopListeningCore(null);

    private void StopListeningCore(object? expectedCapture)
    {
        WaveInEvent? old;
        lock (audioLock)
        {
            if (expectedCapture != null && !ReferenceEquals(expectedCapture, microphone)) return;
            IsListening = false;
            Interlocked.Increment(ref generation);
            session?.Retire();
            session = null;
            old = microphone; microphone = null;
            ResetAudioPipelineLocked();
            CurrentAudioLevel = CurrentRawAudioLevel = CurrentEnhancedRms = CurrentSpeechThreshold = 0;
            CurrentRawPeak = CurrentEnhancedPeak = 0;
            IsClipping = false;
        }
        if (old != null)
        {
            old.DataAvailable -= MicrophoneDataAvailable; old.RecordingStopped -= MicrophoneRecordingStopped;
            try { old.StopRecording(); } catch (Exception) { }
            old.Dispose();
        }
        StopPlaybackMonitor();
        AudioLevelChanged?.Invoke(0);
        EmitStatus("Mikrofon wyłączony");
    }

    private void MicrophoneDataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            float level;
            string? status = null;
            lock (audioLock)
            {
                if (!IsListening || disposed || !ReferenceEquals(sender, microphone) || session == null) return;
                EnhancedAudioFrame frame = audioEnhancer.ProcessPcm16(e.Buffer, e.BytesRecorded, !recognitionSuppressed);
                CurrentRawAudioLevel = frame.RawRms;
                CurrentEnhancedRms = frame.EnhancedRms;
                CurrentRawPeak = frame.RawPeak;
                CurrentEnhancedPeak = frame.Peak;
                CurrentAudioLevel = level = AudioEnhancementService.RmsToMeter(frame.RawRms);
                CurrentSpeechThreshold = AudioEnhancementService.RmsToMeter(frame.GateLevel);
                IsClipping = frame.IsClipping;
                if (frame.IsCalibrating)
                {
                    if (!audioEnhancer.IsCalibrating) status = "Kalibracja zakończona • " + (wakeOnlyMode ? "powiedz Sentinel" : "słucham");
                }
                else if (vadService != null && !recognitionSuppressed)
                {
                    if (signalHistory.SamplePosition == 0)
                        audioTimelineStart = DateTimeOffset.UtcNow.AddSeconds(-frame.Samples.Length / 16000.0);
                    signalHistory.Add(frame);
                    var result = vadService.Process(frame.Samples);
                    if (result.SpeechDetected != speechDetected)
                    {
                        speechDetected = result.SpeechDetected;
                        if (speechDetected) status = "Wykryto mowę";
                    }
                    foreach (var segment in result.Segments)
                    {
                        var record = new VoiceUtteranceRecord
                        {
                            DeviceNumber = SelectedDeviceNumber, DeviceName = SelectedDeviceName,
                            VadStart = audioTimelineStart.AddSeconds(segment.StartSample / 16000.0),
                            VadEnd = audioTimelineStart.AddSeconds((segment.StartSample + segment.Samples.Length) / 16000.0),
                            Signal = signalHistory.Measure(segment.StartSample, segment.Samples), WakeRequired = wakeOnlyMode
                        };
                        if (segment.Samples.Length < options.MinimumSpeechMilliseconds * 16 || record.Signal.EnhancedRms < 0.0003f)
                        {
                            LastDecision = "Odrzucono: wypowiedź zbyt krótka lub cicha";
                            WriteLog(options, record with { Decision = "too_short_or_quiet" });
                            status = LastDecision;
                            continue;
                        }
                        session.AddReference();
                        if (!queue.Writer.TryWrite(new PendingSegment(segment.Samples, generation, session, wakeOnlyMode, options, record, Environment.TickCount64)))
                        {
                            session.Release();
                            Interlocked.Increment(ref droppedSegments);
                            WriteLog(options, record with { Decision = "queue_full" });
                            status = "ASR zajęty • pominięto nowy segment; powtórz po zakończeniu";
                            LastDecision = status;
                        }
                    }
                }
            }
            AudioLevelChanged?.Invoke(level);
            if (status != null) EmitStatus(status);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            LastRecognitionError = ex.Message;
            ErrorOccurred?.Invoke("Błąd wejścia audio: " + ex.Message);
        }
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            await foreach (var item in queue.Reader.ReadAllAsync(lifetime.Token).ConfigureAwait(false))
            {
                if (item.Token.IsCancellationRequested || item.Generation != Volatile.Read(ref generation))
                { WriteLog(item.Options, item.Record with { Decision = "cancelled_before_asr" }); item.Session.Release(); continue; }
                isProcessing = true;
                var timer = Stopwatch.StartNew();
                string text = "", engine = CurrentAsrEngine, decision = "cancelled";
                string? recognitionError = null;
                long queued = Math.Max(0, Environment.TickCount64 - item.EnqueuedAt);
                try
                {
                    EmitStatus(CurrentAsrEngine + " • rozpoznawanie...");
                    if (qwenService?.IsReady == true)
                    {
                        try { text = await qwenService.TranscribeAsync(item.Samples, item.Token).ConfigureAwait(false); }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        { EmitStatus("Qwen: " + ex.Message + " • próba Whisper"); }
                    }
                    if (whisperService?.IsReady == true && (VoiceTranscriptFilter.IsSuspicious(text, item.Samples.Length) || (item.WakeOnly && !VoiceTranscriptFilter.ContainsWakeWord(text, item.Options.WakeWordMode))))
                    {
                        string fallback = await whisperService.TranscribeAsync(item.Samples, item.Token).ConfigureAwait(false);
                        if (!VoiceTranscriptFilter.IsSuspicious(fallback, item.Samples.Length)) { text = fallback; engine = "Whisper Small PL"; }
                    }
                    item.Token.ThrowIfCancellationRequested();
                    text = VoiceTranscriptFilter.Clean(text);
                    if (item.Generation != Volatile.Read(ref generation) || !IsListening || disposed || recognitionSuppressed) continue;
                    LastTranscript = text; LastRecognitionError = null;
                    if (VoiceTranscriptFilter.IsSuspicious(text, item.Samples.Length))
                    {
                        decision = "suspicious_transcript";
                        LastDecision = "ASR odrzucił niepewny tekst: " + (string.IsNullOrWhiteSpace(text) ? "cisza/szum" : text);
                    }
                    else if (item.WakeOnly && !VoiceTranscriptFilter.ContainsWakeWord(text, item.Options.WakeWordMode))
                    {
                        decision = "missing_wake_word";
                        LastDecision = "Usłyszano bez słowa Sentinel: " + text;
                    }
                    else
                    {
                        decision = "accepted";
                        LastDecision = "Rozpoznano i wysłano: " + text;
                        SpeechRecognized?.Invoke(text);
                    }
                    EmitStatus(LastDecision);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { decision = "asr_error"; recognitionError = LastRecognitionError = ex.Message; AppLog.Write(ex); if (!disposed) ErrorOccurred?.Invoke("Błąd rozpoznawania: " + ex.Message); }
                finally
                {
                    Interlocked.Exchange(ref lastRecognitionMilliseconds, timer.ElapsedMilliseconds);
                    isProcessing = false;
                    WriteLog(item.Options, item.Record with { Engine = engine, Transcript = text, Decision = decision, Error = recognitionError, QueueLatencyMilliseconds = queued, AsrLatencyMilliseconds = timer.ElapsedMilliseconds });
                    item.Session.Release();
                }
            }
        }
        catch (OperationCanceledException) { }
        finally { while (queue.Reader.TryRead(out var pending)) pending.Session.Release(); }
    }

    private void WriteLog(VoiceRuntimeOptions config, VoiceUtteranceRecord record)
    {
        if (!config.UtteranceLoggingEnabled) return;
        utteranceLog.TryWrite(config.IncludeTranscriptInLogs ? record with { TranscriptRedacted = false } : record with { Transcript = null, TranscriptRedacted = true });
    }

    private void MicrophoneRecordingStopped(object? sender, StoppedEventArgs e)
    {
        lock (audioLock) { if (!ReferenceEquals(sender, microphone)) return; }
        // Dispose capture from a separate worker, not its own capture callback.
        _ = Task.Run(() =>
        {
            lock (audioLock) { if (!ReferenceEquals(sender, microphone)) return; }
            StopListeningCore(sender);
            if (e.Exception != null) ErrorOccurred?.Invoke("Mikrofon odłączony lub zatrzymany: " + e.Exception.Message);
        });
    }

    private void StartPlaybackMonitor()
    {
        StopPlaybackMonitor();
        WasapiLoopbackCapture? capture = null;
        try
        {
            capture = new WasapiLoopbackCapture();
            playbackMonitor = capture;
            capture.DataAvailable += PlaybackDataAvailable;
            capture.RecordingStopped += PlaybackStopped;
            capture.StartRecording();
        }
        catch (Exception) { if (capture != null) {  capture.RecordingStopped -= PlaybackStopped; capture.Dispose(); } playbackMonitor = null; }
    }
    private void StopPlaybackMonitor()
    {
        var old = playbackMonitor; playbackMonitor = null; systemAudioLevel = 0;
        if (old == null) return;
         old.DataAvailable -= PlaybackDataAvailable; old.RecordingStopped -= PlaybackStopped;
        try { old.StopRecording(); } catch (Exception) { }
        old.Dispose();
    }
    private void PlaybackDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (sender is not WasapiLoopbackCapture capture || !ReferenceEquals(capture, playbackMonitor)) return;
        systemAudioLevel = AudioSignalMath.PlaybackRms(e.Buffer, e.BytesRecorded, capture.WaveFormat);
    }
    private void PlaybackStopped(object? sender, StoppedEventArgs e)
    { if (ReferenceEquals(sender, playbackMonitor)) systemAudioLevel = 0; }
    private void RestoreStatus() => EmitStatus(!IsListening ? "Mikrofon wyłączony" : IsCalibrating ? "Kalibracja • 2 sekundy ciszy" : wakeOnlyMode ? "STANDBY • powiedz Sentinel" : "ACTIVE • słucham");
    private void EmitStatus(string text) { if (!disposed) StatusChanged?.Invoke(text); }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        StopListening();
        queue.Writer.TryComplete();
        // A native decoder may still be in flight. Release its resources only after it exits.
        _ = Task.Run(async () =>
        {
            await worker.ConfigureAwait(false);
            await initializeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                vadService?.Dispose(); qwenService?.Dispose(); whisperService?.Dispose(); modelManager.Dispose();
                await utteranceLog.DisposeAsync().ConfigureAwait(false);
                lifetime.Dispose();
            }
            finally { initializeLock.Release(); }
        });
    }
}

