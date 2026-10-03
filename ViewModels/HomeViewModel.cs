using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Models;

namespace SentinelX.ViewModels;

/// <summary>Home dashboard: a presentation over existing live services and cached page view-models.</summary>
public partial class HomeViewModel : ObservableObject, IDisposable
{
    private readonly TaskViewModel tasksSource;
    private readonly HistoryViewModel historySource;

    public SystemViewModel System { get; }
    public VoiceViewModel Voice { get; }
    public TaskViewModel Tasks { get; }
    public AutomationViewModel Automations { get; }
    public DevicesViewModel Devices { get; }
    public NotificationsViewModel Notifications { get; }
    public ReadinessViewModel Readiness { get; }

    public ObservableCollection<TaskItemViewModel> TodayTasks { get; } = [];
    public ObservableCollection<ReminderItemViewModel> UpcomingReminders { get; } = [];
    public ObservableCollection<ActionHistoryEntry> RecentActivity { get; } = [];

    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private string status = "Dane z komputera są odczytywane lokalnie.";
    [ObservableProperty] private string dateLabel = DateTime.Now.ToString("dddd, d MMMM", CultureInfo.CurrentCulture);

    public event Action<string>? NavigationRequested;

    public HomeViewModel(SystemViewModel system, VoiceViewModel voice, TaskViewModel tasks,
        HistoryViewModel history, AutomationViewModel automations, DevicesViewModel devices,
        NotificationsViewModel notifications, ReadinessViewModel readiness)
    {
        System = system;
        Voice = voice;
        Tasks = tasks;
        Automations = automations;
        Devices = devices;
        Notifications = notifications;
        Readiness = readiness;
        tasksSource = tasks;
        historySource = history;

        tasksSource.DashboardChanged += TasksChanged;
        historySource.Entries.CollectionChanged += HistoryChanged;
        RefreshProjections();
        RefreshActivity();
    }

    private void TasksChanged() => RefreshProjections();
    private void HistoryChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshActivity();

    private void RefreshProjections()
    {
        TodayTasks.Clear();
        foreach (TaskItemViewModel item in tasksSource.GetDashboardItems(4)) TodayTasks.Add(item);

        UpcomingReminders.Clear();
        foreach (ReminderItemViewModel item in tasksSource.Reminders.Where(item => item.Awaiting).Take(3))
            UpcomingReminders.Add(item);

        OnPropertyChanged(nameof(TodayTaskCount));
        OnPropertyChanged(nameof(HasTodayTasks));
        OnPropertyChanged(nameof(HasUpcomingReminders));
    }

    private void RefreshActivity()
    {
        RecentActivity.Clear();
        foreach (ActionHistoryEntry entry in historySource.Entries.OrderByDescending(item => item.Timestamp).Take(5))
            RecentActivity.Add(entry);
        OnPropertyChanged(nameof(HasRecentActivity));
    }

    public int TodayTaskCount => TodayTasks.Count;
    public bool HasTodayTasks => TodayTasks.Count > 0;
    public bool HasUpcomingReminders => UpcomingReminders.Count > 0;
    public bool HasRecentActivity => RecentActivity.Count > 0;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        Status = "Odświeżam lokalne dane…";
        try
        {
            tasksSource.RefreshCommand.Execute(null);
            Devices.RefreshCommand.Execute(null);
            Notifications.RefreshCommand.Execute(null);
            await historySource.RefreshCommand.ExecuteAsync(null);
            RefreshProjections();
            RefreshActivity();
            DateLabel = DateTime.Now.ToString("dddd, d MMMM", CultureInfo.CurrentCulture);
            Status = $"Ostatnia aktualizacja {DateTime.Now:HH:mm:ss} · dane pozostają na tym komputerze.";
        }
        catch (Exception ex)
        {
            Status = "Nie udało się odświeżyć wszystkich paneli. " + ex.Message;
            AppLog.Write("Home", "Error", "Home dashboard refresh failed.", ex);
        }
        finally { IsRefreshing = false; }
    }

    [RelayCommand] private void OpenAssistant() => NavigationRequested?.Invoke("command");
    [RelayCommand] private void OpenComputer() => NavigationRequested?.Invoke("system");
    [RelayCommand] private void OpenPerformance() => NavigationRequested?.Invoke("gaming");
    [RelayCommand] private void OpenAutomation() => NavigationRequested?.Invoke("automation");
    [RelayCommand] private void OpenDevices() => NavigationRequested?.Invoke("devices");
    [RelayCommand] private void OpenTasks() => NavigationRequested?.Invoke("tasks");
    [RelayCommand] private void OpenActivity() => NavigationRequested?.Invoke("history");
    [RelayCommand] private void OpenNotifications() => NavigationRequested?.Invoke("notifications");

    [RelayCommand]
    private void ToggleVoice()
    {
        if (Voice.State == VoiceState.Off) Voice.StartCommand.Execute(null);
        else Voice.StopCommand.Execute(null);
    }

    public void Dispose()
    {
        tasksSource.DashboardChanged -= TasksChanged;
        historySource.Entries.CollectionChanged -= HistoryChanged;
    }
}
