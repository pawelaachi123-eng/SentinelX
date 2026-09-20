namespace SentinelX.Services.Intent;

/// <summary>Deterministic tools first. The language model cannot execute arbitrary commands.</summary>
public sealed class IntentRouter(SentinelToolboxService toolbox, Services.Files.IFileService files,
    CommandRouter router, SystemMonitor monitor, ActionHistoryService history) : IIntentRouter
{
    public async Task<string> ProcessAsync(string input, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string normalized = ConversationMemoryService.Normalize(input).TrimEnd('?', '.', '!', ' ');
        string? read = ReadMetric(normalized, input);
        if (read != null) return read;
        string? file = await files.ProcessAsync(input, token);
        if (file != null) return file;
        var result = await toolbox.ProcessAsync(input, token);
        return result.Handled ? result.Response : await router.ProcessAsync(input, token);
    }
    private string? ReadMetric(string normalized, string input)
    {
        double value;
        string label, unit, source;
        switch (normalized)
        {
            case "ile mam ram": case "ile mam pamieci ram":
                value = monitor.GetTotalRamGB(); label = "Zainstalowana pamięć fizyczna"; unit = "GB"; source = "GlobalMemoryStatusEx"; break;
            case "ile uzywam ram": case "uzycie ram":
                value = monitor.GetUsedRamGB(); label = "Używana pamięć RAM"; unit = "GB"; source = "GlobalMemoryStatusEx"; break;
            case "uzycie cpu": case "ile uzywam cpu":
                value = monitor.GetCpuUsage(); label = "Użycie CPU"; unit = "%"; source = "GetSystemTimes"; break;
            case "uzycie gpu":
                value = monitor.GetGpuUsagePercent(); label = "Użycie GPU"; unit = "%"; source = "GPU Engine / Utilization Percentage"; break;
            default: return null;
        }
        string id = history.CreateActionId();
        var result = double.IsFinite(value)
            ? ActionExecutionResult.VerifiedSuccess($"{label}: {value:F1} {unit}.", $"{source}; odczyt {DateTime.Now:O}; wartość {value:R} {unit}")
            : ActionExecutionResult.Failure($"{label}: pomiar niedostępny. Poczekaj na kolejny odczyt.", source);
        history.AddResult(id, "READ_METRIC", input, result);
        return result.Message;
    }
}
