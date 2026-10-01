using System.Collections.Concurrent;
using System.Threading;

namespace SentinelX.Services.Plugins;

/// <summary>
/// Rejestr rozszerzeń dostarczanych przez pluginy / wbudowane komponenty:
/// tools, skills, widgets, integrations, automation triggers.
/// Dostęp jest sprawdzany przez uprawnienia plugina.
/// </summary>
public sealed class PluginRegistry
{
    private readonly ConcurrentDictionary<string, ToolRegistration> tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SkillRegistration> skills = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, WidgetRegistration> widgets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IntegrationRegistration> integrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TriggerRegistration> triggers = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterTool(ToolRegistration reg) => tools[reg.Id] = reg;
    public void RegisterSkill(SkillRegistration reg) => skills[reg.Id] = reg;
    public void RegisterWidget(WidgetRegistration reg) => widgets[reg.Id] = reg;
    public void RegisterIntegration(IntegrationRegistration reg) => integrations[reg.ProviderId] = reg;
    public void RegisterTrigger(TriggerRegistration reg) => triggers[reg.Id] = reg;

    public bool TryGetTool(string id, out ToolRegistration? reg) => tools.TryGetValue(id, out reg);
    public bool TryGetSkill(string id, out SkillRegistration? reg) => skills.TryGetValue(id, out reg);
    public IReadOnlyList<ToolRegistration> Tools => tools.Values.ToList();
    public IReadOnlyList<SkillRegistration> Skills => skills.Values.ToList();
    public IReadOnlyList<WidgetRegistration> Widgets => widgets.Values.ToList();
    public IReadOnlyList<IntegrationRegistration> Integrations => integrations.Values.ToList();
    public IReadOnlyList<TriggerRegistration> Triggers => triggers.Values.ToList();
}

public sealed record ToolRegistration(string Id, string DisplayName, string Description,
    string Category, Models.Plugins.PluginPermissions RequiredPermission, Func<object?, CancellationToken, Task<object?>> Handler,
    bool IsReadOnly = false, string? SourcePluginId = null);

public sealed record SkillRegistration(string Id, string DisplayName, string Description,
    IReadOnlyList<string> RequiredCapabilities, string? SourcePluginId = null);

public sealed record WidgetRegistration(string Id, string DisplayName, string? SourcePluginId = null);
public sealed record IntegrationRegistration(string ProviderId, string DisplayName, string? SourcePluginId = null);
public sealed record TriggerRegistration(string Id, string DisplayName, string Description, string? SourcePluginId = null);
