namespace SentinelX;

public sealed class VoiceCommandInbox
{
    private readonly Queue<string> pending = new();
    private readonly HashSet<string> seen = new(StringComparer.Ordinal);
    public int Count => pending.Count;
    public bool TryEnqueue(string command)
    {
        if (pending.Count >= 4 || string.IsNullOrWhiteSpace(command)) return false;
        string normalized = ConversationMemoryService.Normalize(command);
        if (normalized.Length > 0 && seen.Contains(normalized)) return false;
        if (normalized.Length > 0) seen.Add(normalized);
        pending.Enqueue(command);
        return true;
    }
    public bool TryDequeue(out string command) => pending.TryDequeue(out command!);
    public void Clear()
    {
        pending.Clear();
        seen.Clear();
    }
}
