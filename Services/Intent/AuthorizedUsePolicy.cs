using System.Text.RegularExpressions;

namespace SentinelX.Services.Intent;

/// <summary>
/// Conservative front-door checks for explicit requests to enable or carry out unlawful harm.
/// This is a deterministic guardrail, not a legal classifier or a substitute for human review.
/// Defensive analysis, education, recovery, and authorized testing are not refused by these rules.
/// </summary>
public static class AuthorizedUsePolicy
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(120);

    private static readonly Regex IllegalCapabilityRequest = new(
        @"\b(?:dodaj\w*|dodac\w*|pozwol\w*|umozliw\w*|wlacz\w*|enable\w*|allow\w*|make\w*|give\w*)\b[\s\S]{0,100}\b(?:nie\s+legaln\w*|nielegaln\w*|illegal|illicit|criminal|przestepstw\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex HarmfulSoftwareCreation = new(
        @"\b(?:stworz\w*|zrob\w*|napisz\w*|zaprogramuj\w*|wygeneruj\w*|wdroz\w*|uruchom\w*|create\w*|build\w*|write\w*|deploy\w*|install\w*|execute\w*)\b[\s\S]{0,100}\b(?:ransomware|keylogger|password\s+stealer|credential\s+stealer|phishing\s+kit|malware|trojan|botnet|rootkit|spyware|wiper|wykradacz\s+hasel|kradn\w*\s+hasl\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex DefensiveSoftwareRequest = new(
        @"\b(?:detector\w*|scanner\w*|monitor\w*|defen[cs]\w*|protect\w*|detect\w*|prevent\w*|sandbox\w*|forensic\w*|analysis|analyz\w*|zabezpiecz\w*|wykryj\w*|wykryw\w*|skaner\w*|detektor\w*|ochron\w*|analiz\w*|sandbox\w*|raport|poradnik|wyjasnij\w*|wytlumacz\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex UnauthorizedAccessOrTheft = new(
        @"\b(?:wlam\w*|zhack\w*|hack\w*|bypass\w*|obejdz\w*|wykrad\w*|ukradnij\w*|krad\w*|steal\w*|exfiltrat\w*|crack\w*|przejmij\w*)\b[\s\S]{0,120}\b(?:cudz\w*|czyj\w*|bez\s+zgod\w*|nieautoryzowan\w*|someone\s+elses|another\s+persons?|victim\w*|hasl\w*|passwords?|credentials?|dan\w*|data|kont\w*|account\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex EvidenceConcealment = new(
        @"\b(?:zatrzyj\w*|ukryj\w*|usun\w*|wyczysc\w*|erase\w*|hide\w*|delete\w*|wipe\w*)\b[\s\S]{0,100}\b(?:slad\w*|log\w*|dowod\w*|evidence|audit\s+trail)\b[\s\S]{0,100}\b(?:wlam\w*|hack\w*|kradziez\w*|incydent\w*|breach|intrusion|attack)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex FraudOrForgery = new(
        @"\b(?:podrob\w*|sfalsz\w*|forge\w*|counterfeit\w*|fake\w*)\b[\s\S]{0,100}\b(?:dowod\w*|dokument\w*|podpis\w*|id\s+card|passport|signature|invoice|banknote|currency)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex ExplosiveConstruction = new(
        @"\b(?:zrob\w*|zbuduj\w*|stworz\w*|skonstruuj\w*|make\w*|build\w*|construct\w*|assemble\w*)\b[\s\S]{0,100}\b(?:bombe|ladunek\s+wybuchow\w*|materialy\s+wybuchow\w*|explosive\w*|improvised\s+explosive\w*|pipe\s+bomb)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    private const string Refusal = "Nie pomogę w tworzeniu ani uruchamianiu narzędzi do nielegalnego dostępu, kradzieży danych, oszustw, ukrywania śladów lub wyrządzania szkody. Mogę pomóc w legalnym audycie własnego systemu, obronie, wykrywaniu zagrożeń, odzyskiwaniu dostępu albo bezpiecznej symulacji w odizolowanym laboratorium.";

    /// <summary>Returns a short refusal only for high-confidence actionable requests; ordinary discussion remains available.</summary>
    public static bool TryRefuse(string? input, out string response)
    {
        response = "";
        if (string.IsNullOrWhiteSpace(input)) return false;
        if (input.Length > 12_000)
        {
            response = "Nie mogę bezpiecznie ocenić tak długiego żądania automatycznie. Mogę pomóc podzielić je na legalny audyt, obronę albo odzyskiwanie dostępu.";
            return true;
        }
        string normalized = ConversationMemoryService.Normalize(input);
        if (normalized.Length == 0) return false;

        try
        {
            bool shouldRefuse = IllegalCapabilityRequest.IsMatch(normalized) ||
                UnauthorizedAccessOrTheft.IsMatch(normalized) || EvidenceConcealment.IsMatch(normalized) ||
                FraudOrForgery.IsMatch(normalized) || ExplosiveConstruction.IsMatch(normalized) ||
                (HarmfulSoftwareCreation.IsMatch(normalized) && !DefensiveSoftwareRequest.IsMatch(normalized));
            if (!shouldRefuse) return false;
            response = Refusal;
            return true;
        }
        catch (RegexMatchTimeoutException)
        {
            // If analysis times out, do not silently claim this request was approved by the policy.
            response = "Nie mogę bezpiecznie ocenić tej prośby automatycznie. Mogę pomóc przeformułować ją jako legalny audyt, obronę lub odzyskiwanie dostępu.";
            return true;
        }
    }
}
