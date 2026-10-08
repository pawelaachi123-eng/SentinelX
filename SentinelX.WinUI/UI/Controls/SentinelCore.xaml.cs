using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SentinelX.WinUI.Core;
using Windows.UI;

namespace SentinelX.WinUI.UI.Controls;

/// <summary>
/// Layered animated Sentinel Core: orbits, segments, glow and a microphone-reactive
/// center. Colors and motion follow <see cref="SentinelCoreState"/>; all animation is
/// transform / opacity based (GPU) and pauses when disabled.
/// </summary>
public sealed partial class SentinelCore : UserControl
{
    public static readonly DependencyProperty StateProperty =
        DependencyProperty.Register(nameof(State), typeof(SentinelCoreState), typeof(SentinelCore),
            new PropertyMetadata(SentinelCoreState.Idle, OnStateChanged));

    public static readonly DependencyProperty AudioLevelProperty =
        DependencyProperty.Register(nameof(AudioLevel), typeof(double), typeof(SentinelCore),
            new PropertyMetadata(0d, OnAudioLevelChanged));

    public static readonly DependencyProperty AnimationsEnabledProperty =
        DependencyProperty.Register(nameof(AnimationsEnabled), typeof(bool), typeof(SentinelCore),
            new PropertyMetadata(true, OnAnimationsEnabledChanged));

    private sealed record Palette(Color Main, Color Soft, Color Dim, Color Dot, Color Center);

    private static readonly Dictionary<SentinelCoreState, Palette> Palettes = new()
    {
        [SentinelCoreState.Idle] = new(C(0x00, 0xD4, 0xFF), C(0x5E, 0xEA, 0xD4), C(0x15, 0x5E, 0x75), C(0xE8, 0xFB, 0xFF), C(0xF2, 0xFD, 0xFF)),
        [SentinelCoreState.Listening] = new(C(0x5E, 0xEA, 0xD4), C(0x7D, 0xE8, 0xFF), C(0x0E, 0x6E, 0x8A), C(0xE8, 0xFB, 0xFF), C(0xF2, 0xFD, 0xFF)),
        [SentinelCoreState.Recognizing] = new(C(0x7D, 0xE8, 0xFF), C(0x5E, 0xEA, 0xD4), C(0x0E, 0x6E, 0x8A), C(0xFF, 0xFF, 0xFF), C(0xF2, 0xFD, 0xFF)),
        [SentinelCoreState.Thinking] = new(C(0x38, 0xBD, 0xF8), C(0x7D, 0xE8, 0xFF), C(0x0C, 0x4A, 0x6E), C(0xE8, 0xFB, 0xFF), C(0xF2, 0xFD, 0xFF)),
        [SentinelCoreState.Executing] = new(C(0x00, 0xD4, 0xFF), C(0x5E, 0xEA, 0xD4), C(0x15, 0x5E, 0x75), C(0xE8, 0xFB, 0xFF), C(0xF2, 0xFD, 0xFF)),
        [SentinelCoreState.Success] = new(C(0x34, 0xD3, 0x99), C(0xA7, 0xF3, 0xD0), C(0x06, 0x5F, 0x46), C(0xEC, 0xFD, 0xF5), C(0xF2, 0xFD, 0xFF)),
        [SentinelCoreState.Warning] = new(C(0xF5, 0xA5, 0x24), C(0xFC, 0xD3, 0x4D), C(0x92, 0x40, 0x0E), C(0xFF, 0xFB, 0xEB), C(0xFF, 0xFB, 0xEB)),
        [SentinelCoreState.Error] = new(C(0xF0, 0x66, 0x5E), C(0xFC, 0xA5, 0xA5), C(0x7F, 0x1D, 0x1D), C(0xFE, 0xF2, 0xF2), C(0xFE, 0xF2, 0xF2)),
        [SentinelCoreState.Offline] = new(C(0x6B, 0x7A, 0x99), C(0x9A, 0xA8, 0xC3), C(0x2E, 0x3A, 0x52), C(0x9A, 0xA8, 0xC3), C(0x8A, 0x97, 0xB5))
    };

    private static Color C(byte r, byte g, byte b) => Color.FromArgb(0xFF, r, g, b);

    private bool started;
    private bool paused;

    public SentinelCoreState State
    {
        get => (SentinelCoreState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public double AudioLevel
    {
        get => (double)GetValue(AudioLevelProperty);
        set => SetValue(AudioLevelProperty, value);
    }

    public bool AnimationsEnabled
    {
        get => (bool)GetValue(AnimationsEnabledProperty);
        set => SetValue(AnimationsEnabledProperty, value);
    }

    public SentinelCore()
    {
        InitializeComponent();
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
    }

    private static void OnStateChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is SentinelCore core) core.ApplyState((SentinelCoreState)e.NewValue);
    }

