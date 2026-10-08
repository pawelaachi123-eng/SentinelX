using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SentinelX.WinUI.ViewModels;

namespace SentinelX.WinUI.UI.Panels;

public sealed partial class HistoryPanel : UserControl
{
    public HistoryPanel() => InitializeComponent();

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is HistoryPanelViewModel vm) vm.Refresh();
    }
}
