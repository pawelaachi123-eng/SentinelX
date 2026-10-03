using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using SentinelX.Services.Desktop;
using SentinelX.ViewModels;

namespace SentinelX.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel, IDesktopService desktop)
    {
        InitializeComponent();
        DataContext = viewModel;
        desktop.Attach(this);
        StateChanged += MainWindow_StateChanged;
        UpdateMaximizeButton();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateMaximizeButton();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void MainWindow_StateChanged(object? sender, EventArgs e) => UpdateMaximizeButton();

    private void UpdateMaximizeButton()
    {
        if (MaximizeButton == null) return;
        bool maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "❐" : "□";
        MaximizeButton.ToolTip = maximized ? "Przywróć" : "Maksymalizuj";
        AutomationProperties.SetName(MaximizeButton, maximized ? "Przywróć okno" : "Maksymalizuj okno");
    }

    private void PaletteBackdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, sender)) return;
        if (DataContext is MainViewModel viewModel) viewModel.Palette.CloseCommand.Execute(null);
        e.Handled = true;
    }

    private void ReadinessBackdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, sender)) return;
        if (DataContext is MainViewModel viewModel) viewModel.Readiness.CloseCommand.Execute(null);
        e.Handled = true;
    }
}
