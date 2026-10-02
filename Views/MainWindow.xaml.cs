using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SentinelX.Services.Desktop;
using SentinelX.Services.Performance;
using SentinelX.ViewModels;

namespace SentinelX.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel vm, IDesktopService desktop)
    {
        InitializeComponent(); DataContext = vm; desktop.Attach(this);
        Loaded += (_, _) =>
        {
            App.Services.GetService<SxThemeService>()?.Apply();
            App.Services.GetService<Services.Monitoring.ISystemMonitorService>()?.Start();
        };
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.SidebarCollapsed)) Sidebar.Width = vm.SidebarCollapsed ? 64 : 232;
            if (e.PropertyName == nameof(vm.SelectedItem)) PageTitle.Text = vm.SelectedItem?.Label ?? "Sentinel";
        };
    }
}
