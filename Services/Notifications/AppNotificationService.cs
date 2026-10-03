using SentinelX.Services.Link;

namespace SentinelX.Services.Notifications;

/// <summary>One notification path for the PC tray and the already-paired phone alert feed.</summary>
public sealed class AppNotificationService(AlertFeed alerts) : INotificationService
{
    public event Action<AppNotification>? Published;

    public AppNotification Publish(string category, string title, string message, string severity = "info")
    {
        string level = severity is "warn" or "error" ? severity : "info";
        string safeCategory = Clip(category, 48);
        // AlertFeed is the shared trust boundary for title/body size and control-character removal.
        LinkAlert alert = alerts.Add(level, title, message);
        var notification = new AppNotification(alert.At, level, safeCategory, alert.Title, alert.Text);
        foreach (Action<AppNotification> observer in Published?.GetInvocationList() ?? [])
        {
            try { observer(notification); }
            catch (Exception ex) { AppLog.Write(ex); }
        }
        return notification;
    }

    private static string Clip(string? value, int max)
    {
        string text = new((value ?? "").Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray());
        text = text.Trim();
        if (text.Length > max) text = text[..max];
        return text;
    }
}
