using System.Globalization;
using System.IO;
using SentinelX.Core;
namespace SentinelX.Services.Monitoring;

public sealed class ReadOnlyCommandService(SystemMonitor monitor, ActionHistoryService history)
{
    public string? Process(string input, CancellationToken token)
    {
        if (!ReadOnlyIntentCatalog.TryResolve(input, out var intent)) return null;
        token.ThrowIfCancellationRequested();
        ActionExecutionResult result;
        try { result = Read(intent, token); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { result = ActionExecutionResult.Failure("Odczyt jest niedostępny.", ex.Message); }
        history.AddResult(history.CreateActionId(), "READ_" + intent.ToString().ToUpperInvariant(), input, result);
        return result.Message;
    }
    private ActionExecutionResult Read(ReadOnlyIntent intent, CancellationToken token)
    {
        if (intent == ReadOnlyIntent.Disks)
        {
            var lines = new List<string>(); var errors = new List<string>();
            foreach (var drive in DriveInfo.GetDrives().Where(x => x.DriveType == DriveType.Fixed))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (!drive.IsReady) { errors.Add(drive.Name + ": niegotowy"); continue; }
                    lines.Add($"{drive.Name} wolne: {drive.TotalFreeSpace / 1073741824d:F1} GiB / {drive.TotalSize / 1073741824d:F1} GiB");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add(drive.Name + ": " + ex.Message); }
            }
            string message = string.Join("\n", lines.Concat(errors));
            string evidence = "DriveInfo.TotalFreeSpace / TotalSize; " + DateTimeOffset.Now.ToString("O");
            return lines.Count == 0 ? ActionExecutionResult.Failure("Brak dostępnych odczytów dysków.", message)
                : errors.Count > 0 ? ActionExecutionResult.UnverifiedSuccess(message, evidence + "; odczyt częściowy")
                : ActionExecutionResult.VerifiedSuccess(message, evidence + "\n" + message);
        }
        if (intent is ReadOnlyIntent.Clock or ReadOnlyIntent.Date)
        {
            var now = DateTimeOffset.Now;
            string message = intent == ReadOnlyIntent.Clock ? now.ToString("HH:mm:ss zzz") : now.ToString("dddd, d MMMM yyyy", CultureInfo.GetCultureInfo("pl-PL"));
            return ActionExecutionResult.VerifiedSuccess(message, $"Lokalny zegar systemowy DateTimeOffset.Now: {now:O}. Nie jest to test synchronizacji NTP.");
        }
        if (intent == ReadOnlyIntent.Uptime)
        {
            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            return ActionExecutionResult.VerifiedSuccess($"Czas pracy: {uptime.Days} dni, {uptime.Hours} godz., {uptime.Minutes} min.", $"Environment.TickCount64: {uptime.TotalMilliseconds:F0} ms.");
        }
        (double value, string name, string unit, string source) = intent switch
        {
            ReadOnlyIntent.RamTotal => (monitor.GetTotalRamGB(), "Zainstalowana pamięć", "GiB", "GlobalMemoryStatusEx"),
            ReadOnlyIntent.RamUsed => (monitor.GetUsedRamGB(), "Używana pamięć", "GiB", "GlobalMemoryStatusEx"),
            ReadOnlyIntent.RamFree => (monitor.GetAvailableRamGB(), "Dostępna pamięć", "GiB", "GlobalMemoryStatusEx.AvailablePhysical"),
            ReadOnlyIntent.RamPercent => (monitor.GetRamUsagePercent(), "Użycie RAM", "%", "GlobalMemoryStatusEx"),
            ReadOnlyIntent.Cpu => (monitor.GetCpuUsage(), "Użycie CPU", "%", "GetSystemTimes"),
            _ => (monitor.GetGpuUsagePercent(), "Użycie GPU", "%", "GPU Engine / Utilization Percentage")
        };
        return double.IsFinite(value)
            ? ActionExecutionResult.VerifiedSuccess($"{name}: {value:F1} {unit}.", $"{source}; {DateTimeOffset.Now:O}; wartość {value:R} {unit}.")
            : ActionExecutionResult.Failure($"{name}: pomiar niedostępny. Poczekaj na kolejny odczyt.", source);
    }
}
