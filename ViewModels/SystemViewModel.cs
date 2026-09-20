using CommunityToolkit.Mvvm.ComponentModel;
using SentinelX.Core;
using SentinelX.Models;
using SentinelX.Services.Monitoring;
namespace SentinelX.ViewModels;
public partial class SystemViewModel : ObservableObject, IDisposable
{
    private readonly ISystemMonitorService monitor;
    private readonly IUiDispatcher dispatcher;
    [ObservableProperty] private SystemSnapshot snapshot = SystemSnapshot.Empty;
    public SystemViewModel(ISystemMonitorService monitor, IUiDispatcher dispatcher)
    { this.monitor = monitor; this.dispatcher = dispatcher; Snapshot = monitor.Current; monitor.Updated += Update; }
    private void Update(SystemSnapshot value) => dispatcher.Post(() => Snapshot = value);
    public void Dispose() => monitor.Updated -= Update;
}
