using System.Windows;
using System.Windows.Input;
using SentinelX;
namespace SentinelX.Views.Overlay;
public partial class OverlayWindow : Window
{
    public OverlayWindow() => InitializeComponent();
    public event Action<double, double>? DragCompleted;

    private void DragOverlay(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); }
        catch (InvalidOperationException ex) { AppLog.Write("UI", "Warning", "Overlay drag could not complete.", ex); }
        finally { DragCompleted?.Invoke(Left, Top); }
    }

    private void CloseOverlay(object sender, RoutedEventArgs e) => Close();
}
