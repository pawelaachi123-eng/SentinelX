using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>Conservative masking for clearly labelled credentials before persistent conversation/memory storage.</summary>
internal static class SensitiveDataRedactor
{
    private static readonly Regex LabelledSecret = new(
        @"(?<prefix>\b(?:has[lł]\w*|password\w*|passcode|token|secret|sekret|api[ -]?key|klucz prywatn\w*)\s*(?::|=|\bis\b|\bto\b|\bwynosi\b|\bbrzmi\b)\s*)(?<value>[^\s,;]{4,})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex NumericCode = new(
        @"(?<prefix>\b(?:otp|pin|kod jednorazow\w*|kod weryfikacyjn\w*)\s*[:=]?\s*)(?<value>\d{4,8})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex TokenAssignment = new(
        @"(?<prefix>\b(?:access[_-]?token|refresh[_-]?token|client[_-]?secret|api[_-]?key)\s*=\s*)(?<value>[^\s,;]{8,})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex BearerCredential = new(
        @"(?<prefix>\b(?:authorization\s*:\s*bearer|bearer)\s+)(?<value>[A-Za-z0-9._~+/-]{12,}=*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PrivateKeyBlock = new(
        @"-----BEGIN [A-Z0-9 ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z0-9 ]*PRIVATE KEY-----",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static bool ContainsLikelySecret(string? text) =>
        !string.IsNullOrEmpty(text) && (LabelledSecret.IsMatch(text) || NumericCode.IsMatch(text) || TokenAssignment.IsMatch(text) ||
            BearerCredential.IsMatch(text) || PrivateKeyBlock.IsMatch(text));

    internal static string Redact(string text)
    {
        text = PrivateKeyBlock.Replace(text ?? string.Empty, "[REDACTED PRIVATE KEY]");
        text = LabelledSecret.Replace(text, m => m.Groups["prefix"].Value + "[REDACTED]");
        text = TokenAssignment.Replace(text, m => m.Groups["prefix"].Value + "[REDACTED]");
        text = BearerCredential.Replace(text, m => m.Groups["prefix"].Value + "[REDACTED]");
        return NumericCode.Replace(text, m => m.Groups["prefix"].Value + "[REDACTED]");
    }
}
