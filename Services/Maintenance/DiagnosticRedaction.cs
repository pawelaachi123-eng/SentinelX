using System.IO;
using System.Text.RegularExpressions;

namespace SentinelX.Services.Maintenance;

/// <summary>Redaction for exported diagnostics: hashes, secrets and the user
/// profile path never leave the machine in clear text.</summary>
public static class DiagnosticRedaction
{
    public static string Redact(string text)
    {
        text = Regex.Replace(text, @"[0-9a-fA-F]{64}", "<hash>");
        text = Regex.Replace(text, "(?i)\"(secret|password|token|secrethex|fingerprint)\"\\s*:\\s*\"[^\"]*\"", "\"$1\":\"<redacted>\"");
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile)) text = text.Replace(profile, "<profile>", StringComparison.OrdinalIgnoreCase);
        return text;
    }
}
