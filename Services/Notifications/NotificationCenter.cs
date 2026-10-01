using System.Collections.Concurrent;
using SentinelX.Models.Notifications;

namespace SentinelX.Services.Notifications;

/// <summary>
/// Centrum powiadomień z deduplikacją, cooldownem i grupowaniem. Nie spamuje 50 identycznymi alertami.
/// </summary>
public sealed class NotificationCenter
{
    private readonly ConcurrentQueue<SmartNotification> notifications = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> lastShown = new(StringComparer.OrdinalIgnoreCase);
    private readonly object gate = new();
    private readonly int maxRetained;
    private TimeSpan defaultCooldown = TimeSpan.FromSeconds(30);

    public event Action<SmartNotification>? NotificationPosted;

    public NotificationCenter(int maxRetained = 200) { this.maxRetained = maxRetained; }

    public void SetCooldown(TimeSpan ts) => defaultCooldown = ts;

    public bool Post(SmartNotification n)
    {
        n = n with { At = n.At ?? DateTimeOffset.UtcNow };
        var key = n.GroupKey ?? $"{n.Source}:{n.Title}:{n.Message}";
        if (lastShown.TryGetValue(key, out var last))
        {
            if (DateTimeOffset.UtcNow - last < defaultCooldown) return false;
        }
        lastShown[key] = n.At.Value;
        notifications.Enqueue(n);
        lock (gate)
        {
            while (notifications.Count > maxRetained && notifications.TryDequeue(out _)) { }
        }
        NotificationPosted?.Invoke(n);
        return true;
    }

    public void Info(string title, string msg, string? groupKey = null, string? source = null) =>
        Post(new SmartNotification(Guid.NewGuid().ToString("N"), title, msg, NotificationLevel.Info, source, groupKey));
    public void Warn(string title, string msg, string? groupKey = null, string? source = null) =>
        Post(new SmartNotification(Guid.NewGuid().ToString("N"), title, msg, NotificationLevel.Warning, source, groupKey));
    public void Error(string title, string msg, string? groupKey = null, string? source = null) =>
        Post(new SmartNotification(Guid.NewGuid().ToString("N"), title, msg, NotificationLevel.Error, source, groupKey));
    public void Success(string title, string msg, string? groupKey = null, string? source = null) =>
        Post(new SmartNotification(Guid.NewGuid().ToString("N"), title, msg, NotificationLevel.Success, source, groupKey));

    public IReadOnlyList<SmartNotification> Recent(int count = 50) => notifications.TakeLast(count).Reverse().ToList();
    public void Clear() { notifications.Clear(); lastShown.Clear(); }
}
