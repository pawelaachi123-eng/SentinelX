using System.Linq;
using System.Collections.Concurrent;

namespace SentinelX.Services.Health;

public enum HealthStatus
{
    Starting,
    Healthy,
    Degraded,
    Unhealthy,
    Disabled,
    Unsupported
}

public sealed record ComponentHealth(string Name, HealthStatus Status, string? Detail = null, DateTimeOffset? At = null);

/// <summary>
/// Centralny rejestr stanu zdrowia wszystkich komponentów: Core, AI, Memory, Voice, Tools,
/// Skills, Plugins, Automations, Remote (Link), Database, Mobile, Overlay.
/// Komponenty raportują stan samodzielnie; brak raportu = Unknown/Degraded.
/// </summary>
public sealed class SentinelHealthService
{
    private readonly ConcurrentDictionary<string, ComponentHealth> components = new(StringComparer.OrdinalIgnoreCase);
    public event Action<ComponentHealth>? Changed;

    public SentinelHealthService()
    {
        foreach (var n in KnownComponents)
            components[n] = new ComponentHealth(n, HealthStatus.Starting);
    }

    public static readonly string[] KnownComponents = {
        "Core", "AI", "Memory", "Voice", "Tools", "Skills", "Plugins",
        "Automations", "Remote", "Database", "Mobile", "Overlay", "Knowledge",
        "Devices", "Integrations", "SelfDiagnostics", "Update"
    };

    public void Report(ComponentHealth health)
    {
        components[health.Name] = health with { At = health.At ?? DateTimeOffset.UtcNow };
        Changed?.Invoke(health);
    }

    public void ReportHealthy(string name, string? detail = null) =>
        Report(new ComponentHealth(name, HealthStatus.Healthy, detail));

    public void ReportDegraded(string name, string reason) =>
        Report(new ComponentHealth(name, HealthStatus.Degraded, reason));

    public void ReportUnhealthy(string name, string reason) =>
        Report(new ComponentHealth(name, HealthStatus.Unhealthy, reason));

    public void ReportUnsupported(string name, string reason) =>
        Report(new ComponentHealth(name, HealthStatus.Unsupported, reason));

    public ComponentHealth Get(string name) => components.TryGetValue(name, out var h) ? h : new ComponentHealth(name, HealthStatus.Starting);
    public IReadOnlyList<ComponentHealth> Snapshot() => components.Values.OrderBy(c => c.Name).ToList();

    public HealthStatus Overall
    {
        get
        {
            var states = components.Values.Select(v => v.Status).ToList();
            if (states.Any(s => s == HealthStatus.Unhealthy)) return HealthStatus.Unhealthy;
            if (states.Any(s => s == HealthStatus.Degraded)) return HealthStatus.Degraded;
            if (states.All(s => s == HealthStatus.Disabled || s == HealthStatus.Unsupported)) return HealthStatus.Disabled;
            if (states.Any(s => s == HealthStatus.Starting)) return HealthStatus.Starting;
            return HealthStatus.Healthy;
        }
    }
}
