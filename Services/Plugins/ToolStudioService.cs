using System.Collections.Concurrent;
using System.Threading;

namespace SentinelX.Services.Plugins;

/// <summary>
/// „Smart Tool Creation" dla DEVELOPER MODE.
/// Tryb produkcyjny: blokada generowania i wykonywania dowolnego kodu.
/// Tryb dev: przejrzysty pipeline need → search → design → code → tests → build → tests → approval → register.
/// Bez zgody użytkownika nic nie jest rejestrowane ani wykonywane.
/// </summary>
public sealed class ToolStudioService
{
    private readonly PluginRegistry registry;
    private readonly Capabilities.CapabilityGraph capabilities;
    private readonly ConcurrentDictionary<string, ToolDraft> drafts = new();
    public bool DeveloperModeEnabled { get; set; }

    public ToolStudioService(PluginRegistry registry, Capabilities.CapabilityGraph capabilities)
    {
        this.registry = registry;
        this.capabilities = capabilities;
    }

    public ToolDesignResult DesignTool(string need)
    {
        if (!DeveloperModeEnabled) return ToolDesignResult.Disabled(
            "Tworzenie narzędzi wymaga włączonego DEVELOPER MODE. Produkcyjny Sentinel nigdy nie generuje ani nie wykonuje dowolnego kodu.");
        // Sprawdź czy istniejące narzędzie już coś pokrywa
        var existing = registry.Tools.FirstOrDefault(t => need.Contains(t.Id, StringComparison.OrdinalIgnoreCase)
                                                       || t.Description.Contains(need, StringComparison.OrdinalIgnoreCase));
        var missing = capabilities.GetMissing().Select(m => m.Capability).ToList();
        return new ToolDesignResult(
            Need: need,
            ExistingTool: existing?.Id,
            MissingCapabilities: missing,
            Status: ToolDesignStatus.ReadyForApproval);
    }

    public ToolBuildResult BuildTool(ToolDraft draft, out string? testOutput)
    {
        testOutput = null;
        if (!DeveloperModeEnabled) return ToolBuildResult.Disabled();
        if (string.IsNullOrWhiteSpace(draft.Id) || !draft.Id.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '.'))
            return ToolBuildResult.Fail("Id musi być prostym identyfikatorem (litery/cyfry/_).");
        if (registry.Tools.Any(t => t.Id.Equals(draft.Id, StringComparison.OrdinalIgnoreCase)))
            return ToolBuildResult.Fail("Narzędzie o takim id już istnieje.");
        // W tej fazie generowanie kodu jest oznakowane jako unsupported (wymaga dedykowanego Roslyn/Skia pipeline).
        // Zamiast tego zwracamy jasny status — użytkownik musi dostarczyć implementację ręcznie albo przez AI
        // w następnej fazie. To jest zachowanie BEZPIECZNE i nie udaje, że coś działa.
        return ToolBuildResult.Unsupported(
            "Automatyczna generacja kodu C# i izolowana kompilacja wymagają integracji Roslyn. W tej wersji zarejestrowanie " +
            "nowego narzędzia możliwe jest przez PluginManager (wbudowany plugin) lub ręcznie w kodzie źródłowym.");
    }

    public bool RegisterApprovedTool(ToolDraft draft, Func<object?, CancellationToken, Task<object?>> handler)
    {
        if (!DeveloperModeEnabled) return false;
        var reg = new ToolRegistration(draft.Id, draft.DisplayName, draft.Description, draft.Category,
            Models.Plugins.PluginPermissions.RegisterTools, handler, draft.IsReadOnly, "tool-studio");
        registry.RegisterTool(reg);
        return true;
    }
}

public sealed record ToolDraft(string Id, string DisplayName, string Description, string Category, bool IsReadOnly);

public enum ToolDesignStatus { ExistingMatch, MissingCapabilities, ReadyForApproval, Disabled }
public sealed record ToolDesignResult(string Need, string? ExistingTool, IReadOnlyList<string> MissingCapabilities, ToolDesignStatus Status);

public enum ToolBuildStatus { Ok, Failed, Disabled, Unsupported }
public sealed record ToolBuildResult(ToolBuildStatus Status, string Message)
{
    public static ToolBuildResult Ok(string msg = "") => new(ToolBuildStatus.Ok, msg);
    public static ToolBuildResult Fail(string msg) => new(ToolBuildStatus.Failed, msg);
    public static ToolBuildResult Disabled() => new(ToolBuildStatus.Disabled, "Developer mode jest wyłączony.");
    public static ToolBuildResult Unsupported(string msg) => new(ToolBuildStatus.Unsupported, msg);
}
