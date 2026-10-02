using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace SentinelX.Services.System;

public sealed record SentinelNotification(
    Guid Id,
    NotificationLevel Level,
    string Title,
    string Message,
    DateTimeOffset Timestamp,
    string? ActionLabel = null,
    Action? Action = null,
    bool AutoDismiss = true,
    bool Silent = false
);

/// <summary>
/// 2.0 · Centrum powiadomień. Toasty pojawiają się na UI, historia jest zachowywana.
/// Wszystko publikowane na UI thread.
/// </summary>
public sealed class NotificationService
{
    private readonly Dispatcher dispatcher;
    public ObservableCollection<SentinelNotification> Items { get; } = [];
    public event Action<SentinelNotification>? Toast;

    public NotificationService(Dispatcher dispatcher) { this.dispatcher = dispatcher; }

    public void Push(NotificationLevel level, string title, string message, string? actionLabel = null, Action? action = null, bool autoDismiss = true, bool silent = false)
    {
        var n = new SentinelNotification(Guid.NewGuid(), level, title, message, DateTimeOffset.Now, actionLabel, action, autoDismiss, silent);
        dispatcher.BeginInvoke(new Action(() =>
        {
            Items.Insert(0, n);
            while (Items.Count > 60) Items.RemoveAt(Items.Count - 1);
            Toast?.Invoke(n);
        }));
    }

    public void Info(string title, string msg, bool silent = false) => Push(NotificationLevel.Info, title, msg, silent: silent);
    public void Success(string title, string msg) => Push(NotificationLevel.Success, title, msg);
    public void Warn(string title, string msg) => Push(NotificationLevel.Warning, title, msg);
    public void Error(string title, string msg) => Push(NotificationLevel.Error, title, msg, autoDismiss: false);
    public void Task(string title, string msg) => Push(NotificationLevel.Task, title, msg);
    public void Security(string title, string msg) => Push(NotificationLevel.Security, title, msg, autoDismiss: false);
    public void Device(string title, string msg) => Push(NotificationLevel.Device, title, msg);

    public void Dismiss(Guid id)
    {
        dispatcher.BeginInvoke(new Action(() =>
        {
            for (int i = 0; i < Items.Count; i++)
                if (Items[i].Id == id) { Items.RemoveAt(i); break; }
        }));
    }

    public void ClearAll()
    {
        dispatcher.BeginInvoke(new Action(Items.Clear));
    }
}
