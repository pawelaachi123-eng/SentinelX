using System.Text.RegularExpressions;
using SentinelX.Core;

namespace SentinelX;

public sealed record CommandRepair(bool Success, string Text, string Canonical, string Summary, double Confidence)
{
    public static CommandRepair None { get; } = new(false, "", "", "", 0);
}

/// <summary>0.94 · wynik pełnego rozumienia: skróty, literówki i polecenia ukryte w zdaniu.</summary>
public sealed record UnderstandingResult(bool Success, string Text, string Canonical, string Summary, double Confidence)
{
    public static UnderstandingResult None { get; } = new(false, "", "", "", 0);
}

/// <summary>Typo-tolerant command understanding: Damerau-Levenshtein repair plus explicit abbreviations.
/// Repair only rewrites text — it never executes anything by itself, and destructive commands are
/// deliberately absent from the catalogue so a typo cannot trigger them.
/// Since 0.94 the pipeline also understands polite sentences („sprawdź proszę ile mam ramu”), verb
/// synonyms („odpal discorda” → „wlacz discord”) and embedded commands — always by rewriting towards
/// the safe catalogue, always with a visible „Zrozumiałem jako…” note.</summary>
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

        // 0.97 · twarda bariera: wejścia z czasownikiem niszczącym nie naprawiam wcale. Bez tego
        // „usun wszystko” potrafiło trafić w podobną frazę bezpieczną (np. „minimalizuj wszystko”)
        // i wrócić jako gotowe polecenie. Literówka nie może zamienić destrukcji w cokolwiek.
        if (DestructiveStems.Any(stem => normalized.Contains(stem, StringComparison.Ordinal))) return CommandRepair.None;

        // Documented abbreviations win: deterministic, listed by „skróty”. They are short by design.
        if (IntentCatalog.Abbreviations.TryGetValue(normalized, out string? expanded))
            return new CommandRepair(true, expanded, expanded, "skrót „" + normalized + "” → „" + expanded + "”", 1.0);

        if (normalized.Length is < 3 or > 80) return CommandRepair.None;
        if (catalogue.Contains(normalized, StringComparer.Ordinal)) return CommandRepair.None;

        var ranked = catalogue
            // 0.97 · naprawa to naprawa literówki, nie parafraza: przy tej samej liczbie słów każde
            // podmienione słowo musi być podobne do wzorca na poziomie progu literówki. Bez tego
            // zdanie z dodatkową treścią („ile mam ramu dziś”) wchodziło w podobną frazę
            // („ile mam ramu lacznie”), bo globalny stosunek edycji to maskował.
            .Where(phrase => LooksLikeTypo(normalized, phrase))
            .Select(phrase => (Phrase: phrase, Score: PhraseSimilarity(normalized, phrase)))
            .OrderByDescending(x => x.Score)
            .Take(2)
            .ToArray();
        if (ranked.Length > 0 && ranked[0].Score >= MinConfidence &&
            (ranked.Length == 1 || ranked[0].Score - ranked[1].Score >= MinMargin))
            return new CommandRepair(true, ranked[0].Phrase, ranked[0].Phrase, Describe(normalized, ranked[0].Phrase), ranked[0].Score);

        return RepairWords(normalized, catalogue);
    }

    /// <summary>0.97 · Czy różnica między wpisem a wzorcem wygląda na literówkę? Przy różnej liczbie
    /// słów decyduje podobieństwo całego tekstu; przy tej samej liczbie słów każde podmienione słowo
    /// musi samo przekraczać próg literówki, więc podmiana treści („dziś” → „lacznie”) nie jest naprawą.</summary>
    private static bool LooksLikeTypo(string input, string phrase)
    {
        string[] a = Words(input), b = Words(phrase);
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length) return true;
        for (int i = 0; i < a.Length; i++)
        {
            string left = CommandLexicon.CompareForm(a[i]), right = CommandLexicon.CompareForm(b[i]);
            if (string.Equals(left, right, StringComparison.Ordinal)) continue;
            if (left.Length < 3 || right.Length < 3) return false;
            if (Similarity(left, right) < MinWordConfidence) return false;
        }
        return true;
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
        // Real commands are short; a multi-word sentence is conversation and goes to the model, never to guessing.
        if (Words(normalized).Length > 4) return [];
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

    // =========================================================================================
    // 0.94 · rozumienie zdań: dekoracje, synonimy i polecenia ukryte w dłuższej wypowiedzi
    // =========================================================================================

    /// <summary>Argument commands: the phrase is a stem and whatever follows is its argument
    /// („ile dni do 24.12”, „policz 2+2”, „notatka: …”). Checked explicitly and by catalogue prefix.</summary>
    private static readonly HashSet<string> ArgumentStems = new(StringComparer.Ordinal)
    {
        "ile dni do", "ile dni od", "ile dni miedzy", "policz", "kalkulator", "oblicz", "ile to", "przelicz", "konwertuj",
        "procent", "ile to procent", "vat", "znizka", "napiwek", "raty", "odsetki", "procent skladany",
        "log", "logarytm", "potega", "modulo", "reszta z dzielenia", "wartosc bezwzgledna", "abs",
        "sin", "cos", "tan", "srednia wazona", "rownanie", "kalendarz", "dodaj", "odejmij",
        "rok przestepny", "kwartal", "ean", "isbn", "luhn", "regon", "literuj", "czestotliwosc",
        "powtorzenia", "skrable", "posortuj", "bez powtorzen", "odwroc slowa", "tylko cyfry", "tylko litery",
        "ile slow", "policz slowa", "ile wyrazow", "ile znakow", "ile zdan", "base64", "dekoduj base64",
        "hash tekstu", "sha256", "json", "sprawdz json", "slug", "transliteruj", "wielkie litery",
        "male litery", "odwroc tekst", "losuj", "rzuc kostka", "wybierz losowo", "bmi", "rzymskie",
        "z rzymskich", "kolor", "pin", "pesel", "nip", "iban", "rgb", "wiek", "dni robocze",
        "wielkanoc", "czas w", "czas w strefie", "pierwiastek", "silnia", "nwd", "nww", "czy pierwsza",
        "dzielniki", "fibonacci", "srednia", "mediana", "suma", "min", "max", "zaokraglij",
        "zmiana z", "zmiana procentowa", "szukaj wszystkiego", "szukaj w rozmowie", "szukaj w pamieci",
        "szukaj zadan", "szukaj", "dodaj zadanie", "zrob zadanie", "nowe zadanie", "notatka",
        "zapamietaj", "przypomnij", "nowy projekt", "uzyj projektu", "aktywuj projekt", "model ai",
        "ustaw model ai", "duplikaty", "porzadki", "zmien nazwy", "cytat", "wylosuj karte", "kostka",
    };

    private static bool TakesArguments(string phrase, IReadOnlyList<string> catalogue) =>
        ArgumentStems.Contains(phrase) || catalogue.Any(p => p.StartsWith(phrase + " ", StringComparison.Ordinal));

    /// <summary>Strips pure decoration words (politeness, fillers) from anywhere in the sentence.
    /// Intent verbs (napisz, opowiedz…) and argument particles are never touched.</summary>
    public static string StripDecorations(string text)
    {
        string[] words = Words(ConversationMemoryService.Normalize(text ?? ""));
        var kept = words.Where(w => !CommandLexicon.IsDecoration(w)).ToArray();
        return string.Join(' ', kept);
    }

    /// <summary>Full pipeline: abbreviations → typo repair (on the decoration-stripped form first)
    /// → embedded known command. Never executes anything; only rewrites text and says what it did.</summary>
    public static UnderstandingResult Understand(string input) => Understand(input, IntentCatalog.Phrases);

    public static UnderstandingResult Understand(string input, IReadOnlyList<string> catalogue)
    {
        string normalized = ConversationMemoryService.Normalize(input ?? "").TrimEnd('?', '!', '.', ' ');
        if (normalized.Length == 0 || normalized.Contains('\n')) return UnderstandingResult.None;

        // 1. Documented abbreviations are deterministic and win first.
        if (IntentCatalog.Abbreviations.TryGetValue(normalized, out string? expanded))
            return new UnderstandingResult(true, expanded, expanded, "skrót „" + normalized + "” → „" + expanded + "”", 1.0);

        // 2. Typo repair — on the stripped form first („sprawdź proszę ile mam ramuu”), then as-is.
        string stripped = StripDecorations(normalized);
        CommandRepair repair = Repair(stripped, catalogue);
        if (!repair.Success && !string.Equals(stripped, normalized, StringComparison.Ordinal))
            repair = Repair(normalized, catalogue);
        if (repair.Success)
            return new UnderstandingResult(true, repair.Text, repair.Canonical, repair.Summary, repair.Confidence);

        // 3. A known command embedded in a polite or wordy sentence. Try the raw sentence
        //    first, then the filler-stripped one (so trailing „prosze” cannot block it).
        UnderstandingResult extracted = Extract(normalized, catalogue);
        if (!extracted.Success && !string.Equals(stripped, normalized, StringComparison.Ordinal))
            extracted = Extract(stripped, catalogue);
        if (extracted.Success) return extracted;

        return UnderstandingResult.None;
    }

    /// <summary>Finds a known (safe) command phrase inside a longer utterance and rebuilds it,
    /// preserving arguments. Refuses: negations, destructive input, conversation verbs and any
    /// context that is not pure politeness around the phrase. The result is always a catalogue
    /// phrase (or a phrase plus its arguments) — never a guessed command.</summary>
    public static UnderstandingResult Extract(string input) => Extract(input, IntentCatalog.Phrases);

    public static UnderstandingResult Extract(string input, IReadOnlyList<string> catalogue)
    {
        string normalized = ConversationMemoryService.Normalize(input ?? "").TrimEnd('?', '!', '.', ' ');
        if (normalized.Length is < 3 or > 120 || normalized.Contains('\n')) return UnderstandingResult.None;
        // A negated or destructive request is never auto-executed by extraction.
        if (PolishTextNormalizer.ContainsNegation(normalized)) return UnderstandingResult.None;
        if (DestructiveStems.Any(stem => normalized.Contains(stem, StringComparison.Ordinal))) return UnderstandingResult.None;

        string[] words = Words(normalized);
        if (words.Length == 0) return UnderstandingResult.None;
        // „napisz mi wiersz o tym ile mam ramu” is a request for text, not for a RAM reading.
        if (CommandLexicon.ContainsConversationVerb(words)) return UnderstandingResult.None;

        var candidates = catalogue
            .Select(phrase => (Phrase: phrase, Words: Words(phrase)))
            .Where(x => x.Words.Length > 0)
            .OrderByDescending(x => x.Words.Length)
            .ThenBy(x => x.Phrase.Length)
            .ThenBy(x => x.Phrase, StringComparer.Ordinal);

        foreach (var (phrase, phraseWords) in candidates)
        {
            for (int i = 0; i + phraseWords.Length <= words.Length; i++)
            {
                bool fuzzy = false;
                bool matched = true;
                for (int k = 0; k < phraseWords.Length; k++)
                {
                    if (!CommandLexicon.WordsMatch(words[i + k], phraseWords[k])) { matched = false; break; }
                    if (!string.Equals(CommandLexicon.CompareForm(words[i + k]), CommandLexicon.CompareForm(phraseWords[k]), StringComparison.Ordinal)) fuzzy = true;
                }
                if (!matched) continue;

                int j = i + phraseWords.Length;
                bool leadOk = true;
                for (int t = 0; t < i; t++)
                    if (!CommandLexicon.IsOutsideWord(words[t])) { leadOk = false; break; }
                if (!leadOk) continue;

                bool takesArgs = TakesArguments(phrase, catalogue);
                // A single-word phrase is only trusted as a whole input (or as an argument stem).
                if (phraseWords.Length == 1 && !takesArgs && !(i == 0 && j == words.Length)) continue;

                var tail = new List<string>();
                bool tailOk = true;
                if (takesArgs)
                {
                    int end = words.Length;
                    while (end > j && CommandLexicon.IsDecoration(words[end - 1])) end--;
                    for (int t = j; t < end; t++) tail.Add(words[t]);
                }
                else
                {
                    for (int t = j; t < words.Length; t++)
                        if (!CommandLexicon.IsOutsideWord(words[t])) { tailOk = false; break; }
                }
                if (!tailOk) continue;

                int dropped = i + (words.Length - j - tail.Count);
                // The sentence already IS the command (maybe with ":" punctuation) — nothing to rewrite.
                if (!fuzzy && i == 0 && dropped == 0) return UnderstandingResult.None;

                string rebuilt = tail.Count == 0 ? phrase : phrase + " " + string.Join(' ', tail);
                var notes = new List<string>();
                if (i > 0)
                {
                    var skipped = words.Take(i).Concat(words.Skip(j).Take(words.Length - j - tail.Count)).Where(x => x.Length > 0).ToArray();
                    if (skipped.Length > 0) notes.Add("pominąłem: " + string.Join(", ", skipped));
                }
                else
                {
                    var skipped = words.Skip(j).Take(words.Length - j - tail.Count).Where(x => x.Length > 0).ToArray();
                    if (skipped.Length > 0) notes.Add("pominąłem: " + string.Join(", ", skipped));
                }
                if (fuzzy)
                {
                    var changed = new List<string>();
                    for (int k = 0; k < phraseWords.Length; k++)
                        if (!string.Equals(CommandLexicon.CompareForm(words[i + k]), CommandLexicon.CompareForm(phraseWords[k]), StringComparison.Ordinal))
                            changed.Add(words[i + k] + " → " + phraseWords[k]);
                    if (changed.Count > 0) notes.Add(string.Join(", ", changed));
                }
                string summary = notes.Count == 0
                    ? "„" + normalized + "” → „" + phrase + "”"
                    : string.Join("; ", notes);
                return new UnderstandingResult(true, rebuilt, phrase, summary, fuzzy ? 0.9 : 1.0);
            }
        }
        return UnderstandingResult.None;
    }

    /// <summary>Read-only dry-run for the „zrozum” command: shows every step of understanding
    /// without executing anything.</summary>
    public static string Explain(string input)
    {
        string normalized = ConversationMemoryService.Normalize(input ?? "").TrimEnd('?', '!', '.', ' ');
        var lines = new List<string>
        {
            "ROZUMIENIE — pokazuję kroki, niczego nie wykonuję.",
            "· wpis: „" + (input ?? "").Trim() + "”"
        };
        if (normalized.Length == 0) return string.Join("\n", lines) + "\n· pusty wpis.";
        lines.Add("· normalizacja: „" + normalized + "”");
        string stripped = StripDecorations(normalized);
        if (!string.Equals(stripped, normalized, StringComparison.Ordinal))
            lines.Add("· bez słów dekoracyjnych: „" + stripped + "”");
        if (IntentCatalog.Abbreviations.TryGetValue(normalized, out string? expanded))
        {
            lines.Add("· skrót: „" + normalized + "” → „" + expanded + "”");
            return string.Join("\n", lines) + "\n⇒ wykonane zostanie: „" + expanded + "”";
        }
        CommandRepair repair = Repair(stripped.Length > 0 ? stripped : normalized, IntentCatalog.Phrases);
        if (repair.Success)
        {
            lines.Add("· literówka/skrót: " + repair.Summary + " (pewność " + repair.Confidence.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ")");
            return string.Join("\n", lines) + "\n⇒ wykonane zostanie: „" + repair.Text + "”";
        }
        UnderstandingResult extracted = Extract(normalized, IntentCatalog.Phrases);
        if (extracted.Success)
        {
            lines.Add("· polecenie w zdaniu: " + extracted.Summary);
            return string.Join("\n", lines) + "\n⇒ wykonane zostanie: „" + extracted.Text + "”";
        }
        var suggestions = Suggest(normalized, IntentCatalog.Phrases);
        if (suggestions.Count > 0)
            lines.Add("· szara strefa: zaproponuję „" + suggestions[0] + "” i zapytam, zamiast zgadywać.");
        else
            lines.Add("· nie znalazłem polecenia — trafi to do modelu AI jako zwykła wypowiedź.");
        return string.Join("\n", lines);
    }
}
