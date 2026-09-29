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

    internal static bool ContainsLikelySecret(string? text) =>
        !string.IsNullOrEmpty(text) && (LabelledSecret.IsMatch(text) || NumericCode.IsMatch(text));

    internal static string Redact(string text)
    {
        text = LabelledSecret.Replace(text ?? string.Empty, m => m.Groups["prefix"].Value + "[REDACTED]");
        return NumericCode.Replace(text, m => m.Groups["prefix"].Value + "[REDACTED]");
    }
}
