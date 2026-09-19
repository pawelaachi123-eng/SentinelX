using System;

namespace SentinelX
{
    public class WakeWordService
    {
        public bool TryGetCommand(string text, out string command)
        {
            command = string.Empty;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            string cleaned = text.Trim();

            if (!cleaned.StartsWith(
                    "sentinel",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            command = cleaned["sentinel".Length..]
                .Trim(' ', ',', '.', ':', ';', '!', '?');

            return true;
        }
    }
}