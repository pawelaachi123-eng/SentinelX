using System.Windows.Threading;
using SentinelX.Core;
using SentinelX.Models;
using SentinelX.Services.Actions;
using SentinelX.Services.Settings;
namespace SentinelX.Services.Voice;

public sealed class VoiceService : IVoiceService, IDisposable
{
    private readonly VoiceRecognitionService capture;
    private readonly ISettingsService settings;
    private readonly IActionEngine engine;
    private readonly IUiDispatcher dispatcher;
    private readonly SpeechOutputService speech;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private CancellationTokenSource? starting;
    private DateTime activeUntil;
    private int generation;
    private bool disposed;
    public bool HasLocalModels => capture.HasLocalModels;
    public VoiceState State { get; private set; }
    public string Status { get; private set; } = "Mikrofon wyłączony";
    public event Action? Changed;
    public event Action<VoiceMetrics>? MetricsUpdated;
    public event Action<string>? CommandRecognized;

    public VoiceService(VoiceRecognitionService capture, ISettingsService settings, IActionEngine engine, IUiDispatcher dispatcher, SpeechOutputService speech)
    {
        this.capture = capture; this.settings = settings; this.engine = engine; this.dispatcher = dispatcher; this.speech = speech;
        capture.SpeechRecognized += Recognized;
        capture.StatusChanged += SetStatus;
        capture.ErrorOccurred += SetStatus;
        settings.Changed += ApplySettings;
        engine.Changed += EngineChanged;
        speech.Completed += SpeechCompleted;
        timer.Tick += Tick;
    }
    private void SetStatus(string status) => dispatcher.Post(() => { if (!disposed) { Status = status; Changed?.Invoke(); } });
    private void ApplySettings() => capture.ApplySettings();
    private void EngineChanged() { if (engine.IsStopped) dispatcher.Post(Stop); }
    public IReadOnlyList<string> GetMicrophones() => capture.GetMicrophones();
    public async Task StartAsync(int device, bool downloadModels, CancellationToken token)
    {
        if (engine.IsStopped) { SetStatus("Wznów Sentinel przed włączeniem mikrofonu."); return; }
        Stop();
        int version = generation;
        using var source = CancellationTokenSource.CreateLinkedTokenSource(token);
        starting = source;
        try
        {
            SetStatus(downloadModels ? "Pobieranie modeli — może potrwać kilka minut…" : "Ładowanie lokalnych modeli…");
            await capture.InitializeAsync(downloadModels, source.Token);
            source.Token.ThrowIfCancellationRequested();
            if (version != generation || disposed || engine.IsStopped) return;
            capture.SetWakeOnlyMode(true); capture.StartListening(device);
            State = VoiceState.Standby; timer.Start();
            SetStatus("STANDBY · powiedz Sentinel");
        }
        finally { if (ReferenceEquals(starting, source)) starting = null; }
    }
    private void Recognized(string text)
    {
        int version = generation;
        dispatcher.Post(() =>
        {
            if (disposed || version != generation || State == VoiceState.Off || engine.IsStopped) return;
            bool wakeAllowed = VoiceTranscriptFilter.ContainsWakeWord(text, settings.Current.Voice.WakeWordMode);
            // Since 0.91 the wake word may sit anywhere in the sentence — without it Sentinel simply
            // keeps listening (early return), exactly as the user asked.
            if (State == VoiceState.Standby && !wakeAllowed) return;
            if (State == VoiceState.Active && DateTime.Now > activeUntil && !wakeAllowed) return;
            string command = wakeAllowed ? CommandText.StripWakeWord(text) : text.Trim();
            State = VoiceState.Active; capture.SetWakeOnlyMode(false);
            activeUntil = DateTime.Now.AddMinutes(settings.Current.Voice.ConversationTimeoutMinutes);
            SetStatus("ACTIVE · słucham");
            if (command.Length > 0) CommandRecognized?.Invoke(command);
        });
    }
    private void Tick(object? sender, EventArgs args)
    {
        if (!capture.IsListening) { Stop(); return; }
        if (State == VoiceState.Active && DateTime.Now > activeUntil && !engine.IsBusy)
        { State = VoiceState.Standby; capture.SetWakeOnlyMode(true); SetStatus("STANDBY · powiedz Sentinel"); }
        MetricsUpdated?.Invoke(new(State, capture.CurrentRawAudioLevel * 100, capture.CurrentEnhancedRms * 100,
            capture.CurrentNoiseFloor, capture.CurrentSnrDb, capture.CurrentGain, capture.SpeechDetected, capture.LastTranscript));
    }
    public void Stop()
    {
        generation++; starting?.Cancel(); capture.StopListening(); speech.Stop(); timer.Stop();
        State = VoiceState.Off; SetStatus("Mikrofon wyłączony");
        MetricsUpdated?.Invoke(new(State, 0, 0, 0, 0, 0, false, ""));
    }
    public void Calibrate() { if (capture.IsListening) capture.CalibrateNoise(); else SetStatus("Najpierw włącz mikrofon."); }
    public void Speak(string text)
    {
        if (!settings.Current.Voice.SpeakResponses || engine.IsStopped || State == VoiceState.Off) return;
        capture.RecognitionSuppressed = true;
        speech.Speak(text, settings.Current.Voice.SpeechVoice, settings.Current.Voice.SpeechRate, settings.Current.Voice.SpeechVolume);
        if (!speech.IsSpeaking) capture.RecognitionSuppressed = false;
    }
    private void SpeechCompleted() => capture.RecognitionSuppressed = false;
    public void Dispose()
    {
        if (disposed) return;
        Stop(); disposed = true;
        timer.Tick -= Tick; capture.SpeechRecognized -= Recognized; capture.StatusChanged -= SetStatus;
        capture.ErrorOccurred -= SetStatus; settings.Changed -= ApplySettings; engine.Changed -= EngineChanged;
        speech.Completed -= SpeechCompleted;
    }
}
