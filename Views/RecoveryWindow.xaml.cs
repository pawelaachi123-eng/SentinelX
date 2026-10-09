using System.Windows;
using SentinelX.Services.Maintenance;
using SentinelX.ViewModels;
namespace SentinelX.Views;
public partial class RecoveryWindow : Window
{
    public RecoveryWindow(RecoveryState state)
    {
        InitializeComponent();
        var vm = new RecoveryViewModel(state, RecoveryService.CreateMaintenance());
        vm.RequestClose += result => { DialogResult = result; Close(); };
        DataContext = vm;
    }
}
