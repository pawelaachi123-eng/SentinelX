using SentinelX.Models;
using SentinelX.Services.History;

namespace SentinelX.Core;

/// <summary>
/// 0.99 ┬Ě CENTRUM WERYFIKACJI ÔÇö zasada ÔÇ×NO SUCCESS = NO PASSÔÇŁ.
/// Niezale┼╝ny s─ůd nad dowodami akcji: nawet gdy narz─Ödzie zg┼éosi┼éo VERIFIED,
/// post-kondycje mog─ů to obali─ç i zdj─ů─ç status sukcesu. Regu┼éy s─ů rejestrowane
/// wg prefiksu typu akcji (ÔÇ×*ÔÇŁ = wszystkie), wi─Öc ka┼╝da nowa rodzina narz─Ödzi
/// dopisuje swoje sprawdzenie zamiast omija─ç zasad─Ö. Zwracany string = pow├│d
/// obalenia sukcesu; null = sprawdzenie przesz┼éo.
/// </summary>
public sealed class VerificationCenter
{
    /// <summary>Post-kondycja: (polecenie, typ akcji, dowody) Ôćĺ pow├│d obalenia lub null.</summary>
    public delegate string? PostCondition(string userRequest, string actionType, IReadOnlyList<ActionHistoryEntry> entries);

    private static readonly object gate = new();
    private static List<(string Match, PostCondition Check)> checks = [];

    static VerificationCenter()
    {
        RegisterDefaults();
    }

    /// <summary>Rejestruje warunek dla typ├│w zaczynaj─ůcych si─Ö od <paramref name="match"/> (ÔÇ×*ÔÇŁ = wszystkie).</summary>
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

    /// <summary>S─ůd nad dowodami: (czy sukces udowodniony, powody obalenia).</summary>
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
                findings.Add("sprawdzenie rzuci┼éo wyj─ůtek (liczone przeciwko sukcesowi): " + ex.Message);
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
                    return entry.ActionId + ": podaje jednostk─Ö bez ┼╝adnej liczby ÔÇö pomiar nieudowodniony";
                }
            }
            return null;
        });

        // 3. PRZEKONYWAJ─äCY WERDYKT: ÔÇ×VERIFIEDÔÇŁ z komunikatem o b┼é─Ödzie to sprzeczno┼Ť─ç.
        Register("*", (_, _, entries) =>
        {
            List<string> mixed = entries
                .Where(x => x.Status == "VERIFIED"
                    && (x.Message.Contains("b┼é─ůd", StringComparison.OrdinalIgnoreCase)
                        || x.Message.Contains("nie uda┼éo", StringComparison.OrdinalIgnoreCase)))
                .Select(x => x.ActionId)
                .ToList();
            return mixed.Count == 0 ? null : "VERIFIED z komunikatem b┼é─Ödu: " + string.Join(", ", mixed);
        });

        // 4. SEKWENCJA NIEDOKO┼âCZONA: WORKFLOW z mniej ni┼╝ 2 narz─Ödziami nie jest sekwencj─ů.
        Register("WORKFLOW", (_, _, entries) =>
            entries.Count >= 2 ? null : "WORKFLOW z " + entries.Count + " narz─Ödziami ÔÇö sekwencja niedoko┼äczona");

        // 5. CZ─ś┼ÜCIOWA PRAWDA: w sekwencji nie mo┼╝e obok siebie by─ç VERIFIED i FAILED.
        Register("WORKFLOW", (_, _, entries) =>
        {
            bool hasVerified = entries.Any(x => x.Status == "VERIFIED");
            bool hasFailed = entries.Any(x => x.Status == "FAILED");
            return hasVerified && hasFailed ? "sekwencja ┼é─ůczy VERIFIED i FAILED ÔÇö ca┼éo┼Ť─ç nie mo┼╝e by─ç sukcesem" : null;
        });

        // 6. POMIAR BEZ LICZBY W DOWODZIE: MEASURE_* musi mie─ç cyfr─Ö w DOWODZIE (nie tylko w komunikacie).
        Register("MEASURE_", (_, _, entries) =>
        {
            List<string> weak = entries
                .Where(x => x.Status == "VERIFIED" && !x.Evidence.Any(char.IsDigit))
                .Select(x => x.ActionId)
                .ToList();
            return weak.Count == 0 ? null : "pomiar bez liczby w dowodzie: " + string.Join(", ", weak);
        });

        // 7. DOW├ôD ZBYT UBOGI: OPEN_*/CLOSE_* kr├│tszy ni┼╝ 8 znak├│w nie potwierdza operacji okna/procesu.
        Register("OPEN_", (_, _, entries) =>
        {
            List<string> thin = entries.Where(x => x.Status == "VERIFIED" && (x.Evidence ?? "").Trim().Length < 8)
                .Select(x => x.ActionId).ToList();
            return thin.Count == 0 ? null : "dow├│d zbyt ubogi, by potwierdzi─ç operacj─Ö okna/procesu: " + string.Join(", ", thin);
        });
        Register("CLOSE_", (_, _, entries) =>
        {
            List<string> thin = entries.Where(x => x.Status == "VERIFIED" && (x.Evidence ?? "").Trim().Length < 8)
                .Select(x => x.ActionId).ToList();
            return thin.Count == 0 ? null : "dow├│d zbyt ubogi, by potwierdzi─ç zamkni─Öcie procesu: " + string.Join(", ", thin);
        });

        // 8. SPRZ─äTANIE BEZ LICZBY: CLEANUP musi kwantyfikowa─ç, CO uwolni┼é.
        Register("CLEANUP", (_, _, entries) =>
        {
            List<string> vague = entries
                .Where(x => x.Status == "VERIFIED" && !x.Evidence.Any(char.IsDigit))
                .Select(x => x.ActionId).ToList();
            return vague.Count == 0 ? null : "sprz─ůtanie bez liczby uwolnionych danych: " + string.Join(", ", vague);
        });
        // 9. TELEFON BEZ SYGNA┼üU: CALL_ VERIFIED wymaga OFFHOOK (prawdziwy stan z telefonu) i numeru w dowodzie.
        Register("CALL_", (_, _, entries) =>
        {
            List<string> blind = entries
                .Where(x => x.Status == "VERIFIED" && (!x.Evidence.Contains("OFFHOOK", StringComparison.Ordinal)
                    || !System.Text.RegularExpressions.Regex.IsMatch(x.Evidence, @"\d{9,}")))
                .Select(x => x.ActionId).ToList();
            return blind.Count == 0 ? null : "rozmowa VERIFIED bez sygna┼éu OFFHOOK/numeru z telefonu (po┼é─ůczenie niepotwierdzone): " + string.Join(", ", blind);
        });
    }
}
