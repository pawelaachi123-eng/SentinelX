using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SentinelX.Utilities;

/// <summary>
/// 0.94 · Ruch widoku: wejścia z przesunięciem, uniesienie na hover, puls stanu i pasmo połysku.
/// Wyłącznie zachowanie widoku — zero odwołań do serwisów i ViewModeli. Każda animacja jest
/// pomijana, gdy wyłączono animacje (Ustawienia → Wygląd, tryb gry) albo gdy Windows ma wyłączony
/// efekt animacji. Elementy poza ekranem nie animują się w pętli.
/// </summary>
public static class SxMotion
{
    public static readonly DependencyProperty EntranceProperty = DependencyProperty.RegisterAttached(
        "Entrance", typeof(bool), typeof(SxMotion), new PropertyMetadata(false, OnEntranceChanged));
    public static void SetEntrance(DependencyObject d, bool value) => d.SetValue(EntranceProperty, value);
    public static bool GetEntrance(DependencyObject d) => (bool)d.GetValue(EntranceProperty);

    public static readonly DependencyProperty EntranceIndexProperty = DependencyProperty.RegisterAttached(
        "EntranceIndex", typeof(int), typeof(SxMotion), new PropertyMetadata(0));
    public static void SetEntranceIndex(DependencyObject d, int value) => d.SetValue(EntranceIndexProperty, value);
    public static int GetEntranceIndex(DependencyObject d) => (int)d.GetValue(EntranceIndexProperty);

    public static readonly DependencyProperty HoverLiftProperty = DependencyProperty.RegisterAttached(
        "HoverLift", typeof(bool), typeof(SxMotion), new PropertyMetadata(false, OnHoverLiftChanged));
    public static void SetHoverLift(DependencyObject d, bool value) => d.SetValue(HoverLiftProperty, value);
    public static bool GetHoverLift(DependencyObject d) => (bool)d.GetValue(HoverLiftProperty);

    public static readonly DependencyProperty PulseProperty = DependencyProperty.RegisterAttached(
        "Pulse", typeof(bool), typeof(SxMotion), new PropertyMetadata(false, OnPulseChanged));
    public static void SetPulse(DependencyObject d, bool value) => d.SetValue(PulseProperty, value);
    public static bool GetPulse(DependencyObject d) => (bool)d.GetValue(PulseProperty);

    public static readonly DependencyProperty ShimmerProperty = DependencyProperty.RegisterAttached(
        "Shimmer", typeof(bool), typeof(SxMotion), new PropertyMetadata(false, OnShimmerChanged));
    public static void SetShimmer(DependencyObject d, bool value) => d.SetValue(ShimmerProperty, value);
    public static bool GetShimmer(DependencyObject d) => (bool)d.GetValue(ShimmerProperty);

    /// <summary>
    /// Wejście z rozłożeniem na raty: panel nada każdemu widocznemu dziecku EntranceIndex i włączy
    /// mu Entrance. Jeden atrybut na stronie = kaskadowe pojawianie sekcji, bez zmian w ViewModelu.
    /// </summary>
    public static readonly DependencyProperty StaggerProperty = DependencyProperty.RegisterAttached(
        "Stagger", typeof(bool), typeof(SxMotion), new PropertyMetadata(false, OnStaggerChanged));
    public static void SetStagger(DependencyObject d, bool value) => d.SetValue(StaggerProperty, value);
    public static bool GetStagger(DependencyObject d) => (bool)d.GetValue(StaggerProperty);

