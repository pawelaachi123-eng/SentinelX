namespace SentinelX.Services.History;
public interface IHistoryService
{
    IReadOnlyList<ActionHistoryEntry> ReadActions();
    IReadOnlyList<ConversationMemoryEntry> ReadConversation();
    string? StorageError { get; }
}
