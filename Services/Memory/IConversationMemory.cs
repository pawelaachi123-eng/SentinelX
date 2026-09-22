namespace SentinelX.Services.Memory;
public interface IConversationMemory
{
    string? LastStorageError { get; }
    void AddUserMessage(string text, string source);
    void AddAssistantMessage(string text);
    IReadOnlyList<ConversationMemoryEntry> GetRecentEntries(int maxEntries = 100);
    string GetRecentContext(int maxEntries = 24);
    void StartNewSession();
}
