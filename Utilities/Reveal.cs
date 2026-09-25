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
    private const string LayerName = "PART_RevealLayer";
    private const double Radius = 190;
    private const double TargetOpacity = 0.85;

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
        if (card.Child is Grid existing && existing.FindName(LayerName) != null) return;

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
            Name = LayerName,
            Background = reveal,
            CornerRadius = card.CornerRadius,
            IsHitTestVisible = false,
            Opacity = 0
        };

        var grid = new Grid();
        UIElement? content = card.Child;
        card.Child = null;
        if (content != null) grid.Children.Add(content);
        grid.Children.Add(layer); // dodanie do Children rejestruje Name w namescope siatki
        card.Child = grid;
        card.ClipToBounds = true;
    }

    private static void OnMouseMove(object sender, MouseEventArgs args)
    {
        if (sender is not Border card) return;
        EnsureLayer(card);
        if (card.Child is not Grid grid || grid.FindName(LayerName) is not Border layer) return;
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
        if (sender is not Border card || card.Child is not Grid grid) return;
        if (grid.FindName(LayerName) is not Border layer) return;
        layer.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(320)), HandoffBehavior.SnapshotAndReplace);
    }

    private static Color RevealColor()
    {
        if (Application.Current?.TryFindResource("SxRevealColor") is Color token) return token;
        return Color.FromArgb(46, 34, 211, 238);
    }
}
