using System.Collections.Concurrent;
 using System.IO; using System.Threading;
using SentinelX.Services.Health;

namespace SentinelX.Services.SelfDiagnostics;

/// <summary>
/// Implementuje polecenie „Sentinel sprawdź siebie" — przegląda wszystkie komponenty,
/// uruchamia szybkie testy (config, baza, AI, głos, narzędzia, pluginy, remote, workerzy, logi).
/// </summary>
public sealed class SelfDiagnosticsService
{
    private readonly SentinelHealthService health;
    private readonly IEnumerable<IDiagnosticCheck> checks;

    public SelfDiagnosticsService(SentinelHealthService health, IEnumerable<IDiagnosticCheck> checks)
    {
        this.health = health;
        this.checks = checks;
    }

    public async Task<IReadOnlyList<DiagnosticFinding>> RunAsync(CancellationToken token)
    {
        var findings = new List<DiagnosticFinding>();
        foreach (var ch in checks)
        {
            try
            {
                var f = await ch.RunAsync(token).ConfigureAwait(false);
                findings.AddRange(f);
            }
            catch (Exception ex)
            {
                findings.Add(new DiagnosticFinding(ch.Category, DiagnosticSeverity.Error, ex.Message));
            }
        }
        return findings;
    }
}

public interface IDiagnosticCheck
{
    string Category { get; }
    Task<IReadOnlyList<DiagnosticFinding>> RunAsync(CancellationToken token);
}

public enum DiagnosticSeverity { Info, Ok, Warning, Error }
public sealed record DiagnosticFinding(string Category, DiagnosticSeverity Severity, string Message, string? Help = null);

/// <summary>Podstawowy sprawdzacz konfiguracji.</summary>
public sealed class ConfigDiagnosticCheck : IDiagnosticCheck
{
    private readonly AppSettingsService settings;
    public ConfigDiagnosticCheck(AppSettingsService s) => settings = s;
    public string Category => "Configuration";
    public Task<IReadOnlyList<DiagnosticFinding>> RunAsync(CancellationToken token)
    {
        var result = new List<DiagnosticFinding>();
        if (!string.IsNullOrWhiteSpace(settings.LastError))
            result.Add(new DiagnosticFinding(Category, DiagnosticSeverity.Error, "Błąd ustawień: " + settings.LastError));
        else
            result.Add(new DiagnosticFinding(Category, DiagnosticSeverity.Ok, "Ustawienia są poprawne."));
        return Task.FromResult<IReadOnlyList<DiagnosticFinding>>(result);
    }
}

/// <summary>Podstawowy sprawdzacz plików i katalogów.</summary>
public sealed class StorageDiagnosticCheck : IDiagnosticCheck
{
    public string Category => "Storage";
    public Task<IReadOnlyList<DiagnosticFinding>> RunAsync(CancellationToken token)
    {
        var result = new List<DiagnosticFinding>();
        try
        {
            foreach (var dir in new[] { AppPaths.SettingsDirectory, AppPaths.LogsDirectory, AppPaths.HistoryDirectory, AppPaths.MemoryDirectory, AppPaths.CacheDirectory })
            {
                try { Directory.CreateDirectory(dir); }
                catch (Exception ex) { result.Add(new DiagnosticFinding(Category, DiagnosticSeverity.Error, $"Nie można utworzyć {dir}: {ex.Message}")); }
            }
            var drive = new DriveInfo(Path.GetPathRoot(AppPaths.Root) ?? ".");
            long free = drive.AvailableFreeSpace;
            if (free < 1024L * 1024 * 1024)
                result.Add(new DiagnosticFinding(Category, DiagnosticSeverity.Warning, $"Mało miejsca na dysku: {free / 1024 / 1024} MB wolnych."));
            else
                result.Add(new DiagnosticFinding(Category, DiagnosticSeverity.Ok, $"Dysk OK — {free / 1024 / 1024 / 1024} GB wolnych."));
        }
        catch (Exception ex) { result.Add(new DiagnosticFinding(Category, DiagnosticSeverity.Error, ex.Message)); }
        return Task.FromResult<IReadOnlyList<DiagnosticFinding>>(result);
    }
}
