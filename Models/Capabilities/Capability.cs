namespace SentinelX.Models.Capabilities;

/// <summary>
/// Pojedyncza możliwość systemu — np. "volumes", "file-read", "ai-chat", "voice-output".
/// Każdy tool zadeklaruje dostarczane capability; każdy skill zadeklaruje wymagane.
/// </summary>
public sealed record CapabilityDescriptor(string Id, string Category, string Description, bool IsMissing = false, string? MissingReason = null);

public sealed class CapabilityEdge
{
    public string Source { get; init; } = "";   // id narzędzia/integration/plugin
    public string Capability { get; init; } = "";
    public string Kind { get; init; } = "provides"; // "provides" | "requires"
}
