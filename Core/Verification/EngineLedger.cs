using SentinelX.Models;

namespace SentinelX.Core;

/// <summary>
/// 0.99 · KSIĘGA ZDROWIA SILNIKA — liczniki sesji w jednym miejscu (thread-safe).
/// Silnik księguje każdy wynik (Verified/Unverified/Failed/Cancelled + flagi
/// namysłu i obalenia), a UI (karta zaufania, pasek statusu) czyta procent
/// udowodnionych sukcesów NA ŻYWO — bez sięgania do dysku. Pusta księga nie
/// udaje 100%: HasData = false, bo no success = no pass dotyczy też statystyk.
/// </summary>
public sealed class EngineLedger
{
    private readonly object gate = new();
    private int executed, verified, unverified, failed, cancelled, retried, downgraded;
    private readonly Dictionary<string, int> unverifiedByType = new(StringComparer.Ordinal);

    /// <summary>Zmiana po każdym zaksięgowaniu — UI się odświeża.</summary>
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

    /// <summary>Procent udowodnionych sukcesów zakończonych akcji (bez Waiting/Cancelled).</summary>
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

    /// <summary>Typy akcji NAJCZĘŚCIEJ kończące bez dowodu — „co poprawić w pytaniach”.</summary>
    public List<string> TopUnverifiedTypes(int count)
    {
        lock (gate)
        {
            return [.. unverifiedByType.OrderByDescending(x => x.Value).Take(count)
                .Select(x => x.Key + " ×" + x.Value)];
        }
    }
}
