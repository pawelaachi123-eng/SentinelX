using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>Keep unrelated old summaries out of a small model's prompt; preserve explicit follow-ups and profile fields.
/// 0.94: selection is scored (term overlap after Polish stemming + recency) instead of „any overlap, keep the tail”,
/// so the most relevant lines survive the budget. Follow-ups and the profile behave exactly as before.</summary>
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
        bool followUp = Regex.IsMatch(normalized, @"\b(?:wczesniej|przed chwila|tamto|to samo|rozwin|kontynuuj|dalej|poprzedni|ostatni temat|moim|moich|moje|moj|w takim razie|co z tamtym|dokladnie)\b") ||
            Regex.IsMatch(normalized, @"^(?:a (?:dlaczego|czemu|co|jak)|dlaczego tak|co z tym|czy to|wyjasnij to|no a |wiec )\b");
        var terms = Terms(question);
        var lines = context.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Score every line: profile lines always win, follow-ups keep everything, otherwise only
        // lines sharing (stemmed) terms with the question — newest and most relevant first.
        var candidates = new List<(string Line, int Index, double Score, bool Profile)>();
        for (int i = 0; i < lines.Length; i++)
        {
            bool profile = IsProfile(lines[i]);
            double score = profile ? 1000 : Score(lines[i], terms);
            if (followUp || profile || score > 0) candidates.Add((lines[i], i, score + i * 0.001, profile));
        }

        var kept = new List<(string Line, int Index)>();
        int budget = characterBudget;
        foreach (var item in candidates.OrderByDescending(x => x.Score))
        {
            int cost = item.Line.Length + 1;
            if (cost > budget) continue;
            budget -= cost;
            kept.Add((item.Line, item.Index));
        }
        kept.Sort((a, b) => a.Index.CompareTo(b.Index));
        return string.Join("\n", kept.Select(x => x.Line));
    }

    private static double Score(string line, HashSet<string> questionTerms)
    {
        var lineTerms = Terms(line);
        if (lineTerms.Count == 0) return 0;
        int matched = lineTerms.Count(t => questionTerms.Contains(t));
        return matched == 0 ? 0 : matched + (double)matched / lineTerms.Count;
    }

    private static bool IsProfile(string line) => Regex.IsMatch(line.Trim(), @"^(?:name|responseStyle|imię|imie|styl odpowiedzi)\s*:", RegexOptions.IgnoreCase);

    private static HashSet<string> Terms(string text) => Regex.Matches(ConversationMemoryService.Normalize(text), @"[a-z0-9]{3,}")
        .Select(m => m.Value).Where(x => !StopWords.Contains(x))
        .Select(x => Core.CommandLexicon.Stem(x))
        .Where(x => x.Length >= 2)
        .ToHashSet(StringComparer.Ordinal);
}
