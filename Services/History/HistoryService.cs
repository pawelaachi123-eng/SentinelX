namespace SentinelX.Services.History;
public sealed class HistoryService(ActionHistoryService history, ConversationMemoryService memory) : IHistoryService
{
    public IReadOnlyList<ActionHistoryEntry> ReadActions() => history.GetRecentEntries(200);
    public IReadOnlyList<ConversationMemoryEntry> ReadConversation() => memory.GetRecentEntries(200);
    public string? StorageError => history.LastStorageError ?? history.LastReadError ?? memory.LastStorageError;
}
