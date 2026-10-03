using System.Text.RegularExpressions;

namespace SentinelX.Services.Automation;

/// <summary>
/// Validates and dispatches typed automation actions. Unknown action IDs are always rejected;
/// there is no command-line or arbitrary process-execution fallback.
/// </summary>
public sealed class AutomationActionRegistry
{
    private static readonly Regex ValidId = new("^[a-z][a-z0-9-]{1,47}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly IReadOnlyDictionary<string, IAutomationActionHandler> handlers;

    public AutomationActionRegistry(IEnumerable<IAutomationActionHandler> handlers)
    {
        var map = new Dictionary<string, IAutomationActionHandler>(StringComparer.OrdinalIgnoreCase);
        foreach (IAutomationActionHandler handler in handlers)
        {
            string id = handler.Descriptor.Id;
            if (!ValidId.IsMatch(id) || !map.TryAdd(id, handler))
                throw new InvalidOperationException("Automation action IDs must be unique, lowercase identifiers.");
        }
        this.handlers = map;
    }

    public IReadOnlyList<AutomationActionDescriptor> GetAvailableActions() =>
        handlers.Values.Select(x => x.Descriptor).OrderBy(x => x.Name, StringComparer.CurrentCulture).ToArray();

    public bool TryNormalize(AutomationStep? action, out AutomationStep normalized, out string error)
    {
        normalized = new("", "");
        if (action == null || !handlers.TryGetValue(action.ActionId ?? "", out IAutomationActionHandler? handler))
        {
            error = "Nieznany typ akcji. Dostępne są wyłącznie akcje pokazane w katalogu automatyzacji.";
            return false;
        }
        if (!handler.TryValidate(action.Parameter, out string parameter, out error)) return false;
        normalized = new(handler.Descriptor.Id, parameter);
        return true;
    }

    public async Task<AutomationActionResult> ExecuteAsync(AutomationStep action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryNormalize(action, out AutomationStep normalized, out string error))
            return new(false, "FAILED", error);
        IAutomationActionHandler handler = handlers[normalized.ActionId];
        return await handler.ExecuteAsync(normalized.Parameter, cancellationToken).ConfigureAwait(false);
    }
}
