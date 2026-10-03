using System.IO;
using SentinelX;
using SentinelX.Core;
using SentinelX.Models;
using SentinelX.Services.Actions;
using SentinelX.Services.Settings;
using SentinelX.Services.Voice;

namespace SentinelX.Tests;

/// <summary>Voice state, wake-word-only controls, cancellation, device errors and TTS cleanup use local fakes.</summary>
internal static class VoiceRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("VoiceRegression: " + message);
    }

    private sealed class InlineDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();
    }

    private sealed class FakeCapture : IVoiceCapture
    {
        public bool HasLocalModels => true;
        public bool IsListening { get; private set; }
        public bool IsWakeOnlyMode { get; private set; }
        public bool RecognitionSuppressed { get; set; }
        public float CurrentRawAudioLevel => 0.1f;
        public float CurrentEnhancedRms => 0.08f;
        public float CurrentNoiseFloor => 0.01f;
        public float CurrentSnrDb => 18;
        public float CurrentGain => 1;
        public bool SpeechDetected => false;
        public string LastTranscript => "";
        public int InitializeCalls { get; private set; }
        public IReadOnlyList<string> Microphones { get; set; } = ["Test microphone"];
        public Exception? InitializeFailure { get; set; }
        public Exception? StartFailure { get; set; }
        public TaskCompletionSource<bool> InitializeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool>? InitializeBlock { get; set; }
        public event Action<string>? SpeechRecognized;
        public event Action<bool>? RecognitionProcessingChanged;
        public event Action<string>? StatusChanged;
        public event Action<string>? ErrorOccurred;

        public IReadOnlyList<string> GetMicrophones() => Microphones;

        public async Task InitializeAsync(bool allowModelDownloads, CancellationToken cancellationToken = default)
        {
            InitializeCalls++;
            InitializeStarted.TrySetResult(true);
            if (InitializeBlock is { } block) await block.Task.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (InitializeFailure != null) throw InitializeFailure;
        }

        public void SetWakeOnlyMode(bool enabled) => IsWakeOnlyMode = enabled;

        public void StartListening(int deviceNumber = 0)
        {
            if (StartFailure != null) throw StartFailure;
            if (deviceNumber != 0) throw new ArgumentOutOfRangeException(nameof(deviceNumber));
            IsListening = true;
        }

        public void StopListening() => IsListening = false;
        public void ApplySettings() { }
        public void CalibrateNoise() { }
        public void EmitTranscript(string text) => SpeechRecognized?.Invoke(text);
        public void EmitProcessing(bool active) => RecognitionProcessingChanged?.Invoke(active);
        public void EmitStatus(string status) => StatusChanged?.Invoke(status);
        public void EmitDeviceFailure(string message)
        {
            IsListening = false;
            ErrorOccurred?.Invoke(message);
        }
    }

    private sealed class FakeSpeechOutput : ISpeechOutput
    {
        public bool IsSpeaking { get; private set; }
        public int SpeakCalls { get; private set; }
        public event Action? Completed;
        public event Action<string>? Failed;

        public void Speak(string text, string voice, int rate, int volume)
        {
            SpeakCalls++;
            IsSpeaking = true;
        }

        public void Stop()
        {
            IsSpeaking = false;
            Completed?.Invoke();
        }

        public void Complete()
        {
            IsSpeaking = false;
            Completed?.Invoke();
        }

        public void Fail(string message)
        {
            IsSpeaking = false;
            Failed?.Invoke(message);
        }
    }

    private sealed class FakeEngine : IActionEngine
    {
        public bool IsStopped { get; private set; }
        public bool IsBusy { get; private set; }
        public ActionRecord? CurrentAction => null;
        public bool HasPendingPermission => false;
        public bool IsStreaming { get; private set; }
        public event Action<string>? StreamDelta { add { } remove { } }
        public string PermissionSummary => "";
        public event Action? Changed;
        public event Action<ActionRecord>? ActionStarted { add { } remove { } }
        public Task<IntentResult> ExecuteAsync(string input, CancellationToken token = default, bool fromVoice = false, Action<string>? onDelta = null) =>
            Task.FromResult(new IntentResult("test"));
        public void Cancel() { IsBusy = false; IsStreaming = false; Changed?.Invoke(); }
        public void EmergencyStop() { IsStopped = true; IsBusy = false; Changed?.Invoke(); }
        public void Resume() { IsStopped = false; Changed?.Invoke(); }
        public void SetBusy(bool busy, bool streaming = false) { IsBusy = busy; IsStreaming = streaming; Changed?.Invoke(); }
    }

    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);

        var machine = new VoiceStateMachine();
        Check(machine.Current.State == VoiceStateKind.OFF && !machine.Current.MicrophoneOpen, "initial OFF state must report a closed microphone");
        bool survivingObserverCalled = false;
        bool failStateObserverOnce = true;
        machine.StateChanged += _ =>
        {
            if (!failStateObserverOnce) return;
            failStateObserverOnce = false;
            throw new InvalidOperationException("Intentional state observer fault.");
        };
        machine.StateChanged += _ => survivingObserverCalled = true;
        machine.TransitionTo(VoiceStateKind.STANDBY, "wake-word capture enabled");
        Check(machine.Current.MicrophoneOpen && machine.IsMicrophoneOpen, "STANDBY keeps the mic open for local wake-word detection");
        Check(survivingObserverCalled, "a failing state observer cannot block later observers");
        bool illegalRejected = false;
        try { machine.TransitionTo(VoiceStateKind.SPEAKING, "illegal graph edge"); }
        catch (VoiceStateTransitionException) { illegalRejected = true; }
        Check(illegalRejected, "normal transitions must still enforce the state graph");
        machine.ForceTransition(VoiceStateKind.SPEAKING, "external speech state");
        Check(machine.Current.State == VoiceStateKind.SPEAKING && machine.Current.Busy, "forced reconciliation must not fail on an unreachable edge");
        machine.Reset("test reset");
        Check(machine.Current.State == VoiceStateKind.OFF && !machine.Current.MicrophoneOpen, "reset closes the lifecycle state");
        for (int i = 0; i < 80; i++) machine.ForceTransition(i % 2 == 0 ? VoiceStateKind.STANDBY : VoiceStateKind.LISTENING, "bounded history");
        Check(machine.HistoryLines(1000).Count == 64, "voice transition history remains bounded");
        Check(VoiceSessionCommandParser.Parse("Sentinel off") == VoiceSessionCommand.Off, "wake-word voice control recognizes off deterministically");
        Check(VoiceSessionCommandParser.Parse("Sentinel, włącz głos") == VoiceSessionCommand.On, "wake-word voice control recognizes Polish on");
        Check(VoiceSessionCommandParser.Parse("Sentinel, uruchom kalkulator") == VoiceSessionCommand.None, "ordinary app commands must not be mistaken for session controls");
        using (var capturePipeline = new VoiceRecognitionService())
        {
            int initialGeneration = capturePipeline.CaptureGeneration;
            capturePipeline.SetWakeOnlyMode(true);
            int standbyGeneration = capturePipeline.CaptureGeneration;
            capturePipeline.SetWakeOnlyMode(true);
            int unchangedGeneration = capturePipeline.CaptureGeneration;
            capturePipeline.SetWakeOnlyMode(false);
            int activeGeneration = capturePipeline.CaptureGeneration;
            Check(standbyGeneration > initialGeneration && unchangedGeneration == standbyGeneration && activeGeneration > standbyGeneration,
                "changing wake-only policy retires stale ASR work exactly once while preserving idempotent repeats");
        }

        var settings = new SettingsService(new AppSettingsService(Path.Combine(directory, "settings")));
        var capture = new FakeCapture();
        var output = new FakeSpeechOutput();
        var engine = new FakeEngine();
        using (var voice = new VoiceService(capture, settings, engine, new InlineDispatcher(), output))
        {
            int changed = 0;
            bool failUiObserverOnce = true;
            voice.Changed += () =>
            {
                if (!failUiObserverOnce) return;
                failUiObserverOnce = false;
                throw new InvalidOperationException("Intentional UI observer fault.");
            };
            voice.Changed += () => changed++;
            var commands = new List<string>();
            voice.CommandRecognized += commands.Add;

            await voice.StartAsync(0, downloadModels: false, CancellationToken.None);
            Check(capture.InitializeCalls == 1 && capture.IsListening && capture.IsWakeOnlyMode, "start uses local models and enters wake-only capture");
            Check(voice.State == VoiceState.Standby && voice.Lifecycle.State == VoiceStateKind.STANDBY, "successful start publishes explicit standby state");
            Check(voice.Lifecycle.MicrophoneOpen, "published standby state accurately reports the open wake-word microphone");
            capture.EmitStatus("test capture status");
            Check(voice.Status == "test capture status", "capture status events reach the voice service and UI");

            capture.EmitTranscript("rozmowa w tle");
            Check(commands.Count == 0 && voice.State == VoiceState.Standby, "speech without a whole-word wake phrase never reaches command routing");
            capture.EmitTranscript("Sentinel on");
            Check(voice.State == VoiceState.Active && !capture.IsWakeOnlyMode && voice.Lifecycle.State == VoiceStateKind.LISTENING,
                "spoken on re-enters the active voice session without dispatching an AI command");
            Check(commands.Count == 0, "Sentinel on is a voice-session control, not an app command");

            voice.Speak("Krótka odpowiedź testowa.");
            Check(capture.RecognitionSuppressed && voice.Lifecycle.State == VoiceStateKind.SPEAKING && output.SpeakCalls == 1,
                "TTS suppresses microphone feedback and publishes SPEAKING");
            output.Fail("fake asynchronous synthesis failure");
            Check(!capture.RecognitionSuppressed && voice.State == VoiceState.Active && voice.Lifecycle.State == VoiceStateKind.LISTENING,
                "asynchronous TTS failure restores microphone recognition and listening state");

            capture.EmitTranscript("Sentinel off");
            Check(voice.State == VoiceState.Standby && capture.IsListening && capture.IsWakeOnlyMode && commands.Count == 0,
                "spoken off ends active conversation but preserves only local wake-word listening");
            Check(voice.Status.Contains("lokalnym trybie wake-word"), "off explains that the physical microphone remains in wake-only standby");

            capture.EmitTranscript("Sentinel, ile mam ramu?");
            Check(commands.SequenceEqual(["ile mam ramu"]) && voice.State == VoiceState.Active,
                "a normal wake-word utterance is stripped once and dispatched through the command event");
            capture.EmitProcessing(true);
            Check(voice.Lifecycle.State == VoiceStateKind.TRANSCRIBING, "ASR processing is reflected as an explicit lifecycle state");
            engine.SetBusy(true, streaming: true);
            Check(voice.Lifecycle.State == VoiceStateKind.THINKING, "active engine streaming is reflected as THINKING");
            engine.SetBusy(false);
            Check(voice.Lifecycle.State == VoiceStateKind.LISTENING, "engine completion restores voice listening state");
            capture.EmitProcessing(false);
            Check(changed > 0, "voice lifecycle updates reach subsequent observers even when one UI observer fails");

            voice.Stop();
            Check(voice.State == VoiceState.Off && !capture.IsListening && voice.Lifecycle.State == VoiceStateKind.OFF,
                "explicit microphone stop closes capture and resets lifecycle state");
            Check(!capture.RecognitionSuppressed, "stopping speech cannot leave recognition suppressed");
        }

        var modelFailureCapture = new FakeCapture { InitializeFailure = new InvalidOperationException("local ASR model unavailable") };
        using (var voice = new VoiceService(modelFailureCapture, settings, new FakeEngine(), new InlineDispatcher(), new FakeSpeechOutput()))
        {
            bool failed = false;
            try { await voice.StartAsync(0, downloadModels: false, CancellationToken.None); }
            catch (InvalidOperationException) { failed = true; }
            Check(failed && modelFailureCapture.InitializeCalls == 1 && voice.Lifecycle.State == VoiceStateKind.ERROR
                && !modelFailureCapture.IsListening,
                "local ASR initialization failure remains visible and leaves the microphone closed");
        }

        var missingDeviceCapture = new FakeCapture { Microphones = [] };
        using (var voice = new VoiceService(missingDeviceCapture, settings, new FakeEngine(), new InlineDispatcher(), new FakeSpeechOutput()))
        {
            bool rejected = false;
            try { await voice.StartAsync(0, downloadModels: true, CancellationToken.None); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && missingDeviceCapture.InitializeCalls == 0 && voice.Lifecycle.State == VoiceStateKind.ERROR,
                "missing microphone is rejected before a potentially large ASR model download");
        }

        var changingDeviceCapture = new FakeCapture { InitializeBlock = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using (var voice = new VoiceService(changingDeviceCapture, settings, new FakeEngine(), new InlineDispatcher(), new FakeSpeechOutput()))
        {
            Task starting = voice.StartAsync(0, downloadModels: false, CancellationToken.None);
            await changingDeviceCapture.InitializeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            changingDeviceCapture.Microphones = ["Replacement microphone"];
            changingDeviceCapture.InitializeBlock!.TrySetResult(true);
            bool rejected = false;
            try { await starting.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && !changingDeviceCapture.IsListening && voice.Lifecycle.State == VoiceStateKind.ERROR,
                "a changed microphone list is revalidated after model initialization and cannot open the wrong device");
        }

        var loopbackCapture = new FakeCapture { Microphones = ["Loopback [DŹWIĘK SYSTEMOWY]"] };
        using (var voice = new VoiceService(loopbackCapture, settings, new FakeEngine(), new InlineDispatcher(), new FakeSpeechOutput()))
        {
            bool rejected = false;
            try { await voice.StartAsync(0, downloadModels: false, CancellationToken.None); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && loopbackCapture.InitializeCalls == 0,
                "a system-loopback device is rejected before model initialization");
        }

        var startFailureCapture = new FakeCapture { StartFailure = new InvalidOperationException("device unavailable") };
        using (var voice = new VoiceService(startFailureCapture, settings, new FakeEngine(), new InlineDispatcher(), new FakeSpeechOutput()))
        {
            bool failed = false;
            try { await voice.StartAsync(0, downloadModels: false, CancellationToken.None); }
            catch (InvalidOperationException) { failed = true; }
            Check(failed && !startFailureCapture.IsListening && voice.State == VoiceState.Off && voice.Lifecycle.State == VoiceStateKind.ERROR,
                "device-open failure is visible, leaves capture closed, and enters ERROR");
            Check(voice.Status.Contains("device unavailable"), "device error detail is retained for the UI");
        }

        var deviceCapture = new FakeCapture();
        using (var voice = new VoiceService(deviceCapture, settings, new FakeEngine(), new InlineDispatcher(), new FakeSpeechOutput()))
        {
            await voice.StartAsync(0, downloadModels: false, CancellationToken.None);
            deviceCapture.EmitDeviceFailure("Mikrofon został odłączony.");
            Check(voice.State == VoiceState.Off && voice.Lifecycle.State == VoiceStateKind.ERROR && !voice.Lifecycle.MicrophoneOpen,
                "a runtime device loss closes capture and remains visible as ERROR instead of silently returning OFF");
            Check(voice.Status.Contains("odłączony"), "runtime device-loss detail reaches the status surface");
        }

        var cancellationCapture = new FakeCapture { InitializeBlock = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using (var voice = new VoiceService(cancellationCapture, settings, new FakeEngine(), new InlineDispatcher(), new FakeSpeechOutput()))
        {
            Task starting = voice.StartAsync(0, downloadModels: false, CancellationToken.None);
            await cancellationCapture.InitializeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            voice.Stop();
            bool cancelled = false;
            try { await starting.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && !cancellationCapture.IsListening && voice.State == VoiceState.Off && voice.Lifecycle.State == VoiceStateKind.OFF,
                "stop cancels in-flight model initialization and prevents a late microphone reopen");
        }

        File.WriteAllText(Path.Combine(directory, "voice-tests.txt"),
            "PASS: validated and force-reconciled state graph, bounded history, standby microphone truth, wake-word-only off/on, command gate, asynchronous TTS failure recovery, observable ASR/AI states, device failure, cancellation cleanup, observer isolation\n");
    }
}
