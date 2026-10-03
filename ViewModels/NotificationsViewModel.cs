using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentinelX.Core;
using SentinelX.Services.Link;

namespace SentinelX.ViewModels;

/// <summary>In-memory notification journal shared with the phone link and desktop services.</summary>
public partial class NotificationsViewModel : ObservableObject, IDisposable
{
    private const int VisibleCapacity = 50;
    private readonly AlertFeed alerts;
    private readonly IUiDispatcher dispatcher;
    private long lastReadId;

    public ObservableCollection<LinkAlert> Items { get; } = [];

    [ObservableProperty] private int unreadCount;
    [ObservableProperty] private string status = "Powiadomienia są przechowywane tylko w pamięci tej sesji.";

    public bool HasUnread => UnreadCount > 0;
    public string BadgeAutomationName => UnreadCount == 0
        ? "Powiadomienia"
        : $"Powiadomienia, {UnreadCount} nieprzeczytanych";

    public NotificationsViewModel(AlertFeed alerts, IUiDispatcher dispatcher)
    {
        this.alerts = alerts;
        this.dispatcher = dispatcher;
        lastReadId = alerts.LastId;
        alerts.Added += AlertAdded;
        Refresh();
    }

    partial void OnUnreadCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasUnread));
        OnPropertyChanged(nameof(BadgeAutomationName));
    }

    private void AlertAdded(LinkAlert alert) => dispatcher.Post(() =>
    {
        if (Items.Any(item => item.Id == alert.Id)) return;
        Items.Insert(0, alert);
        while (Items.Count > VisibleCapacity) Items.RemoveAt(Items.Count - 1);
        if (alert.Id > lastReadId) UnreadCount++;
        Status = "Nowe powiadomienie · tylko ta sesja · nic nie jest zapisywane na dysku.";
    });

    [RelayCommand]
    public void Refresh()
    {
        var current = alerts.After(0, VisibleCapacity).OrderByDescending(item => item.Id).ToArray();
        Items.Clear();
        foreach (LinkAlert alert in current) Items.Add(alert);
        UnreadCount = current.Count(item => item.Id > lastReadId);
        Status = Items.Count == 0
            ? "Brak powiadomień w tej sesji. Alerty pojawiają się tu, gdy Watch, przypomnienia lub połączenie z telefonem wymagają uwagi."
            : $"Ostatnie {Items.Count} powiadomień · pamięć bieżącej sesji · wyczyszczenie nastąpi po zamknięciu aplikacji.";
    }

    [RelayCommand]
    public void MarkRead()
    {
        lastReadId = alerts.LastId;
        UnreadCount = 0;
        Status = "Oznaczono bieżące powiadomienia jako przeczytane. Wpisy pozostają do końca tej sesji.";
    }

    public void Dispose() => alerts.Added -= AlertAdded;
}
