using System.Windows.Threading;
using SentinelX;
using SentinelX.Core;
using SentinelX.Models;
using SentinelX.Services.Actions;
using SentinelX.Services.Settings;

namespace SentinelX.Services.Voice;

/// <summary>Owns the user-facing voice session; capture and speech output remain local services.</summary>
public sealed class VoiceService : IVoiceService, IDisposable
{
    private readonly IVoiceCapture capture;
    private readonly ISettingsService settings;
    private readonly IActionEngine engine;
    private readonly IUiDispatcher dispatcher;
    private readonly ISpeechOutput speech;
    private readonly VoiceStateMachine lifecycle = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly SemaphoreSlim startGate = new(1, 1);
    private readonly object lifecycleGate = new();
    private CancellationTokenSource? starting;
    private DateTime activeUntil;
    private int generation;
    private bool disposed;

    public bool HasLocalModels => capture.HasLocalModels;
    public VoiceState State { get; private set; }
    public VoiceStateInfo Lifecycle => lifecycle.Current;
    public string Status { get; private set; } = "Mikrofon wyłączony";
    public event Action? Changed;
    public event Action<VoiceMetrics>? MetricsUpdated;
    public event Action<string>? CommandRecognized;

    public VoiceService(IVoiceCapture capture, ISettingsService settings, IActionEngine engine,
        IUiDispatcher dispatcher, ISpeechOutput speech)
    {
        this.capture = capture;
        this.settings = settings;
        this.engine = engine;
        this.dispatcher = dispatcher;
        this.speech = speech;
        capture.SpeechRecognized += Recognized;
        capture.RecognitionProcessingChanged += RecognitionProcessingChanged;
        capture.StatusChanged += SetStatus;
        capture.ErrorOccurred += CaptureError;
        settings.Changed += ApplySettings;
        engine.Changed += EngineChanged;
        speech.Completed += SpeechCompleted;
        speech.Failed += SpeechFailed;
        lifecycle.StateChanged += LifecycleChanged;
        timer.Tick += Tick;
    }

    private void SetStatus(string status) => dispatcher.Post(() =>
    {
        if (disposed) return;
        Status = status;
        RaiseChanged();
    });

    private void ApplySettings()
    {
        try { capture.ApplySettings(); }
        catch (ObjectDisposedException) when (disposed) { }
        catch (Exception ex) { AppLog.Write("Voice", "Warning", "Updated voice settings could not be applied to the active capture.", ex); }
    }

    private void EngineChanged()
    {
        if (engine.IsStopped)
        {
            dispatcher.Post(Stop);
            return;
        }
        dispatcher.Post(RefreshLifecycleFromEngine);
    }

    private void RefreshLifecycleFromEngine()
    {
        if (disposed) return;
        if (State == VoiceState.Off) { SetLifecycle(VoiceStateKind.OFF, "Mikrofon wyłączony."); return; }
        if (State == VoiceState.Standby) { SetLifecycle(VoiceStateKind.STANDBY, "Mikrofon oczekuje lokalnie na słowo Sentinel."); return; }
        if (speech.IsSpeaking) { SetLifecycle(VoiceStateKind.SPEAKING, "Odczytuję krótką odpowiedź."); return; }
        if (engine.IsBusy)
        {
            SetLifecycle(engine.IsStreaming ? VoiceStateKind.THINKING : VoiceStateKind.EXECUTING,
                engine.IsStreaming ? "Trwa lokalne generowanie odpowiedzi." : "Trwa wykonywanie polecenia.");
            return;
        }
        if (Lifecycle.State != VoiceStateKind.TRANSCRIBING)
            SetLifecycle(VoiceStateKind.LISTENING, "Tryb aktywny; polecenie głosowe musi zawierać słowo Sentinel.");
    }

    public IReadOnlyList<string> GetMicrophones() => capture.GetMicrophones();

