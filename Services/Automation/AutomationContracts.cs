using System.Globalization;
using SentinelX.Models;

namespace SentinelX.Services.Automation;

/// <summary>Only triggers implemented by the local scheduler are offered in the UI.</summary>
public enum AutomationTriggerKind
{
    Manual,
    ApplicationStartup,
    DailySchedule
}

/// <summary>A declarative, non-shell action. Parameters are validated again immediately before execution.</summary>
public sealed record AutomationStep(string ActionId, string Parameter);

/// <summary>
/// A user-created automation. There is deliberately no shell/PowerShell action in this schema.
/// </summary>
public sealed record AutomationRule
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public AutomationTriggerKind Trigger { get; init; } = AutomationTriggerKind.Manual;
    /// <summary>Local 24-hour time in HH:mm format; used only by DailySchedule.</summary>
    public string ScheduleTime { get; init; } = "";
    public bool Enabled { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastRunAt { get; init; }
    public string LastRunStatus { get; init; } = "";
    public string LastRunMessage { get; init; } = "";
    public long LastRunDurationMilliseconds { get; init; }
    /// <summary>Local date key; prevents a daily rule from firing more than once per date.</summary>
    public string LastScheduledDate { get; init; } = "";
    public IReadOnlyList<AutomationStep> Actions { get; init; } = Array.Empty<AutomationStep>();
}

public sealed record AutomationRunRecord
{
    public string Id { get; init; } = "";
    public string RuleId { get; init; } = "";
    public string RuleName { get; init; } = "";
    public string Trigger { get; init; } = "";
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset FinishedAt { get; init; }
    public long DurationMilliseconds { get; init; }
    public string Status { get; init; } = "";
    public string Result { get; init; } = "";
    public string Error { get; init; } = "";
    public int CompletedActions { get; init; }
    public int TotalActions { get; init; }
}

public sealed record AutomationRunOutcome(bool Started, string Status, string Message, AutomationRunRecord? Record = null);

/// <summary>Capabilities shown to the user before a rule is saved.</summary>
public sealed record AutomationActionDescriptor(string Id, string Name, string Category, string Description, string RequiredPermission);

public sealed record AutomationActionResult(bool Success, string Status, string Message, string Evidence = "");

/// <summary>One handler per action type; adding an action never requires another central command switch.</summary>
public interface IAutomationActionHandler
{
    AutomationActionDescriptor Descriptor { get; }
    bool TryValidate(string? parameter, out string normalized, out string error);
    Task<AutomationActionResult> ExecuteAsync(string normalizedParameter, CancellationToken cancellationToken);
}

public sealed record AutomationTriggerOption(AutomationTriggerKind Kind, string Label, string Help)
{
    public override string ToString() => Label;
}

public sealed record AutomationActionOption(string Id, string Label, string Description)
{
    public override string ToString() => Label;
}

internal sealed class AutomationStore
{
    public AutomationStore() { }
    public int Version { get; set; } = 1;
    public List<AutomationRule> Rules { get; set; } = [];
    public List<AutomationRunRecord> History { get; set; } = [];
}

internal static class AutomationValidation
{
    public static bool IsValidScheduleTime(string? value, out string normalized)
    {
        normalized = "";
        if (!TimeOnly.TryParseExact(value?.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time))
            return false;
        normalized = time.ToString("HH:mm", CultureInfo.InvariantCulture);
        return true;
    }
}
