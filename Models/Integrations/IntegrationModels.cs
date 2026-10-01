namespace SentinelX.Models.Integrations;

public enum IntegrationAuthKind
{
    None,
    LocalToken,
    OAuth2,
    PairingCode,
    UserPassword
}

public enum IntegrationStatus
{
    Disconnected,
    Pairing,
    Connected,
    Error,
    Revoked
}

public enum IntegrationHealth
{
    Healthy,
    Degraded,
    Offline,
    Unknown
}

public sealed class IntegrationActionDefinition
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string RequiredPermission { get; set; } = "";
}

public sealed class IntegrationDefinition
{
    public string Id { get; set; } = "";
    public string Provider { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public IntegrationAuthKind AuthKind { get; set; } = IntegrationAuthKind.None;
    public List<string> Capabilities { get; set; } = new();
    public List<IntegrationActionDefinition> Actions { get; set; } = new();
}

public sealed class IntegrationState
{
    public string Id { get; set; } = "";
    public IntegrationStatus Status { get; set; } = IntegrationStatus.Disconnected;
    public IntegrationHealth Health { get; set; } = IntegrationHealth.Unknown;
    public DateTimeOffset? AuthenticatedAt { get; set; }
    public string? LastError { get; set; }
    public HashSet<string> GrantedScopes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
