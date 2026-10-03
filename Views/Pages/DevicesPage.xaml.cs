using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using SentinelX.ViewModels;
using SentinelX.Views.Link;

namespace SentinelX.Views.Pages;

public partial class DevicesPage : UserControl
{
    private DevicesViewModel? viewModel;

    public DevicesPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => { Rebind(); UpdateQr(); };
        Unloaded += (_, _) => Detach();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsLoaded) return;
        Rebind();
        UpdateQr();
    }

    private void Rebind()
    {
        if (viewModel != null) viewModel.PropertyChanged -= ViewModelPropertyChanged;
        viewModel = DataContext as DevicesViewModel;
        if (viewModel != null) viewModel.PropertyChanged += ViewModelPropertyChanged;
    }

    private void Detach()
    {
        if (viewModel != null) viewModel.PropertyChanged -= ViewModelPropertyChanged;
        viewModel = null;
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DevicesViewModel.PrimaryAddress) or nameof(DevicesViewModel.HasAddress))
            Dispatcher.BeginInvoke(new Action(UpdateQr));
    }

    private void UpdateQr() => PairingQr.Source = viewModel?.HasAddress == true
        ? PhoneQrCode.Create(viewModel.PrimaryAddress)
        : null;
}
