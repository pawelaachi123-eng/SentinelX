using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>Keep unrelated old summaries out of a small model's prompt; preserve explicit follow-ups and profile fields.</summary>
internal static class AiContextFilter
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "dlaczego", "czemu", "ktory", "ktora", "ktore", "jakie", "jakich", "powiedz", "wyjasnij", "odpowiedz",
        "pytanie", "uzytkownik", "uzytkownika", "sentinel", "prosze", "mnie", "jest", "jestem", "chcialbym", "mozesz"
    };

    internal static string Select(string question, string context, int characterBudget)
    {
        if (string.IsNullOrWhiteSpace(context) || characterBudget <= 0) return "";
        string normalized = ConversationMemoryService.Normalize(question);
        bool followUp = Regex.IsMatch(normalized, @"\b(?:wczesniej|przed chwila|tamto|to samo|rozwin|kontynuuj|dalej|poprzedni|ostatni temat|moim|moich|moje|moj)\b") ||
            Regex.IsMatch(normalized, @"^(?:a (?:dlaczego|czemu|co|jak)|dlaczego tak|co z tym|czy to|wyjasnij to)\b");
        var terms = Terms(question);
        var lines = context.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var selected = lines.Where(line => followUp || IsProfile(line) || Terms(line).Overlaps(terms)).ToList();
        // Preserve the most recent relevant evidence when the context grows beyond the budget.
        string text = string.Join("\n", selected);
        return text.Length <= characterBudget ? text : text[^characterBudget..];
    }

    private static bool IsProfile(string line) => Regex.IsMatch(line.Trim(), @"^(?:name|responseStyle|imię|imie|styl odpowiedzi)\s*:", RegexOptions.IgnoreCase);

    private static HashSet<string> Terms(string text) => Regex.Matches(ConversationMemoryService.Normalize(text), @"[a-z0-9]{3,}")
        .Select(m => m.Value).Where(x => !StopWords.Contains(x)).Select(x => x.Length > 5 ? x[..5] : x).ToHashSet(StringComparer.Ordinal);
}
