using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using SentinelX.Core;
using SentinelX.Services.Link;
using SentinelX.WinUI.Core;

namespace SentinelX.WinUI.Services.Toasts;

/// <summary>
/// Modern Windows toast notifications for watch alerts, reminders and engine events.
/// Everything flows through <see cref="AlertFeed"/>, so the phone and the PC stay in sync.
/// Gaming Mode quiets non-critical toasts.
/// </summary>
public sealed class ToastService : IDisposable
{
    private readonly AlertFeed alerts;
    private readonly GamingPolicyService gaming;
    private readonly IUiDispatcher dispatcher;
    private AppNotificationManager? manager;
    private bool disposed;

    /// <summary>Raised when the user clicks a toast — the desktop service shows the window.</summary>
    public event Action? Activated;

    public ToastService(AlertFeed alerts, GamingPolicyService gaming, IUiDispatcher dispatcher)
    {
        this.alerts = alerts;
        this.gaming = gaming;
        this.dispatcher = dispatcher;
    }

    public void Start()
    {
        alerts.Added += OnAlert;
        try
        {
            manager = AppNotificationManager.Default;
            manager.NotificationInvoked += OnInvoked;
            manager.Register();
        }
        catch (Exception ex)
        {
            SentinelX.AppLog.Write(ex);
            manager = null;
        }
    }

    private void OnInvoked(object? sender, AppNotificationActivatedEventArgs args) =>
        dispatcher.Post(() =>
        {
            try
            {
                Activated?.Invoke();
            }
            catch (Exception ex)
            {
                SentinelX.AppLog.Write(ex);
            }
        });

    private void OnAlert(LinkAlert alert)
    {
        if (disposed || manager == null) return;
        if (alert.Level == "info" && gaming.SuppressNonCriticalToasts) return;

        dispatcher.Post(() =>
        {
            if (disposed) return;
            try
            {
                var builder = new AppNotificationBuilder()
                    .AddText(Clip(alert.Title, 90))
                    .AddText(Clip(alert.Text, 220));
                if (alert.Level == "error") builder.SetScenario(AppNotificationScenario.Urgent);
                manager.Show(builder.BuildNotification());
            }
            catch (Exception ex)
            {
                SentinelX.AppLog.Write(ex);
            }
        });
    }

    private static string Clip(string value, int max) =>
        value.Length <= max ? value : value[..max].TrimEnd() + "…";

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        alerts.Added -= OnAlert;
        if (manager != null)
        {
            try
            {
                manager.NotificationInvoked -= OnInvoked;
                manager.Unregister();
            }
            catch (Exception ex)
            {
                SentinelX.AppLog.Write(ex);
            }

            manager = null;
        }
    }
}
