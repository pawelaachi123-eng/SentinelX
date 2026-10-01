using System.Threading.Tasks;

namespace SentinelX.Models.Notifications;

public enum NotificationLevel
{
    Info,
    Success,
    Warning,
    Error
}

public sealed record SmartNotification(
    string Id,
    string Title,
    string Message,
    NotificationLevel Level = NotificationLevel.Info,
    string? Source = null,
    string? GroupKey = null,
    DateTimeOffset? At = null,
    bool IsDismissible = true,
    string? ActionLabel = null,
    Func<CancellationToken, Task>? Action = null);
