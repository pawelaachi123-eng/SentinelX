using System.Linq;
using System.Collections.Concurrent;

namespace SentinelX.Services.SelfDiagnostics;

/// <summary>
/// Klasyfikuje błędy, zbiera diagnostykę dla odpowiedniego komponentu, szuka znanego fixa,
/// proponuje rozwiązanie i pyta użytkownika o zgodę przed wykonaniem naprawy.
/// </summary>
public sealed class TroubleshootingEngine
{
    private readonly SelfDiagnosticsService diag;
    private readonly List<KnownFix> knownFixes = new();

    public TroubleshootingEngine(SelfDiagnosticsService diag)
    {
        this.diag = diag;
        RegisterDefaults();
    }

    public void Register(KnownFix fix) => knownFixes.Add(fix);

    public async Task<TroubleshootingProposal?> ClassifyAndProposeAsync(Exception error, CancellationToken token)
    {
        var key = (error.GetType().Name + " " + error.Message).ToLowerInvariant();
        var findings = await diag.RunAsync(token).ConfigureAwait(false);
        KnownFix? best = null;
        int bestScore = 0;
        foreach (var f in knownFixes)
        {
            int score = f.Triggers.Sum(t => key.Contains(t, StringComparison.OrdinalIgnoreCase) ? 1 : 0);
            if (score > bestScore) { bestScore = score; best = f; }
        }
        return best == null
            ? new TroubleshootingProposal(error.Message, findings, null, null, needsApproval: true)
            : new TroubleshootingProposal(error.Message, findings, best.Description, best.FixAction, best.RequiresApproval);
    }

    private void RegisterDefaults()
    {
        Register(new KnownFix("Brak wolnego miejsca",
            new[] { "disk", "space", "no space", "miejsce", "brak miejsca" },
            "Wyczyść folder Cache i stare logi.",
            async ct =>
            {
                try
                {
                    var cache = AppPaths.CacheDirectory;
                    if (Directory.Exists(cache)) Directory.Delete(cache, true);
                    Directory.CreateDirectory(cache);
                    return await Task.FromResult(true).ConfigureAwait(false);
                }
                catch { return false; }
            }, requiresApproval: true));
        Register(new KnownFix("Restart silnika AI",
            new[] { "ai", "llama", "engine", "model" },
            "Spróbuj zatrzymać i uruchomić silnik AI.", null, requiresApproval: true));
    }
}

public sealed record KnownFix(string Description, string[] Triggers, string FixDescription,
    Func<CancellationToken, Task<bool>>? FixAction, bool RequiresApproval);

public sealed record TroubleshootingProposal(
    string Error,
    IReadOnlyList<DiagnosticFinding> Findings,
    string? ProposedFixDescription,
    Func<CancellationToken, Task<bool>>? FixAction,
    bool NeedsApproval);
