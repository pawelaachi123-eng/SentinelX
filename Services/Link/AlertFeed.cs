namespace SentinelX.Services.Link;

/// <summary>Short in-memory journal of things that deserve the user's attention (load alerts, reminders, engine events).
/// The phone reads it with long-polling. Nothing here is written to disk.</summary>
public sealed class AlertFeed
{
    private const int Capacity = 200;
    private readonly object gate = new();
    private readonly List<LinkAlert> items = [];
    private long lastId;
    private TaskCompletionSource<bool> signal = NewSignal();

    public event Action<LinkAlert>? Added;

    private static TaskCompletionSource<bool> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public long LastId { get { lock (gate) return lastId; } }

    public LinkAlert Add(string level, string title, string text)
    {
        string normalized = level is "warn" or "error" ? level : "info";
        LinkAlert alert;
        TaskCompletionSource<bool> previous;
        lock (gate)
        {
            alert = new LinkAlert(++lastId, DateTimeOffset.Now, normalized, Clip(title, 120), Clip(text, 600));
            items.Add(alert);
            if (items.Count > Capacity) items.RemoveAt(0);
            previous = signal;
            signal = NewSignal();
        }
        previous.TrySetResult(true);
        try { Added?.Invoke(alert); }
        catch (Exception ex) { AppLog.Write(ex); }
        return alert;
    }

    /// <summary>Alerts newer than <paramref name="id"/>, oldest first (at most <paramref name="max"/> of the newest ones).</summary>
    public IReadOnlyList<LinkAlert> After(long id, int max = 50)
    {
        lock (gate) return items.Where(x => x.Id > id).TakeLast(Math.Clamp(max, 1, Capacity)).ToArray();
    }

    /// <summary>Returns immediately when something newer exists, otherwise waits up to <paramref name="wait"/> for the next alert.</summary>
    public async Task<IReadOnlyList<LinkAlert>> WaitAfterAsync(long id, TimeSpan wait, CancellationToken token)
    {
        Task<bool> pending;
        lock (gate)
        {
            if (wait <= TimeSpan.Zero || items.Any(x => x.Id > id)) return After(id);
            pending = signal.Task;
        }
        try { await pending.WaitAsync(wait, token).ConfigureAwait(false); }
        catch (TimeoutException) { }
        return After(id);
    }

    private static string Clip(string? text, int max)
    {
        string safe = new((text ?? "").Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray());
        string trimmed = safe.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
