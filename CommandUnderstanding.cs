using System.Text.RegularExpressions;
using SentinelX.Core;

namespace SentinelX;

public sealed record CommandRepair(bool Success, string Text, string Canonical, string Summary, double Confidence)
{
    public static CommandRepair None { get; } = new(false, "", "", "", 0);
}

/// <summary>Typo-tolerant command understanding: Damerau-Levenshtein repair plus explicit abbreviations.
/// Repair only rewrites text — it never executes anything by itself, and destructive commands are
/// deliberately absent from the catalogue so a typo cannot trigger them.</summary>
public static class CommandUnderstanding
{
    /// <summary>Minimum similarity for a whole-phrase repair.</summary>
    public const double MinConfidence = 0.80;
    /// <summary>Minimum similarity for repairing a single word.</summary>
    public const double MinWordConfidence = 0.80;
    /// <summary>How much better the winner must be than the runner-up, so ambiguous input is left alone.</summary>
    public const double MinMargin = 0.08;

    /// <summary>Optimal string alignment distance: substitution, insertion, deletion and transposition
    /// each cost one edit, which is how most real typing mistakes happen.</summary>
    public static int Distance(string a, string b, int max = 12)
    {
        a ??= ""; b ??= "";
        int n = a.Length, m = b.Length;
        if (n == 0) return m;
        if (m == 0) return n;
        if (Math.Abs(n - m) > max) return max + 1;
        var previous2 = new int[m + 1];
        var previous = new int[m + 1];
        var current = new int[m + 1];
        for (int j = 0; j <= m; j++) previous[j] = j;
        for (int i = 1; i <= n; i++)
        {
            current[0] = i;
            for (int j = 1; j <= m; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    current[j] = Math.Min(current[j], previous2[j - 2] + cost);
            }
            int[] swap = previous2; previous2 = previous; previous = current; current = swap;
            if (previous.All(x => x > max)) return max + 1;
        }
        return previous[m];
    }

    /// <summary>Normalized similarity in 0..1. The search cap is roughly one edit per five characters,
    /// but the score itself always reflects the real distance, so near misses never tie with good matches.</summary>
    public static double Similarity(string a, string b)
    {
        a ??= ""; b ??= "";
        int longest = Math.Max(a.Length, b.Length);
        if (longest == 0) return 1;
        int budget = Math.Max(1, (int)Math.Ceiling(longest / 5.0)) + 4;
        return Math.Clamp(1 - (double)Distance(a, b, budget) / longest, 0, 1);
    }

