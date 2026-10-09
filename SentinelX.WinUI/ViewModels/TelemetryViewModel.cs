using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SentinelX.Core;
using SentinelX.Models;
using SentinelX.Services.Monitoring;
using SentinelX.WinUI.Core;

namespace SentinelX.WinUI.ViewModels;

/// <summary>
/// Live telemetry: current snapshot plus short ring-buffer histories for the charts.
/// Gaming Mode slows the chart sampling down.
/// </summary>
public sealed partial class TelemetryViewModel : ObservableObject, IDisposable
{
    private const int Capacity = 60;
    private readonly ISystemMonitorService monitor;
    private readonly GamingPolicyService gaming;
    private readonly IUiDispatcher dispatcher;
    private long updates;
    private bool disposed;

    [ObservableProperty] private SystemSnapshot snapshot = SystemSnapshot.Empty;

    public ObservableCollection<double> CpuHistory { get; } = [];
    public ObservableCollection<double> GpuHistory { get; } = [];
    public ObservableCollection<double> RamHistory { get; } = [];

    public TelemetryViewModel(ISystemMonitorService monitor, GamingPolicyService gaming, IUiDispatcher dispatcher)
    {
        this.monitor = monitor;
        this.gaming = gaming;
        this.dispatcher = dispatcher;
        Snapshot = monitor.Current;
        monitor.Updated += OnUpdate;
    }

    private void OnUpdate(SystemSnapshot value) => dispatcher.Post(() =>
    {
        if (disposed) return;
        Snapshot = value;
        updates++;
        if (updates % Math.Max(1, gaming.TelemetryDivisor) != 0) return;
        Push(CpuHistory, value.Cpu);
        Push(GpuHistory, value.Gpu);
        Push(RamHistory, value.RamTotal > 0 ? value.RamUsed / value.RamTotal * 100 : double.NaN);
    });

    private static void Push(ObservableCollection<double> target, double value)
    {
        double point = double.IsFinite(value) ? Math.Clamp(value, 0, 100) : target.LastOrDefault();
        target.Add(point);
        while (target.Count > Capacity) target.RemoveAt(0);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        monitor.Updated -= OnUpdate;
    }
}
