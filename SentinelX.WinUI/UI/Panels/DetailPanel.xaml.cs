using System.ComponentModel;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SentinelX.WinUI.ViewModels;

namespace SentinelX.WinUI.UI.Panels;

public sealed partial class DetailPanel : UserControl
{
    public static readonly DependencyProperty TelemetryProperty =
        DependencyProperty.Register(nameof(Telemetry), typeof(TelemetryViewModel), typeof(DetailPanel), new PropertyMetadata(null));

    public static readonly DependencyProperty QuickCommandProperty =
        DependencyProperty.Register(nameof(QuickCommand), typeof(ICommand), typeof(DetailPanel), new PropertyMetadata(null));

    public TelemetryViewModel? Telemetry
    {
        get => (TelemetryViewModel?)GetValue(TelemetryProperty);
        set => SetValue(TelemetryProperty, value);
    }

    public ICommand? QuickCommand
    {
        get => (ICommand?)GetValue(QuickCommandProperty);
        set => SetValue(QuickCommandProperty, value);
    }

    public DetailPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => PanelBorder.Translation = new System.Numerics.Vector3(0, 0, 24);
        DataContextChanged += (_, _) => Subscribe();
        Subscribe();
    }

    private DetailPanelViewModel? tracked;

    private void Subscribe()
    {
        if (tracked != null) tracked.PropertyChanged -= OnVmChanged;
        tracked = DataContext as DetailPanelViewModel;
        if (tracked != null) tracked.PropertyChanged += OnVmChanged;
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DetailPanelViewModel.Kind) && tracked?.IsOpen == true)
        {
            try
            {
                SlideIn.Begin();
            }
            catch
            {
            }
        }
    }

    private void Watch_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && DataContext is DetailPanelViewModel vm)
            vm.SetWatchEnabledCommand.Execute(toggle.IsOn);
    }
}
