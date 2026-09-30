using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace SentinelX.Views.Controls;

/// <summary>
/// Lightweight vector particle core. WPF's retained drawing surface keeps the render work on the
/// compositor where available; its timer pauses when the window is inactive or minimized.
/// </summary>
public sealed class SentinelCoreControl : FrameworkElement
{
    private readonly record struct Particle(double X, double Y, double Z, double Size, double Seed);
    private readonly Particle[] particles = CreateParticles(220);
    private readonly DispatcherTimer timer = new(DispatcherPriority.Render);
    private Window? hostWindow;
    private DateTime lastFrame;
    private double phase;
    private Brush[] particleBrushes = [];
    private Brush? haloBrush;
    private Brush? nucleusBrush;
    private Pen? outerRingPen;
    private Pen? innerRingPen;

    public static readonly DependencyProperty CoreStateProperty = DependencyProperty.Register(
        nameof(CoreState), typeof(string), typeof(SentinelCoreControl), new FrameworkPropertyMetadata("IDLE", FrameworkPropertyMetadataOptions.AffectsRender, StateChanged));
    public static readonly DependencyProperty AudioLevelProperty = DependencyProperty.Register(
        nameof(AudioLevel), typeof(double), typeof(SentinelCoreControl), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AccentColorProperty = DependencyProperty.Register(
        nameof(AccentColor), typeof(Color), typeof(SentinelCoreControl), new FrameworkPropertyMetadata(Color.FromRgb(34, 211, 238), FrameworkPropertyMetadataOptions.AffectsRender, PaletteChanged));
    public static readonly DependencyProperty SecondaryColorProperty = DependencyProperty.Register(
        nameof(SecondaryColor), typeof(Color), typeof(SentinelCoreControl), new FrameworkPropertyMetadata(Color.FromRgb(167, 139, 250), FrameworkPropertyMetadataOptions.AffectsRender, PaletteChanged));
    public static readonly DependencyProperty ErrorColorProperty = DependencyProperty.Register(
        nameof(ErrorColor), typeof(Color), typeof(SentinelCoreControl), new FrameworkPropertyMetadata(Color.FromRgb(251, 113, 133), FrameworkPropertyMetadataOptions.AffectsRender, PaletteChanged));

    public string CoreState { get => (string)GetValue(CoreStateProperty); set => SetValue(CoreStateProperty, value); }
    public double AudioLevel { get => (double)GetValue(AudioLevelProperty); set => SetValue(AudioLevelProperty, value); }
    public Color AccentColor { get => (Color)GetValue(AccentColorProperty); set => SetValue(AccentColorProperty, value); }
    public Color SecondaryColor { get => (Color)GetValue(SecondaryColorProperty); set => SetValue(SecondaryColorProperty, value); }
    public Color ErrorColor { get => (Color)GetValue(ErrorColorProperty); set => SetValue(ErrorColorProperty, value); }

    public SentinelCoreControl()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
        timer.Tick += OnFrame;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        RebuildPalette();
    }

