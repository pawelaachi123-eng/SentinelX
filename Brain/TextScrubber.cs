using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>Secret scrubbing for memory/conversation exports. Replacements are
/// inert (no quotes or structure), so scrubbed JSON stays valid.</summary>
public static class TextScrubber
{
    public static string Scrub(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = Regex.Replace(text, @"-----BEGIN [A-Z0-9 ]*PRIVATE KEY-----[^-]*-----END [A-Z0-9 ]*PRIVATE KEY-----", "<private-key>");
        text = Regex.Replace(text, @"\b[0-9a-fA-F]{64}\b", "<hash>");
        text = Regex.Replace(text, "(?i)\"(api[_-]?key|secret|password|passwd|pwd|token|bearer)\"\\s*:\\s*\"[^\"]*\"", "\"$1\": \"<redacted>\"");
        text = Regex.Replace(text, @"(?i)\b(api[_-]?key|secret|password|passwd|pwd|token|bearer)\b\s*[:=]\s*([^\s"",}]+)", "$1=<redacted>");
        return text;
    }
}