    private static void OnStaggerChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement view) return;
        view.Loaded -= StaggerLoaded;
        if (e.NewValue is true) view.Loaded += StaggerLoaded;
        if (e.NewValue is true && view.IsLoaded) ApplyStagger(view);
    }

    private static void StaggerLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement view) { view.Loaded -= StaggerLoaded; ApplyStagger(view); }
    }

    private static void ApplyStagger(FrameworkElement view)
    {
        if (view is not System.Windows.Controls.Panel panel || !MotionEnabled) return;
        var order = 0;
        foreach (System.Windows.UIElement child in panel.Children)
        {
            if (child is not FrameworkElement element || element.Visibility != Visibility.Visible) continue;
            element.SetValue(EntranceIndexProperty, order++);
            if (GetEntrance(element)) continue;
            element.SetValue(EntranceProperty, true);
        }
    }

    /// <summary>Animacje są wyłączone, gdy użytkownik tak ustawił, gdy Windows wyłączył efekty albo gdy gra jest na pierwszym planie.</summary>
    public static bool MotionEnabled => Application.Current.TryFindResource("SxAnimationsEnabled") is true
        && SystemParameters.ClientAreaAnimation;

    private static void OnEntranceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement view) return;
        view.Loaded -= EntranceLoaded;
        if (e.NewValue is true && view.IsLoaded) BeginEntrance(view);
        if (e.NewValue is true) view.Loaded += EntranceLoaded;
    }

    private static void EntranceLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement view) { view.Loaded -= EntranceLoaded; BeginEntrance(view); }
    }

    private static void BeginEntrance(FrameworkElement view)
    {
        if (!MotionEnabled) return;
        var rise = Number(view, "SxEntranceRise", 14);
        var time = TimeSpan.FromMilliseconds(Number(view, "SxMotionBase", 180));
        var start = TimeSpan.FromMilliseconds(Number(view, "SxMotionStagger", 35) * Math.Max(0, GetEntranceIndex(view)));
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        view.Opacity = 0;
        view.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, time) { BeginTime = start, EasingFunction = ease });
        if (Rise(view) is { } shift)
            shift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(rise, 0, time) { BeginTime = start, EasingFunction = ease });
    }

    private static void OnHoverLiftChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not UIElement element) return;
        element.MouseEnter -= LiftIn;
        element.MouseLeave -= LiftOut;
        if (e.NewValue is not true) return;
        element.MouseEnter += LiftIn;
        element.MouseLeave += LiftOut;
    }

    private static void LiftIn(object sender, MouseEventArgs args) => Lift((FrameworkElement)sender, -Number(sender, "SxLiftDistance", 2));
    private static void LiftOut(object sender, MouseEventArgs args) => Lift((FrameworkElement)sender, 0);

    private static void Lift(FrameworkElement view, double to)
    {
        var shift = Rise(view);
        if (shift == null) return;
        var time = TimeSpan.FromMilliseconds(MotionEnabled ? Number(view, "SxMotionFast", 110) : 0);
        shift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(to, time) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private static void OnPulseChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement view) return;
        view.IsVisibleChanged -= PulseVisibility;
        view.IsVisibleChanged += PulseVisibility;
        PlayPulse(view, (bool)e.NewValue);
    }

    private static void PulseVisibility(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement view) PlayPulse(view, GetPulse(view) && (e.NewValue is true));
    }

    private static void PlayPulse(FrameworkElement view, bool on)
    {
        if (on && MotionEnabled && view.IsVisible)
        {
            var cycle = TimeSpan.FromMilliseconds(900);
            view.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.45, cycle)
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
            return;
        }
        view.BeginAnimation(UIElement.OpacityProperty, null);
        view.Opacity = 1;
    }

    private static void OnShimmerChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement view) return;
        var shift = Rise(view);
        if (shift == null) return;
        if (e.NewValue is true && MotionEnabled && view.IsVisible)
            shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-360, 560, TimeSpan.FromMilliseconds(1900))
            { RepeatBehavior = RepeatBehavior.Forever });
        else
        {
            shift.BeginAnimation(TranslateTransform.XProperty, null);
            shift.X = 0;
        }
    }

    /// <summary>
    /// Ambientny dryf + „oddech" dla dekoracji tła (halo, aurora). Nigdy na tekście i nigdy na elemencie klikalnym.
    /// Ruch jest wolny (26 s) i mały — cel: ekran oddycha, a nie rozprasza.
    /// </summary>
    public static readonly DependencyProperty FloatProperty =
        DependencyProperty.RegisterAttached("Float", typeof(bool), typeof(SxMotion), new PropertyMetadata(false, OnFloatChanged));

    public static void SetFloat(DependencyObject target, bool value) => target.SetValue(FloatProperty, value);
    public static bool GetFloat(DependencyObject target) => (bool)target.GetValue(FloatProperty);

    private static void OnFloatChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement view) return;
        view.IsVisibleChanged -= FloatVisibility;
        view.IsVisibleChanged += FloatVisibility;
        PlayFloat(view, (bool)e.NewValue);
    }

    private static void FloatVisibility(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement view) PlayFloat(view, GetFloat(view) && (e.NewValue is true));
    }

    private static void PlayFloat(FrameworkElement view, bool on)
    {
        if (!on || !MotionEnabled || !view.IsVisible) return;
        var shift = Rise(view);
        if (shift == null) return;
        var cycle = TimeSpan.FromMilliseconds(26000);
        var drift = new DoubleAnimationUsingKeyFrames
        {
            Duration = cycle,
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        drift.KeyFrames.Add(new EasingDoubleKeyFrame(14) { KeyTime = TimeSpan.Zero });
        drift.KeyFrames.Add(new EasingDoubleKeyFrame(-12) { KeyTime = cycle });
        shift.BeginAnimation(TranslateTransform.YProperty, drift);
        view.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.72, 1.0, TimeSpan.FromMilliseconds(11000))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });
    }

    /// <summary>Zwraca istniejący TranslateTransform elementu albo go dokleja. Gdy ma inną transformację — nie mieszamy się.</summary>
    private static TranslateTransform? Rise(DependencyObject target)
    {
        if (target is not FrameworkElement view) return null;
        switch (view.RenderTransform)
        {
            case TranslateTransform translate: return translate;
            case null:
                var created = new TranslateTransform();
                view.RenderTransform = created;
                view.RenderTransformOrigin = new Point(0.5, 0.5);
                return created;
            default: return null;
        }
    }

    private static double Number(DependencyObject target, string key, double fallback)
        => Application.Current.TryFindResource(key) is double value ? value : fallback;
}
