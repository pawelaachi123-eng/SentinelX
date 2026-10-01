namespace SentinelX.Models.Repository;

public enum RepoSymbolKind
{
    File,
    Class,
    Interface,
    Method,
    Property,
    Field,
    Enum,
    Record,
    Struct,
    Test,
    Configuration,
    Project,
    Unknown
}

public sealed record RepoSymbol(
    string Id,
    string Name,
    RepoSymbolKind Kind,
    string FilePath,
    int Line,
    int EndLine,
    string? ParentId = null,
    string[]? Modifiers = null);

public sealed record RepoReference(string FromSymbolId, string ToSymbolId, string FilePath, int Line);

public sealed record RepoBuildError(string File, int Line, string Code, string Message, string Severity);
