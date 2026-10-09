using SentinelX.Models;

namespace SentinelX.Core;

/// <summary>
/// 0.99 ┬Ě KSI─śGA ZDROWIA SILNIKA ÔÇö liczniki sesji w jednym miejscu (thread-safe).
/// Silnik ksi─Öguje ka┼╝dy wynik (Verified/Unverified/Failed/Cancelled + flagi
/// namys┼éu i obalenia), a UI (karta zaufania, pasek statusu) czyta procent
/// udowodnionych sukces├│w NA ┼╗YWO ÔÇö bez si─Ögania do dysku. Pusta ksi─Öga nie
/// udaje 100%: HasData = false, bo no success = no pass dotyczy te┼╝ statystyk.
/// </summary>
public sealed class EngineLedger
{
    private readonly object gate = new();
    private int executed, verified, unverified, failed, cancelled, retried, downgraded;
    private readonly Dictionary<string, int> unverifiedByType = new(StringComparer.Ordinal);

    /// <summary>Zmiana po ka┼╝dym zaksi─Ögowaniu ÔÇö UI si─Ö od┼Ťwie┼╝a.</summary>
    public event Action? Changed;

    public void Record(ActionStatus status, string actionType, bool retriedAttempt, bool downgradedVerdict)
    {
        lock (gate)
        {
            executed++;
            switch (status)
            {
                case ActionStatus.Verified: verified++; break;
                case ActionStatus.Unverified:
                    unverified++;
                    string key = string.IsNullOrWhiteSpace(actionType) ? "REQUEST" : actionType;
                    unverifiedByType[key] = unverifiedByType.TryGetValue(key, out int n) ? n + 1 : 1;
                    break;
                case ActionStatus.Failed: failed++; break;
                case ActionStatus.Cancelled: cancelled++; break;
            }
            if (retriedAttempt) retried++;
            if (downgradedVerdict) downgraded++;
        }
        Changed?.Invoke();
    }

    public int Executed { get { lock (gate) return executed; } }
    public int Verified { get { lock (gate) return verified; } }
    public int Unverified { get { lock (gate) return unverified; } }
    public int Failed { get { lock (gate) return failed; } }
    public int Cancelled { get { lock (gate) return cancelled; } }
    public int Retried { get { lock (gate) return retried; } }
    public int Downgraded { get { lock (gate) return downgraded; } }

    public bool HasData
    {
        get { lock (gate) return verified + unverified + failed > 0; }
    }

    /// <summary>Procent udowodnionych sukces├│w zako┼äczonych akcji (bez Waiting/Cancelled).</summary>
    public int TrustPercent
    {
        get
        {
            lock (gate)
            {
                int finished = verified + unverified + failed;
                return finished == 0 ? 0 : (int)Math.Round(100.0 * verified / finished);
            }
        }
    }

    /// <summary>Typy akcji NAJCZ─ś┼ÜCIEJ ko┼äcz─ůce bez dowodu ÔÇö ÔÇ×co poprawi─ç w pytaniachÔÇŁ.</summary>
    public List<string> TopUnverifiedTypes(int count)
    {
        lock (gate)
        {
            return [.. unverifiedByType.OrderByDescending(x => x.Value).Take(count)
                .Select(x => x.Key + " ├Ś" + x.Value)];
        }
    }
}
