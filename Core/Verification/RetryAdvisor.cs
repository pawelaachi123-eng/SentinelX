namespace SentinelX.Core;

/// <summary>
/// 0.99 ┬Ě NAMYS┼ü NAD PONOWIENIEM ÔÇö decyduje, czy niepowodzenie WYGL─äDA na chwilowe.
/// Auto-retry jest uczciwe tylko wtedy, gdy przyczyna mo┼╝e znikn─ů─ç sama (limit czasu,
/// zaj─Öty zas├│b, sie─ç, us┼éuga niedost─Öpna). B┼é─Ödy trwa┼ée (brak pliku, odmowa dost─Öpu,
/// z┼éa sk┼éadnia) NIE s─ů ponawiane ÔÇö ponawianie ich by┼éoby udawaniem my┼Ťlenia.
/// Zwracany label = kr├│tka nazwa rodziny przyczyny; null = nie ponawiaj.
/// </summary>
public static class RetryAdvisor
{
    private static readonly (string Label, string[] Cues)[] Permanent =
    [
        ("odmowa dost─Öpu", ["odmowa", "access denied", "unauthorized", "brak uprawnie┼ä", "denied"]),
        ("nie znaleziono", ["nie znaleziono", "not found", "nie istnieje", "does not exist", "404"]),
        ("z┼éa sk┼éadnia", ["sk┼éadnia", "syntax", "invalid argument", "nieprawid┼éowy argument", "nieprawid┼éowe polecenie"]),
    ];

    private static readonly (string Label, string[] Cues)[] Transient =
    [
        ("limit czasu", ["timeout", "timed out", "przekroczono czas", "limit czasu"]),
        ("zas├│b zaj─Öty", ["zaj─Öt", "busy", "locked", "zablokowan", "w u┼╝yciu", "in use"]),
        ("sie─ç", ["sieci", "sie─ç", "network", "connection", "po┼é─ůczen", "socket", "gniazdo"]),
        ("us┼éuga chwilowo niedost─Öpna", ["chwilowo", "temporarily", "unavailable", "niedost─Öpn", "busy service", "us┼éuga niedost─Öpna"]),
    ];

    /// <summary>Kr├│tki label chwilowej przyczyny albo null, gdy ponowienie nie ma sensu.</summary>
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
                return null; // trwa┼ée: ponowienie niczego nie zmieni
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

    /// <summary>Rosn─ůce oczekiwanie mi─Ödzy pr├│bami: 350 ms, sufit 1200 ms.</summary>
    public static int BackoffMilliseconds(int attempt) => Math.Min(1200, 350 * Math.Max(1, attempt));
}