    /// <summary>Word-level similarity: one mistyped word must not sink an otherwise correct phrase.</summary>
    public static double PhraseSimilarity(string input, string candidate)
    {
        double whole = Similarity(input, candidate);
        string[] a = Words(input), b = Words(candidate);
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length) return whole;
        int edits = 0;
        for (int i = 0; i < a.Length; i++) edits += Distance(a[i], b[i], 3);
        int longest = Math.Max(input.Length, candidate.Length);
        double wordwise = longest == 0 ? 1 : 1 - (double)edits / longest;
        return Math.Max(whole, wordwise);
    }

    internal static string[] Words(string text) =>
        Regex.Split(text ?? "", @"\s+", RegexOptions.None, TimeSpan.FromMilliseconds(200))
            .Where(x => x.Length > 0).ToArray();

    /// <summary>Repairs a mistyped command against the catalogue of safe, known phrases.</summary>
    public static CommandRepair Repair(string input) => Repair(input, IntentCatalog.Phrases);

    public static CommandRepair Repair(string input, IReadOnlyList<string> catalogue)
    {
        string normalized = ConversationMemoryService.Normalize(input ?? "").TrimEnd('?', '!', '.', ' ');
        if (normalized.Length == 0 || normalized.Contains('\n')) return CommandRepair.None;

        // Documented abbreviations win: deterministic, listed by „skróty”. They are short by design.
        if (IntentCatalog.Abbreviations.TryGetValue(normalized, out string? expanded))
            return new CommandRepair(true, expanded, expanded, "skrót „" + normalized + "” → „" + expanded + "”", 1.0);

        if (normalized.Length is < 3 or > 80) return CommandRepair.None;
        if (catalogue.Contains(normalized, StringComparer.Ordinal)) return CommandRepair.None;

        var ranked = catalogue
            .Select(phrase => (Phrase: phrase, Score: PhraseSimilarity(normalized, phrase)))
            .OrderByDescending(x => x.Score)
            .Take(2)
            .ToArray();
        if (ranked.Length > 0 && ranked[0].Score >= MinConfidence &&
            (ranked.Length == 1 || ranked[0].Score - ranked[1].Score >= MinMargin))
            return new CommandRepair(true, ranked[0].Phrase, ranked[0].Phrase, Describe(normalized, ranked[0].Phrase), ranked[0].Score);

        return RepairWords(normalized, catalogue);
    }

    /// <summary>Repairs individual mistyped words (also inside commands that take arguments).</summary>
    private static CommandRepair RepairWords(string normalized, IReadOnlyList<string> catalogue)
    {
        var vocabulary = IntentCatalog.Vocabulary;
        string[] words = Words(normalized);
        var changes = new List<string>();
        double worst = 1;
        for (int i = 0; i < words.Length; i++)
        {
            string word = words[i];
            if (word.Length < 4 || vocabulary.Contains(word)) continue;
            if (char.IsDigit(word[0]) || word.Contains('.') || word.Contains(':')) continue; // dates, times, numbers
            var candidates = vocabulary
                .Select(candidate => (Word: candidate, Score: Similarity(word, candidate)))
                .Where(x => x.Score >= MinWordConfidence)
                .OrderByDescending(x => x.Score)
                .Take(2)
                .ToArray();
            if (candidates.Length == 0) continue;
            if (candidates.Length > 1 && candidates[0].Score - candidates[1].Score < 0.05) continue; // ambiguous: leave it alone
            words[i] = candidates[0].Word;
            changes.Add(word + " → " + candidates[0].Word);
            worst = Math.Min(worst, candidates[0].Score);
        }
        if (changes.Count == 0) return CommandRepair.None;
        string repaired = string.Join(' ', words);
        // Only offer the repair when the result still looks like a known command, not like free conversation.
        if (!LooksLikeKnownCommand(repaired, catalogue)) return CommandRepair.None;
        return new CommandRepair(true, repaired, repaired, string.Join(", ", changes), worst);
    }

    internal static bool LooksLikeKnownCommand(string text, IReadOnlyList<string> catalogue) =>
        catalogue.Any(phrase => string.Equals(text, phrase, StringComparison.Ordinal) ||
            text.StartsWith(phrase + " ", StringComparison.Ordinal) ||
            phrase.StartsWith(text + " ", StringComparison.Ordinal));

    /// <summary>Below this similarity Sentinel does not even guess.</summary>
    public const double SuggestFloor = 0.62;
    /// <summary>At or above this similarity the command is executed (repair band) instead of asked about.</summary>
    public const double SuggestCeiling = 0.80;
    /// <summary>Stems that may never be guessed: a suggestion must not steer towards anything destructive.</summary>
    private static readonly string[] DestructiveStems = ["usun", "zamknij", "wylacz", "czysc", "kill", "sformatuj", "potwierdz", "zatrzymaj", "skasuj"];

    /// <summary>The grey zone between "understood" and "no idea": Sentinel proposes up to three known commands
    /// and asks, instead of guessing silently. Suggestions never execute anything on their own.</summary>
    public static IReadOnlyList<string> Suggest(string input) => Suggest(input, IntentCatalog.Phrases);

    public static IReadOnlyList<string> Suggest(string input, IReadOnlyList<string> catalogue)
    {
        string normalized = ConversationMemoryService.Normalize(input ?? "").TrimEnd('?', '!', '.', ' ');
        if (normalized.Length is < 3 or > 60 || normalized.Contains('\n')) return [];
        if (catalogue.Contains(normalized, StringComparer.Ordinal)) return [];
        if (DestructiveStems.Any(stem => normalized.Contains(stem, StringComparison.Ordinal))) return [];

        string firstWord = Words(normalized).FirstOrDefault() ?? "";
        var ranked = catalogue
            .Select(phrase => (Phrase: phrase, Score: PhraseSimilarity(normalized, phrase)))
            .Where(x => x.Score >= SuggestFloor && x.Score < SuggestCeiling)
            // A suggestion must at least start from the same verb — otherwise a typo could be steered
            // into a completely different intent (e.g. „zamknij notatnk” must never suggest launching).
            .Where(x => firstWord.Length > 0 && Distance(firstWord, Words(x.Phrase).FirstOrDefault() ?? "", 3) <= 1)
            .OrderByDescending(x => x.Score)
            .Take(3)
            .Select(x => x.Phrase)
            .ToList();
        return ranked;
    }

    private static string Describe(string from, string to)
    {
        string[] a = Words(from), b = Words(to);
        if (a.Length != b.Length) return "„" + from + "” → „" + to + "”";
        var changed = new List<string>();
        for (int i = 0; i < a.Length; i++)
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) changed.Add(a[i] + " → " + b[i]);
        return changed.Count == 0 ? "„" + from + "” → „" + to + "”" : string.Join(", ", changed);
    }
}
