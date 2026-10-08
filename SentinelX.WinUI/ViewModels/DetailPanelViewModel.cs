using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;
using SentinelX.Models;
using SentinelX.Services.Link;
using SentinelX.Services.Monitoring;
using SentinelX.Services.Settings;
using SentinelX.ViewModels;

namespace SentinelX.WinUI.ViewModels;

public enum DetailKind
{
    None,
    Cpu,
    Gpu,
    Ram,
    Net,
    Ai,
    Watch
}

/// <summary>
/// Sliding detail panel: CPU / GPU / RAM / NET / AI / WATCH open in place,
/// without switching the whole view.
/// </summary>
public sealed partial class DetailPanelViewModel : ObservableObject, IDisposable
{
    private readonly ISystemMonitorService monitor;
    private readonly AlertFeed alerts;
    private readonly ISettingsService settings;
    private readonly IUiDispatcher dispatcher;
    private bool disposed;

    public AiViewModel Ai { get; }

    [ObservableProperty] private DetailKind kind = DetailKind.None;
    [ObservableProperty] private string title = "";
    [ObservableProperty] private SystemSnapshot snapshot = SystemSnapshot.Empty;
    [ObservableProperty] private bool watchEnabled = true;
    [ObservableProperty] private string watchSummary = "";

    public ObservableCollection<LinkAlert> RecentAlerts { get; } = [];

    public bool IsOpen => Kind != DetailKind.None;

    public DetailPanelViewModel(ISystemMonitorService monitor, AlertFeed alerts,
        ISettingsService settings, AiViewModel ai, IUiDispatcher dispatcher)
    {
        this.monitor = monitor;
        this.alerts = alerts;
        this.settings = settings;
        this.dispatcher = dispatcher;
        Ai = ai;
        Snapshot = monitor.Current;
        monitor.Updated += OnSnapshot;
        alerts.Added += OnAlert;
        settings.Changed += SyncWatch;
        SyncWatch();
        RefreshAlerts();
    }

    partial void OnKindChanged(DetailKind value)
    {
        OnPropertyChanged(nameof(IsOpen));
        Title = value switch
        {
            DetailKind.Cpu => "Procesor",
            DetailKind.Gpu => "Karta graficzna",
            DetailKind.Ram => "Pamięć i dyski",
            DetailKind.Net => "Sieć",
            DetailKind.Ai => "Silnik AI",
            DetailKind.Watch => "Sentinel Watch",
            _ => ""
        };

        if (value == DetailKind.Ai)
            Ai.RefreshCommand.Execute(null);
        if (value == DetailKind.Watch)
            RefreshAlerts();
    }

    public void Open(DetailKind kind) => Kind = kind;

    [RelayCommand]
    private void Close() => Kind = DetailKind.None;

    [RelayCommand]
    private void SetWatchEnabled(bool enabled)
    {
        settings.Current.Watch.Enabled = enabled;
        settings.Save();
    }

    private void OnSnapshot(SystemSnapshot value) => dispatcher.Post(() =>
    {
        if (!disposed) Snapshot = value;
    });

    private void OnAlert(LinkAlert alert) => dispatcher.Post(() =>
    {
        if (!disposed && Kind == DetailKind.Watch) RefreshAlerts();
    });

    private void SyncWatch() => dispatcher.Post(() =>
    {
        if (disposed) return;
        var watch = settings.Current.Watch;
        WatchEnabled = watch.Enabled;
        WatchSummary = $"Progi: CPU {watch.CpuAlertPercent}% · RAM {watch.RamAlertPercent}% · opóźnienie {watch.MinSecondsBeforeAlert} s · przerwa {watch.CooldownMinutes} min";
    });

    private void RefreshAlerts()
    {
        RecentAlerts.Clear();
        foreach (var alert in alerts.After(0, 8))
            RecentAlerts.Add(alert);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        monitor.Updated -= OnSnapshot;
        alerts.Added -= OnAlert;
        settings.Changed -= SyncWatch;
    }
}
