using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SentinelX;
public sealed class MetricOverlay : Window
{
    private readonly TextBlock label = new() { Foreground = new SolidColorBrush(Color.FromRgb(137, 160, 179)), FontSize = 12 };
    private readonly TextBlock value = new() { Foreground = new SolidColorBrush(Color.FromRgb(103, 231, 195)), FontSize = 28, FontWeight = FontWeights.SemiBold };
    public MetricOverlay()
    {
        Title = "Sentinel — statystyka"; Width = 245; Height = 116; Left = SystemParameters.WorkArea.Left + 20; Top = SystemParameters.WorkArea.Top + 30;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        AllowsTransparency = true; Background = Brushes.Transparent;
        var close = new Button { Content = "×", Width = 25, Height = 25, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, ToolTip = "Zamknij nakładkę" };
        close.Click += (_, _) => Close();
        var panel = new StackPanel { Margin = new Thickness(18, 15, 45, 12) }; panel.Children.Add(label); panel.Children.Add(value);
        var grid = new Grid(); grid.Children.Add(panel); grid.Children.Add(close); Content = new Border { CornerRadius = new CornerRadius(18), Background = new SolidColorBrush(Color.FromRgb(17, 26, 40)), BorderBrush = new SolidColorBrush(Color.FromRgb(58, 79, 108)), BorderThickness = new Thickness(1), Child = grid };
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove(); };
    }
    public void Update(string metric, string text) { label.Text = "SENTINEL  /  " + metric; value.Text = text; }
    public void ApplySettings(UiSettings settings)
    {
        double scale = settings.OverlayScalePercent / 100d;
        Width = 245 * scale; Height = 116 * scale; label.FontSize = 12 * scale; value.FontSize = 28 * scale;
        Opacity = settings.OverlayOpacityPercent / 100d;
        value.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.AccentColor));
        Rect area = SystemParameters.WorkArea;
        Left = settings.OverlayPosition.StartsWith("Prawy") ? area.Right - Width - 20 : area.Left + 20;
        Top = settings.OverlayPosition.EndsWith("dolny") ? area.Bottom - Height - 20 : area.Top + 20;
    }
}
