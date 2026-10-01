using System.Linq;
using System.Collections.Concurrent;
using SentinelX.Models.Capabilities;

namespace SentinelX.Services.Capabilities;

/// <summary>
/// Graf możliwości systemu. Daje odpowiedź na pytania:
/// - co Sentinel aktualnie potrafi?
/// - który tool/skill/integration zapewnia daną możliwość?
/// - czego brakuje, by dana funkcja działała?
/// </summary>
public sealed class CapabilityGraph
{
    private readonly ConcurrentDictionary<string, CapabilityDescriptor> capabilities = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentBag<CapabilityEdge> edges = new();
    private readonly ConcurrentDictionary<string, List<string>> missingReasons = new();

    public void RegisterCapability(CapabilityDescriptor descriptor)
    {
        capabilities[descriptor.Id] = descriptor;
    }

    public void MarkMissing(string capabilityId, string reason)
    {
        missingReasons.AddOrUpdate(capabilityId,
            _ => new List<string> { reason },
            (_, existing) => { lock (existing) { existing.Add(reason); } return existing; });
        if (capabilities.TryGetValue(capabilityId, out var d))
            capabilities[capabilityId] = d with { IsMissing = true, MissingReason = reason };
    }

    public void RegisterProvider(string source, string capabilityId)
    {
        edges.Add(new CapabilityEdge { Source = source, Capability = capabilityId, Kind = "provides" });
    }

    public void RegisterRequirement(string source, string capabilityId)
    {
        edges.Add(new CapabilityEdge { Source = source, Capability = capabilityId, Kind = "requires" });
    }

    public IReadOnlyList<CapabilityDescriptor> All => capabilities.Values.ToList();

    public IReadOnlyList<string> ProvidersOf(string capabilityId) =>
        edges.Where(e => e.Capability.Equals(capabilityId, StringComparison.OrdinalIgnoreCase) && e.Kind == "provides")
             .Select(e => e.Source).Distinct().ToList();

    public IReadOnlyList<string> RequirementsOf(string source) =>
        edges.Where(e => e.Source.Equals(source, StringComparison.OrdinalIgnoreCase) && e.Kind == "requires")
             .Select(e => e.Capability).Distinct().ToList();

    public IReadOnlyList<string> MissingFor(string source) =>
        RequirementsOf(source).Where(c => capabilities.TryGetValue(c, out var d) && d.IsMissing).ToList();

    public IReadOnlyList<(string Capability, string Reason)> GetMissing() =>
        capabilities.Values.Where(d => d.IsMissing)
            .Select(d => (d.Id, d.MissingReason ?? "unknown")).ToList();
}
