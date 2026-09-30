using System.Windows;

namespace SentinelX.Utilities;

/// <summary>
/// Wejście strony: fade + wjazd z dołu + dojście skali (storyboard SxPageIn).
/// Wyłączone automatycznie w grze, przy wyłączonych animacjach w ustawieniach
/// i przy wyłączonych animacjach Windows — decyduje <see cref="Motion.AnimationsOn"/>.
/// </summary>
public static class PageTransitions
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(PageTransitions), new PropertyMetadata(false, Changed));

    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);

    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement view) return;
        view.Loaded -= Loaded;
        if (e.NewValue is true) view.Loaded += Loaded;
    }

    private static void Loaded(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement view) return;
        Motion.EnsureTransform(view);
        if (!Motion.AnimationsOn) { view.Opacity = 1; return; }
        Motion.Play(view, "SxPageIn");
    }
}