    private static string RequireAvailableMicrophone(IReadOnlyList<string> microphones, int device)
    {
        if (device < 0 || device >= microphones.Count)
            throw new InvalidOperationException("Wybrany mikrofon nie jest już dostępny. Odśwież listę urządzeń i wybierz działający mikrofon.");
        string name = microphones[device];
        if (name.Contains("[DŹWIĘK SYSTEMOWY]", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Wybrane urządzenie przechwytuje dźwięk systemowy. Wybierz fizyczny mikrofon lub mikrofon zestawu słuchawkowego.");
        return name;
    }

    public async Task StartAsync(int device, bool downloadModels, CancellationToken token)
    {
        await startGate.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (engine.IsStopped)
            {
                SetStatus("Wznów Sentinel przed włączeniem mikrofonu.");
                return;
            }

            Stop();
            int version = Volatile.Read(ref generation);
            using var source = CancellationTokenSource.CreateLinkedTokenSource(token);
            lock (lifecycleGate)
            {
                if (version != generation || disposed) return;
                starting = source;
            }

            try
            {
                string selectedMicrophone = RequireAvailableMicrophone(capture.GetMicrophones(), device);

                SetStatus(downloadModels ? "Pobieranie modeli — może potrwać kilka minut…" : "Ładowanie lokalnych modeli…");
                await capture.InitializeAsync(downloadModels, source.Token);
                source.Token.ThrowIfCancellationRequested();
                if (version != Volatile.Read(ref generation) || disposed || engine.IsStopped) return;
                string currentMicrophone = RequireAvailableMicrophone(capture.GetMicrophones(), device);
                if (!string.Equals(selectedMicrophone, currentMicrophone, StringComparison.Ordinal))
                    throw new InvalidOperationException("Lista urządzeń mikrofonu zmieniła się podczas ładowania modeli. Odśwież listę i wybierz mikrofon ponownie.");

                lock (lifecycleGate)
                {
                    if (version != generation || disposed || source.IsCancellationRequested || engine.IsStopped) return;
                    capture.SetWakeOnlyMode(true);
                    capture.StartListening(device);
                    if (version != generation || disposed || source.IsCancellationRequested || engine.IsStopped)
                    {
                        capture.StopListening();
                        return;
                    }
                    if (!capture.IsListening)
                        throw new InvalidOperationException("Mikrofon nie rozpoczął nasłuchu. Odłącz i podłącz urządzenie, a następnie spróbuj ponownie.");
                    State = VoiceState.Standby;
                    activeUntil = default;
                    timer.Start();
                }

                SetLifecycleIfCurrent(version, VoiceStateKind.STANDBY, "Mikrofon aktywny; wykrywanie słowa Sentinel działa lokalnie.");
                if (version == Volatile.Read(ref generation)) SetStatus("STANDBY · powiedz Sentinel");
            }
            catch (OperationCanceledException) when (source.IsCancellationRequested)
            {
                try { capture.StopListening(); }
                catch (Exception ex) { AppLog.Write("Voice", "Warning", "Capture cleanup after cancelled startup failed.", ex); }
                try { capture.RecognitionSuppressed = false; }
                catch (Exception ex) { AppLog.Write("Voice", "Warning", "Could not clear recognition suppression after cancelled startup.", ex); }
                if (version == Volatile.Read(ref generation) && !disposed)
                {
                    State = VoiceState.Off;
                    timer.Stop();
                    SetLifecycleIfCurrent(version, VoiceStateKind.OFF, "Uruchamianie mikrofonu anulowano.");
                    SetStatus("Przerwano uruchamianie głosu. Mikrofon jest wyłączony.");
                }
                throw;
            }
            catch (Exception ex)
            {
                try { capture.StopListening(); } catch (Exception stopError) { AppLog.Write("Voice", "Warning", "Capture cleanup after startup failure failed.", stopError); }
                try { capture.RecognitionSuppressed = false; } catch (Exception suppressionError) { AppLog.Write("Voice", "Warning", "Could not clear speech suppression after startup failure.", suppressionError); }
                if (version == Volatile.Read(ref generation) && !disposed)
                {
                    State = VoiceState.Off;
                    timer.Stop();
                    string detail = "Uruchomienie mikrofonu nie powiodło się: " + ex.Message;
                    SetLifecycleIfCurrent(version, VoiceStateKind.ERROR, detail);
                    SetStatus(detail);
                }
                AppLog.Write("Voice", "Error", "Voice capture could not start.", ex);
                throw;
            }
            finally
            {
                lock (lifecycleGate)
                    if (ReferenceEquals(starting, source)) starting = null;
            }
        }
        finally { startGate.Release(); }
    }

    private void Recognized(string text)
    {
        int version = Volatile.Read(ref generation);
        dispatcher.Post(() =>
        {
            if (disposed || version != Volatile.Read(ref generation) || State == VoiceState.Off || engine.IsStopped) return;
            bool wakeAllowed = VoiceTranscriptFilter.ContainsWakeWord(text, settings.Current.Voice.WakeWordMode);
            // No command is dispatched until the utterance contains the whole-word wake phrase.
            if (!wakeAllowed) return;

            string command = CommandText.StripWakeWord(text);
            switch (VoiceSessionCommandParser.Parse(command))
            {
                case VoiceSessionCommand.Off:
                    EnterWakeWordStandby("Tryb rozmowy wyłączony. Mikrofon pozostaje w lokalnym trybie wake-word; powiedz „Sentinel on” albo wyłącz go w interfejsie.");
                    return;
                case VoiceSessionCommand.On:
                    EnterActiveListening("Tryb aktywny. Każde polecenie nadal musi zawierać słowo Sentinel.");
                    return;
            }

            EnterActiveListening("AKTYWNY · każde polecenie powiedz z „Sentinel”");
            if (command.Length > 0) Publish(CommandRecognized, command);
        });
    }

    private void EnterWakeWordStandby(string status)
    {
        if (disposed || State == VoiceState.Off) return;
        capture.SetWakeOnlyMode(true); // Rotates the capture generation and cancels queued ASR work.
        State = VoiceState.Standby;
        activeUntil = default;
        SetLifecycle(VoiceStateKind.STANDBY, "Tylko lokalne wykrywanie słowa Sentinel jest aktywne.");
        SetStatus(status);
    }

    private void EnterActiveListening(string status)
    {
        capture.SetWakeOnlyMode(false);
        State = VoiceState.Active;
        activeUntil = DateTime.Now.AddMinutes(settings.Current.Voice.ConversationTimeoutMinutes);
        SetLifecycle(VoiceStateKind.LISTENING, "Aktywna sesja głosowa; każde polecenie wymaga słowa Sentinel.");
        SetStatus(status);
    }

    private void RecognitionProcessingChanged(bool processing)
    {
        if (disposed) return;
        if (processing)
        {
            if (State != VoiceState.Off && !engine.IsBusy && !speech.IsSpeaking)
                SetLifecycle(VoiceStateKind.TRANSCRIBING, "Lokalne rozpoznawanie wypowiedzi.");
            return;
        }

        if (Lifecycle.State != VoiceStateKind.TRANSCRIBING) return;
        SetLifecycle(State switch
        {
            VoiceState.Off => VoiceStateKind.OFF,
            VoiceState.Standby => VoiceStateKind.STANDBY,
            _ => VoiceStateKind.LISTENING
        }, State == VoiceState.Standby ? "Oczekuję lokalnie na słowo Sentinel." : "Gotowe na polecenie z wake-word.");
    }

    private void Tick(object? sender, EventArgs args)
    {
        if (!capture.IsListening)
        {
            if (State != VoiceState.Off && Lifecycle.State != VoiceStateKind.ERROR)
                CaptureError("Mikrofon przestał nasłuchiwać. Sprawdź urządzenie i uruchom ponownie mikrofon.");
            return;
        }
        if (State == VoiceState.Active && DateTime.Now > activeUntil && !engine.IsBusy && !speech.IsSpeaking)
        {
            EnterWakeWordStandby("STANDBY · sesja rozmowy wygasła; powiedz Sentinel, aby wznowić.");
        }
        Publish(MetricsUpdated, new VoiceMetrics(State, capture.CurrentRawAudioLevel * 100,
            capture.CurrentEnhancedRms * 100, capture.CurrentNoiseFloor, capture.CurrentSnrDb,
            capture.CurrentGain, capture.SpeechDetected, capture.LastTranscript));
    }

    public void Stop()
    {
        CancellationTokenSource? pendingStart;
        lock (lifecycleGate)
        {
            if (disposed) return;
            Interlocked.Increment(ref generation);
            pendingStart = starting;
            starting = null;
            State = VoiceState.Off;
            activeUntil = default;
            timer.Stop();
        }
        try { pendingStart?.Cancel(); }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { AppLog.Write("Voice", "Warning", "Could not cancel an in-flight voice startup cleanly.", ex); }
        try { capture.StopListening(); }
        catch (Exception ex) { AppLog.Write("Voice", "Warning", "Could not stop microphone capture cleanly.", ex); }
        try { capture.RecognitionSuppressed = false; }
        catch (Exception ex) { AppLog.Write("Voice", "Warning", "Could not clear voice recognition suppression while stopping.", ex); }
        try { speech.Stop(); }
        catch (Exception ex) { AppLog.Write("Voice", "Warning", "Could not stop speech output cleanly.", ex); }
        SetLifecycle(VoiceStateKind.OFF, "Mikrofon wyłączony.");
        SetStatus("Mikrofon wyłączony");
        Publish(MetricsUpdated, new VoiceMetrics(State, 0, 0, 0, 0, 0, false, ""));
    }

    private void CaptureError(string message) => dispatcher.Post(() =>
    {
        if (disposed) return;
        Status = message;
        if (!capture.IsListening)
        {
            CancellationTokenSource? pendingStart;
            lock (lifecycleGate)
            {
                Interlocked.Increment(ref generation);
                pendingStart = starting;
                starting = null;
                State = VoiceState.Off;
                activeUntil = default;
                timer.Stop();
            }
            try { pendingStart?.Cancel(); }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { AppLog.Write("Voice", "Warning", "Could not cancel voice startup after microphone failure.", ex); }
            try { capture.RecognitionSuppressed = false; } catch (Exception ex) { AppLog.Write("Voice", "Warning", "Could not restore recognition after microphone failure.", ex); }
            SetLifecycle(VoiceStateKind.ERROR, message);
            Publish(MetricsUpdated, new VoiceMetrics(State, 0, 0, 0, 0, 0, false, ""));
        }
        RaiseChanged();
    });

    public void Calibrate()
    {
        if (capture.IsListening)
        {
            try { capture.CalibrateNoise(); }
            catch (Exception ex) { CaptureError("Kalibracja mikrofonu nie powiodła się: " + ex.Message); }
        }
        else SetStatus("Najpierw włącz mikrofon.");
    }

    public void Speak(string text)
    {
        if (!settings.Current.Voice.SpeakResponses || engine.IsStopped || State == VoiceState.Off) return;
        try
        {
            capture.RecognitionSuppressed = true;
            SetLifecycle(VoiceStateKind.SPEAKING, "Odczytuję krótką odpowiedź.");
            speech.Speak(text, settings.Current.Voice.SpeechVoice,
                settings.Current.Voice.SpeechRate, settings.Current.Voice.SpeechVolume);
            // Synchronous SAPI-unavailable/empty-text paths may not emit an asynchronous completion.
            if (!speech.IsSpeaking) SpeechCompleted();
        }
        catch (Exception ex)
        {
            SpeechFailed("Nie udało się odczytać odpowiedzi: " + ex.Message);
            AppLog.Write("Voice", "Warning", "Speech output failed; microphone recognition was restored.", ex);
        }
    }

    private void SpeechCompleted()
    {
        try { capture.RecognitionSuppressed = false; }
        catch (Exception ex) { AppLog.Write("Voice", "Warning", "Could not restore microphone recognition after speech output.", ex); }
        RestoreListeningLifecycle("Odczyt głosowy zakończony.");
    }

    private void SpeechFailed(string message)
    {
        try { capture.RecognitionSuppressed = false; }
        catch (Exception ex) { AppLog.Write("Voice", "Warning", "Could not restore microphone recognition after speech failure.", ex); }
        SetStatus("Synteza głosu nie powiodła się; mikrofon wrócił do nasłuchu. " + message);
        RestoreListeningLifecycle("Synteza głosu zakończyła się błędem; nasłuch jest aktywny.");
    }

    private void RestoreListeningLifecycle(string detail)
    {
        if (State == VoiceState.Off) SetLifecycle(VoiceStateKind.OFF, "Mikrofon wyłączony.");
        else if (State == VoiceState.Standby) SetLifecycle(VoiceStateKind.STANDBY, "Tylko lokalne wykrywanie słowa Sentinel jest aktywne.");
        else if (engine.IsBusy) SetLifecycle(engine.IsStreaming ? VoiceStateKind.THINKING : VoiceStateKind.EXECUTING, "Trwa polecenie.");
        else SetLifecycle(VoiceStateKind.LISTENING, detail);
    }

    private void SetLifecycleIfCurrent(int expectedGeneration, VoiceStateKind state, string detail)
    {
        lock (lifecycleGate)
        {
            if (!disposed && expectedGeneration == generation)
                lifecycle.ForceTransition(state, detail);
        }
    }

    private void SetLifecycle(VoiceStateKind state, string detail)
    {
        if (!disposed) lifecycle.ForceTransition(state, detail);
    }

    private void LifecycleChanged(VoiceStateInfo _) => dispatcher.Post(() =>
    {
        if (!disposed) RaiseChanged();
    });

    private void RaiseChanged()
    {
        foreach (Action observer in Changed?.GetInvocationList() ?? [])
        {
            try { observer(); }
            catch (Exception ex) { AppLog.Write("Voice", "Warning", "A voice UI observer failed.", ex); }
        }
    }

    private static void Publish<T>(Action<T>? observers, T value)
    {
        foreach (Action<T> observer in observers?.GetInvocationList() ?? [])
        {
            try { observer(value); }
            catch (Exception ex) { AppLog.Write("Voice", "Warning", "A voice event observer failed.", ex); }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        Stop();
        disposed = true;
        timer.Tick -= Tick;
        capture.SpeechRecognized -= Recognized;
        capture.RecognitionProcessingChanged -= RecognitionProcessingChanged;
        capture.StatusChanged -= SetStatus;
        capture.ErrorOccurred -= CaptureError;
        settings.Changed -= ApplySettings;
        engine.Changed -= EngineChanged;
        speech.Completed -= SpeechCompleted;
        speech.Failed -= SpeechFailed;
        lifecycle.StateChanged -= LifecycleChanged;
    }
}
