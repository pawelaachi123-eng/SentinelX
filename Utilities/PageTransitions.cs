using System.Windows;
using System.Windows.Media.Animation;
namespace SentinelX.Utilities;
/// <summary>View-only behavior; disabled in gaming mode and when Windows animations are disabled.</summary>
public static class PageTransitions
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(PageTransitions), new PropertyMetadata(false, Changed));
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement view) return;
        view.Loaded -= Loaded;
        if ((bool)e.NewValue) view.Loaded += Loaded;
    }
    private static void Loaded(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement view && SystemParameters.ClientAreaAnimation && Application.Current.TryFindResource("SxAnimationsEnabled") is true)
            view.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
    }
}
