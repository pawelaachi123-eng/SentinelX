using SentinelX.Services.History;

namespace SentinelX.Core;

/// <summary>
/// 0.99 · PODSUMOWANIE ZAUFANIA — ile zakończonych akcji realnie udowodniło sukces.
/// Liczone z historii (no success = no pass): VERIFIED się liczy, UNVERIFIED/FAILED
/// pokazują wprost. Pusta historia NIE daje 100% — daje „brak danych”, bo ocenianie
/// na zapas byłoby kłamstwem w duchu, przed którym ta zasada walczy.
/// </summary>
public sealed record TrustReport(int Percent, int Verified, int Total, int Unverified, int Failed)
{
    public bool HasData => Total > 0;
}

public static class TrustSummary
{
    private static readonly string[] Finished =
        ["VERIFIED", "UNVERIFIED", "FAILED", "CANCELLED", "EXPIRED", "INTERRUPTED"];

    public static TrustReport Calculate(IReadOnlyList<ActionHistoryEntry> entries)
    {
        List<ActionHistoryEntry> finished = entries
            .Where(x => Finished.Contains(x.Status, StringComparer.Ordinal))
            .ToList();
        int verified = finished.Count(x => x.Status == "VERIFIED");
        int unverified = finished.Count(x => x.Status == "UNVERIFIED");
        int failed = finished.Count(x => x.Status is "FAILED" or "CANCELLED" or "EXPIRED" or "INTERRUPTED");
        int percent = finished.Count == 0 ? 0 : (int)Math.Round(100.0 * verified / finished.Count);
        return new TrustReport(percent, verified, finished.Count, unverified, failed);
    }
}
