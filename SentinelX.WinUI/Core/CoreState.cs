using CommunityToolkit.Mvvm.ComponentModel;
using SentinelX.Core;
using SentinelX.Models;
using SentinelX.Services.Actions;
using SentinelX.Services.Engine;
using SentinelX.Services.Link;
using SentinelX.Services.Voice;

namespace SentinelX.WinUI.Core;

/// <summary>
/// The nine states of the Sentinel Core. The core is not a graphic — it is part
/// of the application architecture: <see cref="CoreStateService"/> owns the current
/// state and the whole UI reacts to its changes automatically.
/// </summary>
public enum SentinelCoreState
{
    Idle,
    Listening,
    Recognizing,
    Thinking,
    Executing,
    Success,
    Warning,
    Error,
    Offline
}

public sealed record CoreStateChanged(SentinelCoreState State, string Detail, string ActiveTask);

public interface ICoreStateService
{
    SentinelCoreState State { get; }
    string Detail { get; }
    string ActiveTask { get; }
    /// <summary>Live microphone energy 0..1, for the Listening animation. 0 when the mic is off.</summary>
    double AudioLevel { get; }
    event Action<CoreStateChanged>? Changed;
}

/// <summary>
/// Central state of Sentinel. It derives the base state from the real engine / voice /
/// AI state and layers short-lived overlays on top (recognition blips, results,
/// watch alerts). Everything is published on the UI thread.
/// </summary>
public sealed partial class CoreStateService : ObservableObject, ICoreStateService, IDisposable
{
    private readonly IActionEngine engine;
    private readonly IVoiceService voice;
    private readonly IEngineService ai;
    private readonly AlertFeed alerts;
    private readonly IUiDispatcher dispatcher;
    private readonly object gate = new();
    private CancellationTokenSource? overlay;
    private bool disposed;

    [ObservableProperty] private SentinelCoreState state = SentinelCoreState.Idle;
    [ObservableProperty] private string detail = "Gotowy na polecenie";
    [ObservableProperty] private string activeTask = "";
    [ObservableProperty] private double audioLevel;

    public event Action<CoreStateChanged>? Changed;

    public CoreStateService(IActionEngine engine, IVoiceService voice, IEngineService ai,
        AlertFeed alerts, IUiDispatcher dispatcher)
    {
        this.engine = engine;
        this.voice = voice;
        this.ai = ai;
        this.alerts = alerts;
        this.dispatcher = dispatcher;
        engine.Changed += Recompute;
        engine.ActionStarted += OnActionStarted;
        voice.Changed += Recompute;
        voice.MetricsUpdated += OnMetrics;
        ai.Changed += Recompute;
        alerts.Added += OnAlert;
        Recompute();
    }

    private void OnActionStarted(ActionRecord record) => Recompute();

    private void OnAlert(LinkAlert alert)
    {
        if (alert.Level == "error") SetOverlay(SentinelCoreState.Error, Clip(alert.Title, 140), TimeSpan.FromSeconds(5));
        else if (alert.Level == "warn") SetOverlay(SentinelCoreState.Warning, Clip(alert.Title, 140), TimeSpan.FromSeconds(5));
    }

    private void OnMetrics(VoiceMetrics metrics)
    {
        double level = Math.Clamp(metrics.Enhanced / 100d, 0, 1);
        if (metrics.SpeechDetected)
            SetOverlay(SentinelCoreState.Recognizing, Clip(metrics.Transcript, 140), TimeSpan.FromMilliseconds(1200));
        dispatcher.Post(() =>
        {
            if (disposed) return;
            // Throttle: tiny level noise would repaint the core 10 times a second.
            if (Math.Abs(level - AudioLevel) > 0.015) AudioLevel = level;
        });
    }

    /// <summary>Called by the command path when <see cref="IActionEngine.ExecuteAsync"/> finishes.</summary>
    public void ReportResult(ActionRecord? action)
    {
        if (disposed) return;
        if (action == null)
        {
            SetOverlay(SentinelCoreState.Success, "Gotowe", TimeSpan.FromSeconds(2));
            return;
        }

        switch (action.Status)
        {
            case ActionStatus.Failed:
                SetOverlay(SentinelCoreState.Error,
                    action.Error.Length > 0 ? Clip(action.Error, 140) : "Zadanie nie powiodło się",
                    TimeSpan.FromSeconds(4));
                break;
            case ActionStatus.Cancelled:
                SetOverlay(SentinelCoreState.Warning, "Zadanie anulowane", TimeSpan.FromSeconds(3));
                break;
            case ActionStatus.Unverified:
                SetOverlay(SentinelCoreState.Warning, "Wynik bez pełnej weryfikacji — sprawdź szczegóły", TimeSpan.FromSeconds(3.5));
                break;
            default:
                SetOverlay(SentinelCoreState.Success, "Gotowe", TimeSpan.FromSeconds(2.5));
                break;
        }
    }