    private static void OnAudioLevelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is SentinelCore core) core.ApplyMic((double)e.NewValue);
    }

    private static void OnAnimationsEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is SentinelCore core) core.ApplyEnabled((bool)e.NewValue);
    }

    private void Start()
    {
        if (started) return;
        started = true;
        OuterSpin.Begin();
        TicksSpin.Begin();
        MidSpin.Begin();
        InnerSpin.Begin();
        SatelliteSpin.Begin();
        Breathe.Begin();
        ApplyState(State);
        ApplyEnabled(AnimationsEnabled);
    }

    private void Stop()
    {
        if (!started) return;
        started = false;
        OuterSpin.Stop();
        TicksSpin.Stop();
        MidSpin.Stop();
        InnerSpin.Stop();
        SatelliteSpin.Stop();
        ProgressSpin.Stop();
        Breathe.Stop();
        DotPulse.Stop();
        Flash.Stop();
    }

    private void ApplyEnabled(bool enabled)
    {
        if (!started) return;
        if (enabled && paused)
        {
            paused = false;
            OuterSpin.Resume();
            TicksSpin.Resume();
            MidSpin.Resume();
            InnerSpin.Resume();
            SatelliteSpin.Resume();
            Breathe.Resume();
            if (State == SentinelCoreState.Recognizing) DotPulse.Resume();
            if (State == SentinelCoreState.Executing) ProgressSpin.Resume();
        }
        else if (!enabled && !paused)
        {
            paused = true;
            OuterSpin.Pause();
            TicksSpin.Pause();
            MidSpin.Pause();
            InnerSpin.Pause();
            SatelliteSpin.Pause();
            Breathe.Pause();
            DotPulse.Pause();
            ProgressSpin.Pause();
            Flash.Pause();
        }
    }

    private void ApplyState(SentinelCoreState state)
    {
        Palette palette = Palettes[state];
        RingBrushOuter.Color = palette.Dim;
        RingBrushTicks.Color = palette.Main;
        RingBrushMid.Color = palette.Soft;
        RingBrushInner.Color = palette.Main;
        ProgressBrush.Color = palette.Soft;
        SatBrush.Color = palette.Soft;
        DotBrush.Color = palette.Dot;
        CoreStop0.Color = palette.Center;
        CoreStop1.Color = palette.Main;
        GlowStop0.Color = Color.FromArgb(0x4D, palette.Main.R, palette.Main.G, palette.Main.B);
        FlashBrush.Color = palette.Main;

        if (!started) return;

        // Speed ratios keep the motion fluid — rings never jump when the state changes.
        double outer = 1, ticks = 1, mid = 1, inner = 1, breathe = 1, glow = 0.4, root = 1;
        bool dotPulse = false, progress = false;
        switch (state)
        {
            case SentinelCoreState.Listening:
                ticks = 1.6; inner = 1.4; glow = 0.55;
                break;
            case SentinelCoreState.Recognizing:
                ticks = 2; inner = 3.5; breathe = 1.6; glow = 0.7;
                dotPulse = true;
                break;
            case SentinelCoreState.Thinking:
                mid = 2.6; inner = 5; ticks = 2; breathe = 2; glow = 0.65;
                break;
            case SentinelCoreState.Executing:
                mid = 1.8; inner = 2.4; glow = 0.6;
                progress = true;
                break;
            case SentinelCoreState.Success:
                inner = 1.2; glow = 0.55;
                if (!paused) Flash.Begin();
                break;
            case SentinelCoreState.Warning:
                outer = 0.6; ticks = 0.6; mid = 0.6; inner = 0.6; breathe = 0.7; glow = 0.5;
                break;
            case SentinelCoreState.Error:
                // Calm down, no harsh blinking: frozen rings, slow deep breathing.
                SetSpinSpeed(0.0001);
                breathe = 0.55; glow = 0.45;
                Breathe.SpeedRatio = breathe;
                Glow.Opacity = glow;
                Root.Opacity = 1;
                SetPulse(dotPulse: false, progress: false);
                return;
            case SentinelCoreState.Offline:
                SetSpinSpeed(0.0001);
                root = 0.45; glow = 0.25;
                Root.Opacity = root;
                Glow.Opacity = glow;
                SetPulse(dotPulse: false, progress: false);
                return;
        }

        OuterSpin.SpeedRatio = outer;
        TicksSpin.SpeedRatio = ticks;
        MidSpin.SpeedRatio = mid;
        InnerSpin.SpeedRatio = inner;
        SatelliteSpin.SpeedRatio = state == SentinelCoreState.Warning ? 0.6 : 1;
        Breathe.SpeedRatio = breathe;
        Glow.Opacity = glow;
        Root.Opacity = root;
        SetPulse(dotPulse, progress);
        ApplyMic(AudioLevel);
    }

    private void SetSpinSpeed(double speed)
    {
        OuterSpin.SpeedRatio = speed;
        TicksSpin.SpeedRatio = speed;
        MidSpin.SpeedRatio = speed;
        InnerSpin.SpeedRatio = speed;
        SatelliteSpin.SpeedRatio = speed;
    }

    private void SetPulse(bool dotPulse, bool progress)
    {
        try
        {
            if (dotPulse) DotPulse.Begin(); else DotPulse.Stop();
        }
        catch
        {
        }

        try
        {
            if (progress)
            {
                ProgressWrapper.Opacity = 1;
                ProgressSpin.Begin();
            }
            else
            {
                ProgressSpin.Stop();
                ProgressWrapper.Opacity = 0;
            }
        }
        catch
        {
        }
    }

    private void ApplyMic(double level)
    {
        if (!started) return;
        double clamped = Math.Clamp(level, 0, 1);
        double factor = State is SentinelCoreState.Listening or SentinelCoreState.Recognizing ? 0.45 : 0.12;
        double scale = 1 + clamped * factor;
        DotScale.ScaleX = scale;
        DotScale.ScaleY = scale;
    }
}
