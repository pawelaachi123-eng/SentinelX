namespace SentinelX.Models.Automations;

public enum AutomationTriggerKind
{
    Schedule,
    ProcessEvent,    // aplikacja wystartowała/zamknęła się
    Threshold,       // CPU/temp/disk
    DeviceEvent,     // zdarzenie z urządzenia/integracji
    VoiceCommand,    // fraza
    SystemEvent      // start/stop/gaming
}

public sealed class AutomationTrigger
{
    public AutomationTriggerKind Kind { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AutomationCondition
{
    public string Expression { get; set; } = ""; // prosty DSL, np. "cpu > 90"
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AutomationAction
{
    public string ActionId { get; set; } = "";
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AutomationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public AutomationTrigger When { get; set; } = new();
    public List<AutomationCondition> If { get; set; } = new();
    public List<AutomationAction> Then { get; set; } = new();
    public bool IsTemplate { get; set; }
    public string? TemplateId { get; set; }
}
