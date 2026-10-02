using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Microsoft.Extensions.DependencyInjection;
using SentinelX.Services.Desktop;
using SentinelX.Services.System;
using SentinelX.ViewModels;

namespace SentinelX.Views;

public sealed class BooleanInvertConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}

/// <summary>Collapsed when true → Collapsed; visible when false.</summary>
public sealed class CollapsedVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel, IDesktopService desktop)
    {
        InitializeComponent();
        DataContext = viewModel;
        desktop.Attach(this);

        Loaded += (_, _) =>
        {
            ApplySidebar(viewModel.SidebarCollapsed);
            var theme = App.Services.GetService<ThemeService>();
            theme?.Apply();
            // Monitor system
            var sys = App.Services.GetService<Services.Monitoring.ISystemMonitorService>();
            sys?.Start();
        };
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(viewModel.SidebarCollapsed)) ApplySidebar(viewModel.SidebarCollapsed);
            if (e.PropertyName == nameof(viewModel.SelectedItem))
            {
                PageTitle.Text = viewModel.SelectedItem?.Label ?? "Sentinel";
            }
        };
    }

    private void ApplySidebar(bool collapsed)
    {
        Sidebar.Width = collapsed ? 64 : 232;
    }
}
