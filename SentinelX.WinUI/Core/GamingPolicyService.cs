using CommunityToolkit.Mvvm.ComponentModel;
using SentinelX.Core;
using SentinelX.Services.Gaming;
using SentinelX.Services.Settings;

namespace SentinelX.WinUI.Core;

/// <summary>
/// Automatic Gaming Mode: while a known game runs, the shell reduces animations,
/// slows telemetry, quiets notifications and tells heavy UI live-updates to pause.
/// (The AI engine itself already sleeps during games — see EngineService.)
/// </summary>
public sealed partial class GamingPolicyService : ObservableObject, IDisposable
{
    private readonly IGamingService gaming;
    private readonly ISettingsService settings;
    private readonly IUiDispatcher dispatcher;
    private Timer? timer;
    private bool disposed;

    [ObservableProperty] private bool isGamingActive;
    [ObservableProperty] private string runningGame = "";
    [ObservableProperty] private bool animationsAllowed = true;

    /// <summary>Telemetry charts sample every Nth update while gaming.</summary>
    public int TelemetryDivisor => IsGamingActive ? 4 : 1;

    public bool SuppressNonCriticalToasts => IsGamingActive;

    public GamingPolicyService(IGamingService gaming, ISettingsService settings, IUiDispatcher dispatcher)
    {
        this.gaming = gaming;
        this.settings = settings;
        this.dispatcher = dispatcher;
        settings.Changed += ApplySettings;
        ApplySettings();
    }

    public void Start()
    {
        timer ??= new Timer(_ => Poll(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3));
        Poll();
    }

    private void ApplySettings() => dispatcher.Post(() =>
    {
        if (!disposed) AnimationsAllowed = settings.Current.Ui.AnimationsEnabled && !IsGamingActive;
    });

    private void Poll()
    {
        string game;
        try
        {
            game = gaming.GetRunningGame();
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            return;
        }

        dispatcher.Post(() =>
        {
            if (disposed) return;
            bool active = game.Length > 0;
            if (active == IsGamingActive && game == RunningGame) return;
            IsGamingActive = active;
            RunningGame = game;
            AnimationsAllowed = settings.Current.Ui.AnimationsEnabled && !active;
        });
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        timer?.Dispose();
        timer = null;
        settings.Changed -= ApplySettings;
    }
}
