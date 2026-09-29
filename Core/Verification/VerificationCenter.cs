using SentinelX.Models;
using SentinelX.Services.History;

namespace SentinelX.Core;

/// <summary>
/// 0.99 · CENTRUM WERYFIKACJI — zasada „NO SUCCESS = NO PASS”.
/// Niezależny sąd nad dowodami akcji: nawet gdy narzędzie zgłosiło VERIFIED,
/// post-kondycje mogą to obalić i zdjąć status sukcesu. Reguły są rejestrowane
/// wg prefiksu typu akcji („*” = wszystkie), więc każda nowa rodzina narzędzi
/// dopisuje swoje sprawdzenie zamiast omijać zasadę. Zwracany string = powód
/// obalenia sukcesu; null = sprawdzenie przeszło.
/// </summary>
public sealed class VerificationCenter
{
    /// <summary>Post-kondycja: (polecenie, typ akcji, dowody) → powód obalenia lub null.</summary>
    public delegate string? PostCondition(string userRequest, string actionType, IReadOnlyList<ActionHistoryEntry> entries);

    private static readonly object gate = new();
    private static List<(string Match, PostCondition Check)> checks = [];

    static VerificationCenter()
    {
        RegisterDefaults();
    }

    /// <summary>Rejestruje warunek dla typów zaczynających się od <paramref name="match"/> („*” = wszystkie).</summary>
    public static void Register(string match, PostCondition check)
    {
        lock (gate)
        {
            checks = [.. checks, (match, check)];
        }
    }

    public static int RegisteredCount
    {
        get { lock (gate) return checks.Count; }
    }

    /// <summary>Sąd nad dowodami: (czy sukces udowodniony, powody obalenia).</summary>
    public static (bool Passed, List<string> Findings) Evaluate(
        string userRequest, string actionType, IReadOnlyList<ActionHistoryEntry> entries)
    {
        List<string> findings = [];
        (string, PostCondition)[] snapshot;
        lock (gate)
        {
            snapshot = [.. checks];
        }
        foreach ((string match, PostCondition check) in snapshot)
        {
            if (match != "*" && !actionType.StartsWith(match, StringComparison.Ordinal))
            {
                continue;
            }
            try
            {
                if (check(userRequest, actionType, entries) is { Length: > 0 } reason)
                {
                    findings.Add(reason);
                }
            }
            catch (Exception ex)
            {
                findings.Add("sprawdzenie rzuciło wyjątek (liczone przeciwko sukcesowi): " + ex.Message);
            }
        }
        return (findings.Count == 0, findings);
    }

    private static void RegisterDefaults()
    {
        // 1. WERDYKT BEZ DOWODU: status VERIFIED z pustym dowodem nie jest sukcesem.
        Register("*", (_, _, entries) =>
        {
            List<string> empty = entries
                .Where(x => x.Status == "VERIFIED" && string.IsNullOrWhiteSpace(x.Evidence))
                .Select(x => x.ActionId + " (" + x.ActionType + ")")
                .ToList();
            return empty.Count == 0 ? null : "VERIFIED bez dowodu: " + string.Join(", ", empty);
        });

        // 2. LICZBA BEZ POMIARU: jednostka (%, GB, MB) bez jakiejkolwiek cyfry = nieudowodniony odczyt.
        Register("*", (_, _, entries) =>
        {
            foreach (ActionHistoryEntry entry in entries)
            {
                string text = entry.Message + " " + entry.Evidence;
                bool claimsUnit = text.Contains('%') || text.Contains("GB") || text.Contains("MB");
                if (claimsUnit && !text.Any(char.IsDigit))
                {
                    return entry.ActionId + ": podaje jednostkę bez żadnej liczby — pomiar nieudowodniony";
                }
            }
            return null;
        });

        // 3. PRZEKONYWAJĄCY WERDYKT: „VERIFIED” z komunikatem o błędzie to sprzeczność.
        Register("*", (_, _, entries) =>
        {
            List<string> mixed = entries
                .Where(x => x.Status == "VERIFIED"
                    && (x.Message.Contains("błąd", StringComparison.OrdinalIgnoreCase)
                        || x.Message.Contains("nie udało", StringComparison.OrdinalIgnoreCase)))
                .Select(x => x.ActionId)
                .ToList();
            return mixed.Count == 0 ? null : "VERIFIED z komunikatem błędu: " + string.Join(", ", mixed);
        });

        // 4. SEKWENCJA NIEDOKOŃCZONA: WORKFLOW z mniej niż 2 narzędziami nie jest sekwencją.
        Register("WORKFLOW", (_, _, entries) =>
            entries.Count >= 2 ? null : "WORKFLOW z " + entries.Count + " narzędziami — sekwencja niedokończona");

        // 5. CZĘŚCIOWA PRAWDA: w sekwencji nie może obok siebie być VERIFIED i FAILED.
        Register("WORKFLOW", (_, _, entries) =>
        {
            bool hasVerified = entries.Any(x => x.Status == "VERIFIED");
            bool hasFailed = entries.Any(x => x.Status == "FAILED");
            return hasVerified && hasFailed ? "sekwencja łączy VERIFIED i FAILED — całość nie może być sukcesem" : null;
        });

        // 6. POMIAR BEZ LICZBY W DOWODZIE: MEASURE_* musi mieć cyfrę w DOWODZIE (nie tylko w komunikacie).
        Register("MEASURE_", (_, _, entries) =>
        {
            List<string> weak = entries
                .Where(x => x.Status == "VERIFIED" && !x.Evidence.Any(char.IsDigit))
                .Select(x => x.ActionId)
                .ToList();
            return weak.Count == 0 ? null : "pomiar bez liczby w dowodzie: " + string.Join(", ", weak);
        });

        // 7. DOWÓD ZBYT UBOGI: OPEN_*/CLOSE_* krótszy niż 8 znaków nie potwierdza operacji okna/procesu.
        Register("OPEN_", (_, _, entries) =>
        {
            List<string> thin = entries.Where(x => x.Status == "VERIFIED" && (x.Evidence ?? "").Trim().Length < 8)
                .Select(x => x.ActionId).ToList();
            return thin.Count == 0 ? null : "dowód zbyt ubogi, by potwierdzić operację okna/procesu: " + string.Join(", ", thin);
        });
        Register("CLOSE_", (_, _, entries) =>
        {
            List<string> thin = entries.Where(x => x.Status == "VERIFIED" && (x.Evidence ?? "").Trim().Length < 8)
                .Select(x => x.ActionId).ToList();
            return thin.Count == 0 ? null : "dowód zbyt ubogi, by potwierdzić zamknięcie procesu: " + string.Join(", ", thin);
        });

        // 8. SPRZĄTANIE BEZ LICZBY: CLEANUP musi kwantyfikować, CO uwolnił.
        Register("CLEANUP", (_, _, entries) =>
        {
            List<string> vague = entries
                .Where(x => x.Status == "VERIFIED" && !x.Evidence.Any(char.IsDigit))
                .Select(x => x.ActionId).ToList();
            return vague.Count == 0 ? null : "sprzątanie bez liczby uwolnionych danych: " + string.Join(", ", vague);
        });
    }
}
