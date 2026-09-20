using System.Windows;
using SentinelX.Services.Desktop;
using SentinelX.ViewModels;
namespace SentinelX.Views;
public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel, IDesktopService desktop)
    { InitializeComponent(); DataContext = viewModel; desktop.Attach(this); }
}
