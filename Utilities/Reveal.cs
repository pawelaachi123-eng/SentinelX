using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SentinelX.Utilities;

/// <summary>
/// 0.93 · NOWOCZESNE GUI: poświata podążająca za kursorem po karcie (Fluent reveal).
/// Czysto widokowe zachowanie — bez serwisów, bez ViewModeli, bez stanu globalnego.
/// Włącza się atrybutem <c>u:Reveal.Enabled="True"</c> na Border ze stylem karty.
/// Kolory pochodzą z tokenów SxReveal / SxTransparent, więc motyw działa jak wszędzie.
/// </summary>
public static class Reveal
{
    private const double Radius = 190;
    private const double TargetOpacity = 0.85;

    /// <summary>Warstwa poświaty trzymana jako właściwość dołączona karty. Celowo nie
    /// <c>Name</c> + <c>FindName</c>: element tworzony w kodzie nie rejestruje się w namescope
    /// strony, więc <c>FindName</c> zwracałoby null, a warstwa byłaby dokładana przy każdym
    /// ruchu myszy — karta zawijałaby się w kolejne siatki bez końca.</summary>
    private static readonly DependencyProperty LayerProperty = DependencyProperty.RegisterAttached(
        "Layer", typeof(Border), typeof(Reveal), new PropertyMetadata(null));
    private static Border? GetLayer(Border card) => (Border?)card.GetValue(LayerProperty);
    private static void SetLayer(Border card, Border? layer) => card.SetValue(LayerProperty, layer);

    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(Reveal), new PropertyMetadata(false, OnEnabledChanged));
    public static void SetEnabled(DependencyObject d, bool value) => d.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border card) return;
        card.MouseMove -= OnMouseMove;
        card.MouseLeave -= OnMouseLeave;
        card.Loaded -= OnLoaded;
        if (e.NewValue is not true) return;
        card.Loaded += OnLoaded;
        card.MouseMove += OnMouseMove;
        card.MouseLeave += OnMouseLeave;
    }

    private static void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is Border card) EnsureLayer(card);
    }

    /// <summary>
    /// Dokłada warstwę poświaty do karty: dotychczasowa zawartość trafia do siatki,
    /// a na niej leży Border z radialnym gradientem, który nie przechwytuje myszy.
    /// </summary>
    private static void EnsureLayer(Border card)
    {
        if (GetLayer(card) is Border current && card.Child is Grid existing && existing.Children.Contains(current)) return;

        var reveal = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            RadiusX = Radius,
            RadiusY = Radius,
            Center = new Point(-Radius, -Radius),
            GradientOrigin = new Point(-Radius, -Radius)
        };
        reveal.GradientStops.Add(new GradientStop(RevealColor(), 0));
        reveal.GradientStops.Add(new GradientStop(Colors.Transparent, 1));

        var layer = new Border
        {
            Background = reveal,
            CornerRadius = card.CornerRadius,
            IsHitTestVisible = false,
            Opacity = 0
        };

        var grid = new Grid();
        UIElement? content = card.Child;
        card.Child = null;
        if (content != null) grid.Children.Add(content);
        grid.Children.Add(layer);
        card.Child = grid;
        card.ClipToBounds = true;
        SetLayer(card, layer);
    }

    private static void OnMouseMove(object sender, MouseEventArgs args)
    {
        if (sender is not Border card) return;
        EnsureLayer(card);
        if (GetLayer(card) is not Border layer) return;
        if (layer.Background is not RadialGradientBrush brush) return;

        // Gradient jest w pikselach karty (MappingMode=Absolute): centrum musi iść za kursorem.
        Point position = args.GetPosition(card);
        brush.Center = position;
        brush.GradientOrigin = position;
        if (layer.Opacity < TargetOpacity)
            layer.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(TargetOpacity, TimeSpan.FromMilliseconds(180)), HandoffBehavior.SnapshotAndReplace);
    }

    private static void OnMouseLeave(object sender, MouseEventArgs args)
    {
        if (sender is not Border card) return;
        if (GetLayer(card) is not Border layer) return;
        layer.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(320)), HandoffBehavior.SnapshotAndReplace);
    }

    private static Color RevealColor()
    {
        if (Application.Current?.TryFindResource("SxRevealColor") is Color token) return token;
        return Color.FromArgb(46, 34, 211, 238);
    }
}
