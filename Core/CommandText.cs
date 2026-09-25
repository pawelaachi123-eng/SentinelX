using System.Text.RegularExpressions;
namespace SentinelX.Core;

/// <summary>One normalization boundary for keyboard, voice and palette. Never normalize file contents.</summary>
public static class CommandText
{
    /// <summary>The wake word as a whole word — allowed anywhere in the sentence since 0.91, with the
    /// usual spelling variants. The lookahead/lookbehind keep „sentinelowy” from matching.</summary>
    private static readonly Regex WakeToken = new(
        @"(?<![0-9a-ząćęłńóśźż])(?:hej\s+)?(?:sentinel|sentynel|sentinelu|sentynelu|centinel|centynel|centenel|santinel|sentnel)(?:\s*x)?[\s,.!?]*(?=\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));

    /// <summary>True when the user addressed Sentinel anywhere in the utterance („Sentinel, …”,
    /// „… i sentinel”, „zrób to sentinel”). Without the wake word Sentinel only listens.</summary>
    public static bool ContainsWakeWord(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        try { return WakeToken.IsMatch(text); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    /// <summary>Removes exactly one wake-word invocation (wherever it stands) so the rest is a clean command.
    /// Only the first occurrence is removed — a note that merely mentions „sentinel” keeps its content.</summary>
    public static string StripWakeWord(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string stripped;
        try { stripped = WakeToken.Replace(text.Trim(), " ", 1); }
        catch (RegexMatchTimeoutException) { return text.Trim(); }
        return Regex.Replace(stripped, @"\s+", " ", RegexOptions.None, TimeSpan.FromMilliseconds(200)).Trim().Trim(' ', ',', '.', '!', '?', ':', ';');
    }

    public static string Normalize(string text) => ConversationMemoryService.Normalize(StripWakeWord(text)).TrimEnd('?', '.', '!', ' ');
    public static bool IsApproval(string normalized) => normalized is "potwierdz" or "potwierdz akcje" or "confirm" or "potwierdz usuniecie wspomnien";
}
