namespace SentinelX.Core;

/// <summary>
/// 0.99 · NAMYSŁ NAD PONOWIENIEM — decyduje, czy niepowodzenie WYGLĄDA na chwilowe.
/// Auto-retry jest uczciwe tylko wtedy, gdy przyczyna może zniknąć sama (limit czasu,
/// zajęty zasób, sieć, usługa niedostępna). Błędy trwałe (brak pliku, odmowa dostępu,
/// zła składnia) NIE są ponawiane — ponawianie ich byłoby udawaniem myślenia.
/// Zwracany label = krótka nazwa rodziny przyczyny; null = nie ponawiaj.
/// </summary>
public static class RetryAdvisor
{
    private static readonly (string Label, string[] Cues)[] Permanent =
    [
        ("odmowa dostępu", ["odmowa", "access denied", "unauthorized", "brak uprawnień", "denied"]),
        ("nie znaleziono", ["nie znaleziono", "not found", "nie istnieje", "does not exist", "404"]),
        ("zła składnia", ["składnia", "syntax", "invalid argument", "nieprawidłowy argument", "nieprawidłowe polecenie"]),
    ];

    private static readonly (string Label, string[] Cues)[] Transient =
    [
        ("limit czasu", ["timeout", "timed out", "przekroczono czas", "limit czasu"]),
        ("zasób zajęty", ["zajęt", "busy", "locked", "zablokowan", "w użyciu", "in use"]),
        ("sieć", ["sieci", "sieć", "network", "connection", "połączen", "socket", "gniazdo"]),
        ("usługa chwilowo niedostępna", ["chwilowo", "temporarily", "unavailable", "niedostępn", "busy service", "usługa niedostępna"]),
    ];

    /// <summary>Krótki label chwilowej przyczyny albo null, gdy ponowienie nie ma sensu.</summary>
    public static string? TransientCause(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }
        foreach ((string label, string[] cues) in Permanent)
        {
            if (cues.Any(cue => message.Contains(cue, StringComparison.OrdinalIgnoreCase)))
            {
                return null; // trwałe: ponowienie niczego nie zmieni
            }
        }
        foreach ((string label, string[] cues) in Transient)
        {
            if (cues.Any(cue => message.Contains(cue, StringComparison.OrdinalIgnoreCase)))
            {
                return label;
            }
        }
        return null;
    }

    /// <summary>Rosnące oczekiwanie między próbami: 350 ms, sufit 1200 ms.</summary>
    public static int BackoffMilliseconds(int attempt) => Math.Min(1200, 350 * Math.Max(1, attempt));
}
