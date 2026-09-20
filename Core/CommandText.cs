using System.Text.RegularExpressions;
namespace SentinelX.Core;

/// <summary>One normalization boundary for keyboard, voice and palette. Never normalize file contents.</summary>
public static class CommandText
{
    public static string StripWakeWord(string text) => Regex.Replace(text.Trim(),
        @"^(?:hej\s+)?(?:sentinel|sentynel|sentinelu|sentynelu|centinel|centynel|centenel|santinel|sentnel)(?:\s*x)?(?=[\s,.!?]|$)[\s,.!?]*", "", RegexOptions.IgnoreCase);
    public static string Normalize(string text) => ConversationMemoryService.Normalize(StripWakeWord(text)).TrimEnd('?', '.', '!', ' ');
    public static bool IsApproval(string normalized) => normalized is "potwierdz" or "potwierdz akcje" or "confirm";
}
