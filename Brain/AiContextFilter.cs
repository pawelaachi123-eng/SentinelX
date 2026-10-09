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
        bool followUp = Regex.IsMatch(normalized, @"\b(?:wczesniej|przed chwila|tamto|to samo|rozwin|kontynuuj|dalej|poprzedni|ostatni temat|moim|moich|moje|moj|co (?:mowilem|mowilismy|powiedzialem|powiedzielismy|ustalilem|ustalilismy|zdecydowalem|zdecydowalismy|pisalem|pisalismy|wspomnialem)|o czym rozmawialismy|what did (?:i|we)|what (?:was|were) we talking about|earlier|previously|last time)\b") ||
            Regex.IsMatch(normalized, @"^(?:a (?:dlaczego|czemu|co|jak)|dlaczego tak|co z tym|czy to|wyjasnij to)\b");
        var terms = Terms(question);
        string[] lines = context.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var candidates = lines.Select((line, index) =>
        {
            int overlap = Terms(line).Intersect(terms).Count();
            bool profile = IsProfile(line);
            bool retrieved = line.Contains("[starszy trafiony fragment]", StringComparison.Ordinal);
            bool matched = overlap > 0;
            return (Line: line, Index: index, Overlap: overlap, Profile: profile, Retrieved: retrieved,
                Selected: followUp || profile || matched);
        }).Where(x => x.Selected).ToArray();
        string allSelected = string.Join("\n", candidates.Select(x => x.Line));
        if (allSelected.Length <= characterBudget) return allSelected;

        // If the prompt is tight, prioritize explicitly retrieved older turns, then profile and
        // query-matching evidence; only then keep the newest general follow-up lines.
        var priority = candidates
            .OrderByDescending(x => x.Retrieved)
            .ThenByDescending(x => x.Profile)
            .ThenByDescending(x => x.Overlap)
            .ThenByDescending(x => x.Index)
            .ToArray();
        var chosen = new Dictionary<int, string>();
        int remaining = characterBudget;
        foreach (var candidate in priority)
        {
            if (remaining <= 0) break;
            int separator = chosen.Count == 0 ? 0 : 1;
            int available = remaining - separator;
            if (available <= 0) break;
            string line = candidate.Line;
            if (line.Length > available)
            {
                bool essential = candidate.Retrieved || (candidate.Profile && candidate.Overlap > 0) || candidate.Overlap > 0;
                if (!essential || available < 24) continue;
                line = line[..(available - 1)] + "…";
            }
            chosen[candidate.Index] = line;
            remaining -= line.Length + separator;
        }
        return string.Join("\n", chosen.OrderBy(x => x.Key).Select(x => x.Value));
    }

    private static bool IsProfile(string line) => Regex.IsMatch(line.Trim(), @"^-?\s*(?:name|responseStyle|imię|imie|styl odpowiedzi)\s*:", RegexOptions.IgnoreCase);

    private static HashSet<string> Terms(string text) => Regex.Matches(ConversationMemoryService.Normalize(text), @"[a-z0-9]{3,}")
        .Select(m => m.Value).Where(x => !StopWords.Contains(x)).Select(x => x.Length > 5 ? x[..5] : x).ToHashSet(StringComparer.Ordinal);
}
