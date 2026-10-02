using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;
using SentinelX.Models;
using SentinelX.Services.Gaming;
using SentinelX.Services.Monitoring;
using SentinelX.Services.Performance;
using SentinelX.Services.Voice;

namespace SentinelX.ViewModels;

public sealed record QuickActionVm(string Key, string Icon, string Label, string Description, RelayCommand Action);
public sealed record WidgetCard(string Key, string Title, string Value, string Subtitle, string Icon, string ColorKey, double Progress);

public partial class DashboardViewModel : ObservableObject
{
    private readonly ISystemMonitorService monitor;
    private readonly IGamingService gaming;
    private readonly IUiDispatcher ui;
    private readonly NotificationService notifications;

    [ObservableProperty] private SystemSnapshot snapshot = SystemSnapshot.Empty;
    [ObservableProperty] private bool isGaming;
    [ObservableProperty] private string sentinelStatusText = "Sentinel Online";
    [ObservableProperty] private string aiStatusText = "Gotowy";
    [ObservableProperty] private string voiceStatusText = "Wyciszony";
    [ObservableProperty] private string stationStatusText = "—";
    [ObservableProperty] private string statusSummary = "System działa prawidłowo";

    public ObservableCollection<QuickActionVm> QuickActions { get; } = [];
    public ObservableCollection<WidgetCard> Widgets { get; } = [];
    public ObservableCollection<SentinelNotification> RecentNotifications { get; } = [];

    public DashboardViewModel(ISystemMonitorService monitor, IGamingService gaming, IUiDispatcher ui,
        NotificationService notifications, IVoiceService voice)
    {
        this.monitor = monitor; this.gaming = gaming; this.ui = ui; this.notifications = notifications;

        QuickActions.Add(new QuickActionVm("ai", "🤖", "Zapytaj Sentinela", "Rozpocznij rozmowę z AI", new RelayCommand(() => Navigate("command"))));
        QuickActions.Add(new QuickActionVm("gaming", "🎮", "Tryb Gry", "Włącz/throtluje tło", new RelayCommand(ToggleGaming)));
        QuickActions.Add(new QuickActionVm("monitor", "📊", "Monitor Systemu", "CPU · RAM · GPU · Sieć", new RelayCommand(() => Navigate("system"))));
        QuickActions.Add(new QuickActionVm("devices", "📱", "Urządzenia", "Zarządzaj połączonymi urządzeniami", new RelayCommand(() => Navigate("devices"))));
        QuickActions.Add(new QuickActionVm("automation", "⚡", "Automatyzacja", "Zadania i skrypty", new RelayCommand(() => Navigate("tasks"))));
        QuickActions.Add(new QuickActionVm("voice", "🎙", "Głos", "Naciśnij i mów", new RelayCommand(() => Navigate("voice"))));

        RefreshFromSnapshot(SystemSnapshot.Empty);
        monitor.Updated += OnSystemUpdated;
    }

    private void OnSystemUpdated(SystemSnapshot s) => ui.Post(() => RefreshFromSnapshot(s));

    private void RefreshFromSnapshot(SystemSnapshot s)
    {
        Snapshot = s;
        IsGaming = !string.IsNullOrEmpty(s.Game);
        Widgets.Clear();
        Widgets.Add(new WidgetCard("cpu", "CPU", s.CpuText, $"{Environment.ProcessorCount} rdzeni", "🖥", "Cyan", double.IsFinite(s.Cpu) ? s.Cpu : 0));
        Widgets.Add(new WidgetCard("ram", "RAM", s.RamText, "Pamięć", "🧠", "Violet", s.RamPercent));
        Widgets.Add(new WidgetCard("gpu", "GPU", s.GpuText, "Karta graficzna", "🎮", "Mint", double.IsFinite(s.Gpu) ? s.Gpu : 0));
        Widgets.Add(new WidgetCard("net", "Sieć", s.NetText, "Transfer", "📡", "Blue", 0));
        Widgets.Add(new WidgetCard("game", "Gry", s.GamingText, IsGaming ? "Aktywny tryb gry" : "Spokój", IsGaming ? "Amber" : "Idle", IsGaming ? 100 : 0));
        Widgets.Add(new WidgetCard("temp", "Temp CPU", s.TempCpuText, "Procesor", "🌡", double.IsFinite(s.CpuTempC ?? double.NaN) && s.CpuTempC > 80 ? "Error" : "Cyan", 0));

        if (RecentNotifications.Count == 0)
        {
            RecentNotifications.Add(new SentinelNotification(Guid.NewGuid(), NotificationLevel.Info, "Witaj w Sentinel X 2.0",
                "Nowy pulpit, wielordzeniowy silnik i glass design.", DateTimeOffset.Now, AutoDismiss: false));
        }
    }

    private void ToggleGaming()
    {
        // Toggle through GamingModeService if exposed; otherwise push a toast explaining.
        notifications.Info("Tryb Gry", "Włączam wykrywanie pełnoekranowych aplikacji — automatycznie w grze.");
    }

    public event Action<string>? NavigateRequested;
    private void Navigate(string key) => NavigateRequested?.Invoke(key);
}
