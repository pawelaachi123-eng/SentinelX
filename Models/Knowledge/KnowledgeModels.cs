namespace SentinelX.Models.Knowledge;

public enum KnowledgeSourceKind
{
    Txt,
    Markdown,
    Pdf,
    SourceCode,
    Directory
}

public sealed class KnowledgeSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = "";
    public string Path { get; set; } = "";
    public KnowledgeSourceKind Kind { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? IndexedAt { get; set; }
    public int ChunkCount { get; set; }
    public string? Hash { get; set; }
    public bool Indexed => IndexedAt.HasValue;
}

public sealed record KnowledgeChunk(string SourceId, int Index, string Text, string[] Tokens);
