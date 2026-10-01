using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SentinelX;

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
        Register(new KnownFix(
            FixDescription: "Brak wolnego miejsca",
            Triggers: new[] { "disk", "space", "no space", "miejsce", "brak miejsca" },
            Description: "Wyczyść folder Cache i stare logi.",
            FixAction: async ct =>
            {
                try
                {
                    var cache = AppPaths.CacheDirectory;
                    if (Directory.Exists(cache)) Directory.Delete(cache, true);
                    Directory.CreateDirectory(cache);
                    return await Task.FromResult(true).ConfigureAwait(false);
                }
                catch { return false; }
            },
            RequiresApproval: true));
        Register(new KnownFix(
            FixDescription: "Restart silnika AI",
            Triggers: new[] { "ai", "llama", "engine", "model" },
            Description: "Spróbuj zatrzymać i uruchomić silnik AI.",
            FixAction: null,
            RequiresApproval: true));
    }
}

public sealed record KnownFix(string FixDescription, string[] Triggers, string Description,
    Func<CancellationToken, Task<bool>>? FixAction, bool RequiresApproval);

public sealed record TroubleshootingProposal(
    string Error,
    IReadOnlyList<DiagnosticFinding> Findings,
    string? ProposedFixDescription,
    Func<CancellationToken, Task<bool>>? FixAction,
    bool NeedsApproval);
