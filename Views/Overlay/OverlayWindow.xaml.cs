using System.Windows;
using System.Windows.Input;
namespace SentinelX.Views.Overlay;
public partial class OverlayWindow : Window
{
    public OverlayWindow() => InitializeComponent();
    private void DragOverlay(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
}