    private static void StateChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (SentinelCoreControl)sender;
        control.RebuildPalette();
        control.UpdateFrameRate();
        control.InvalidateVisual();
    }

    private static void PaletteChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (SentinelCoreControl)sender;
        control.RebuildPalette();
        control.InvalidateVisual();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        AttachWindow();
        UpdateFrameRate();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        timer.Stop();
        if (hostWindow != null)
        {
            hostWindow.Activated -= WindowActivityChanged;
            hostWindow.Deactivated -= WindowActivityChanged;
            hostWindow.StateChanged -= WindowStateChanged;
            hostWindow = null;
        }
    }

    private void AttachWindow()
    {
        var current = Window.GetWindow(this);
        if (ReferenceEquals(current, hostWindow)) return;
        if (hostWindow != null)
        {
            hostWindow.Activated -= WindowActivityChanged;
            hostWindow.Deactivated -= WindowActivityChanged;
            hostWindow.StateChanged -= WindowStateChanged;
        }
        hostWindow = current;
        if (hostWindow != null)
        {
            hostWindow.Activated += WindowActivityChanged;
            hostWindow.Deactivated += WindowActivityChanged;
            hostWindow.StateChanged += WindowStateChanged;
        }
    }

    private void WindowActivityChanged(object? sender, EventArgs args) => UpdateFrameRate();
    private void WindowStateChanged(object? sender, EventArgs args) => UpdateFrameRate();

    private bool MotionEnabled => SystemParameters.ClientAreaAnimation && Application.Current?.TryFindResource("SxAnimationsEnabled") is not false;
    private bool CanAnimate => IsLoaded && IsVisible && MotionEnabled && hostWindow != null && hostWindow.IsActive && hostWindow.WindowState != WindowState.Minimized;

    private void UpdateFrameRate()
    {
        if (!CanAnimate)
        {
            timer.Stop();
            InvalidateVisual();
            return;
        }
        timer.Interval = CoreState switch
        {
            "THINKING" or "WORKING" => TimeSpan.FromMilliseconds(40),
            "LISTENING" or "SPEAKING" or "ERROR" => TimeSpan.FromMilliseconds(55),
            _ => TimeSpan.FromMilliseconds(105)
        };
        lastFrame = DateTime.UtcNow;
        if (!timer.IsEnabled) timer.Start();
    }

    private void OnFrame(object? sender, EventArgs args)
    {
        if (!CanAnimate)
        {
            timer.Stop();
            InvalidateVisual();
            return;
        }
        DateTime now = DateTime.UtcNow;
        double elapsed = Math.Clamp((now - lastFrame).TotalSeconds, 0, 0.12);
        lastFrame = now;
        double speed = CoreState switch
        {
            "THINKING" => 1.25,
            "WORKING" => 1.65,
            "LISTENING" => 0.42 + Math.Clamp(AudioLevel, 0, 1) * 1.2,
            "SPEAKING" => 0.55 + Math.Clamp(AudioLevel, 0, 1) * 1.15,
            "ERROR" => 0.7,
            _ => 0.19
        };
        phase += elapsed * speed;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        Point center = new(width / 2, height / 2);
        double baseRadius = Math.Min(width, height) * 0.375;
        double audio = Math.Clamp(AudioLevel, 0, 1);
        double pulse = CoreState switch
        {
            "LISTENING" => Math.Sin(phase * 2.1) * 0.018 + audio * 0.08,
            "SPEAKING" => Math.Sin(phase * 4.4) * 0.035 + audio * 0.07,
            "THINKING" or "WORKING" => Math.Sin(phase * 1.5) * 0.025,
            "ERROR" => Math.Sin(phase * 3.1) * 0.02,
            _ => Math.Sin(phase * 1.15) * 0.018
        };
        double radius = baseRadius * (1 + pulse);
        if (haloBrush != null)
            drawingContext.DrawEllipse(haloBrush, null, center, radius * 1.55, radius * 1.55);
        if (outerRingPen != null)
        {
            double ringPulse = 1 + Math.Sin(phase * 0.8) * 0.012;
            drawingContext.DrawEllipse(null, outerRingPen, center, radius * 1.12 * ringPulse, radius * 1.12 * ringPulse);
            drawingContext.PushTransform(new RotateTransform(-phase * (CoreState == "WORKING" ? 28 : 10), center.X, center.Y));
            drawingContext.DrawEllipse(null, innerRingPen, center, radius * 0.94, radius * 0.42);
            drawingContext.Pop();
        }

        double yaw = phase * (CoreState is "THINKING" or "WORKING" ? 0.8 : 0.31);
        double pitch = Math.Sin(phase * 0.37) * 0.19;
        double cy = Math.Cos(yaw), sy = Math.Sin(yaw), cp = Math.Cos(pitch), sp = Math.Sin(pitch);
        for (int i = 0; i < particles.Length; i++)
        {
            Particle particle = particles[i];
            double x = particle.X * cy - particle.Z * sy;
            double z = particle.X * sy + particle.Z * cy;
            double y = particle.Y * cp - z * sp;
            z = particle.Y * sp + z * cp;
            double perspective = 2.7 / (3.5 - z * 0.72);
            double activityDrift = CoreState is "THINKING" or "WORKING"
                ? Math.Sin(phase * 1.8 + particle.Seed) * 0.025
                : Math.Sin(phase * 0.65 + particle.Seed) * 0.009;
            double px = center.X + x * radius * (perspective + activityDrift);
            double py = center.Y + y * radius * (perspective + activityDrift);
            double depth = Math.Clamp((z + 1) * 0.5, 0, 1);
            int alphaIndex = depth < 0.22 ? 0 : depth < 0.52 ? 1 : depth < 0.78 ? 2 : 3;
            double twinkle = 0.82 + (Math.Sin(phase * (0.7 + particle.Seed * 0.12) + particle.Seed) + 1) * 0.12;
            double pointSize = particle.Size * (0.64 + depth * 0.62) * twinkle * (1 + audio * 0.13);
            if (particleBrushes.Length > alphaIndex)
                drawingContext.DrawEllipse(particleBrushes[alphaIndex], null, new Point(px, py), pointSize, pointSize);
        }

        // A compact inner nucleus gives the particle shell a coherent center without a heavy bitmap effect.
        double coreRadius = radius * (0.17 + audio * 0.035);
        if (nucleusBrush != null)
            drawingContext.DrawEllipse(nucleusBrush, null, center, coreRadius, coreRadius);
    }

    private Color CurrentStateColor() => CoreState switch
    {
        "ERROR" => ErrorColor,
        "WORKING" => SecondaryColor,
        "THINKING" => AccentColor,
        _ => AccentColor
    };

    private void RebuildPalette()
    {
        Color color = CurrentStateColor();
        byte[] alpha = [22, 58, 112, 205];
        particleBrushes = alpha.Select(a => MakeBrush(Color.FromArgb(a, color.R, color.G, color.B))).ToArray();
        var halo = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.48, 0.43),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(34, color.R, color.G, color.B), 0));
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(13, color.R, color.G, color.B), 0.48));
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
        halo.Freeze();
        haloBrush = halo;
        var nucleus = new RadialGradientBrush(
            Color.FromArgb(120, color.R, color.G, color.B),
            Color.FromArgb(0, color.R, color.G, color.B));
        nucleus.Freeze();
        nucleusBrush = nucleus;
        outerRingPen = MakePen(Color.FromArgb(38, color.R, color.G, color.B), 1.1);
        innerRingPen = MakePen(Color.FromArgb(25, color.R, color.G, color.B), 0.8);
    }

    private static SolidColorBrush MakeBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen MakePen(Color color, double thickness)
    {
        var pen = new Pen(MakeBrush(color), thickness);
        pen.Freeze();
        return pen;
    }

    private static Particle[] CreateParticles(int count)
    {
        var result = new Particle[count];
        double goldenAngle = Math.PI * (3 - Math.Sqrt(5));
        for (int i = 0; i < count; i++)
        {
            double y = 1 - 2 * (i + 0.5) / count;
            double radial = Math.Sqrt(Math.Max(0, 1 - y * y));
            double theta = goldenAngle * i;
            double x = Math.Cos(theta) * radial;
            double z = Math.Sin(theta) * radial;
            double seed = (i * 0.6180339887498949) % 1;
            result[i] = new Particle(x, y, z, 0.85 + seed * 1.25, seed * Math.PI * 2);
        }
        return result;
    }
}
