using System.Windows;
using System.Windows.Controls;
namespace SentinelX.Utilities;
public static class ChatScroll
{
    public static readonly DependencyProperty FollowProperty = DependencyProperty.RegisterAttached("Follow", typeof(bool), typeof(ChatScroll), new PropertyMetadata(false, Changed));
    public static void SetFollow(DependencyObject d, bool value) => d.SetValue(FollowProperty, value);
    public static bool GetFollow(DependencyObject d) => (bool)d.GetValue(FollowProperty);
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scroll) return;
        scroll.ScrollChanged -= Scrolled;
        if ((bool)e.NewValue) scroll.ScrollChanged += Scrolled;
    }
    private static void Scrolled(object sender, ScrollChangedEventArgs e)
    {
        var scroll = (ScrollViewer)sender;
        // Only follow additions if the reader was already near the bottom.
        if (e.ExtentHeightChange > 0 && scroll.VerticalOffset >= scroll.ScrollableHeight - e.ExtentHeightChange - 40)
            scroll.ScrollToEnd();
    }
}