    private void Recompute() => dispatcher.Post(() =>
    {
        if (disposed) return;
        var computed = ComputeBase();
        lock (gate)
        {
            // A live base state always wins over a fading overlay.
            if (computed.State is SentinelCoreState.Offline or SentinelCoreState.Thinking or SentinelCoreState.Executing)
            {
                overlay?.Cancel();
                overlay = null;
            }
            else if (overlay != null)
            {
                return;
            }
        }

        Publish(computed.State, computed.Detail, computed.Task);
    });

    private (SentinelCoreState State, string Detail, string Task) ComputeBase()
    {
        if (engine.IsStopped)
            return (SentinelCoreState.Offline, "STOP awaryjny — nowe akcje zablokowane", "");

        if (engine.IsBusy)
        {
            var current = engine.CurrentAction;
            string task = current == null
                ? "Przetwarzanie…"
                : (current.Phase.Length > 0 ? current.Phase : "Wykonywanie") + " · " + current.ActionType;
            return engine.IsStreaming
                ? (SentinelCoreState.Thinking, "Model układa odpowiedź…", task)
                : (SentinelCoreState.Executing, "Wykonuje: " + Clip(task, 120), task);
        }

        if (voice.State != VoiceState.Off)
            return (SentinelCoreState.Listening,
                voice.State == VoiceState.Active
                    ? "Słucha — każde polecenie powiedz z „Sentinel”"
                    : "Czuwa na słowo „Sentinel”", "");

        EngineStatus status = ai.Status;
        if (status.State == "error")
            return (SentinelCoreState.Warning, "Silnik AI czeka — naprawi się sam", "");
        if (status.State == "installing")
            return (SentinelCoreState.Idle, $"Silnik AI pobiera się w tle ({status.Progress * 100:F0}%) — polecenia działają już teraz", "");
        if (status.State == "paused")
            return (SentinelCoreState.Idle, "Silnik AI śpi, bo trwa gra — polecenia systemowe działają", "");

        return (SentinelCoreState.Idle, "Gotowy na polecenie", "");
    }

    private void SetOverlay(SentinelCoreState state, string detail, TimeSpan hold)
    {
        CancellationTokenSource source;
        lock (gate)
        {
            if (disposed) return;
            overlay?.Cancel();
            source = new CancellationTokenSource();
            overlay = source;
        }

        dispatcher.Post(() =>
        {
            if (disposed) return;
            // Never cover a live working state with a stale blip.
            var live = ComputeBase();
            if (live.State is SentinelCoreState.Offline or SentinelCoreState.Thinking or SentinelCoreState.Executing)
            {
                Publish(live.State, live.Detail, live.Task);
                return;
            }

            Publish(state, detail.Length > 0 ? detail : live.Detail, live.Task);
        });

        _ = Task.Delay(hold, source.Token).ContinueWith(_ =>
        {
            lock (gate)
            {
                if (!ReferenceEquals(overlay, source)) return;
                overlay = null;
            }

            Recompute();
        }, TaskScheduler.Default);
    }

    private void Publish(SentinelCoreState state, string detail, string task)
    {
        bool changed = state != State || detail != Detail || task != ActiveTask;
        State = state;
        Detail = detail;
        ActiveTask = task;
        if (changed)
        {
            try
            {
                Changed?.Invoke(new CoreStateChanged(state, detail, task));
            }
            catch (Exception ex)
            {
                AppLog.Write(ex);
            }
        }
    }

    private static string Clip(string value, int max) =>
        value.Length <= max ? value : value[..max].TrimEnd() + "…";

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lock (gate)
        {
            overlay?.Cancel();
            overlay = null;
        }

        engine.Changed -= Recompute;
        engine.ActionStarted -= OnActionStarted;
        voice.Changed -= Recompute;
        voice.MetricsUpdated -= OnMetrics;
        ai.Changed -= Recompute;
        alerts.Added -= OnAlert;
    }
}
