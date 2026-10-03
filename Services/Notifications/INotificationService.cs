namespace SentinelX.Services.Notifications;

/// <summary>A deliberately small, transient notification. No notification body is persisted to disk.</summary>
public sealed record AppNotification(DateTimeOffset Timestamp, string Severity, string Category, string Title, string Message);

public interface INotificationService
{
    event Action<AppNotification>? Published;
    AppNotification Publish(string category, string title, string message, string severity = "info");
}
