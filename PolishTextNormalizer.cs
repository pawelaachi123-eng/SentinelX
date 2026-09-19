using System.Text;
using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>
/// Normalizacja języka polskiego dla poleceń głosowych i tekstowych.
/// Zakres: diakrytyki (bez utraty informacji), liczebniki słowne, odmiana nazw aplikacji
/// przed czasownikiem („włącz Discorda" → „włącz discord"), detekcja negacji i pytań o zgodę.
/// Celowo NIE przepisuje swobodnej wypowiedzi — nie zmienia intencji, tylko wyrównuje formy.
/// </summary>
public static class PolishTextNormalizer
{
    /// <summary>Najczęstsze formy odmiane nazw aplikacji i urządzeń w poleceniach użytkownika.</summary>
    private static readonly Dictionary<string, string> InflectedToCanonical = new(StringComparer.Ordinal)
    {
        // Discord
        ["discorda"] = "discord", ["discordzie"] = "discord", ["discordowi"] = "discord", ["discordem"] = "discord", ["discordy"] = "discord",
        // Steam
        ["steama"] = "steam", ["steamie"] = "steam", ["steamem"] = "steam",
        // Brave
        ["brave'a"] = "brave", ["bravea"] = "brave", ["bravem"] = "brave", ["bravie"] = "brave",
        // Chrome
        ["chrome'a"] = "chrome", ["chromea"] = "chrome", ["chroma"] = "chrome", ["chromie"] = "chrome", ["chromem"] = "chrome",
        // Firefox
        ["firefoxa"] = "firefox", ["firefoxem"] = "firefox", ["firefoxie"] = "firefox",
        // YouTube
        ["youtuba"] = "youtube", ["youtube'a"] = "youtube", ["youtube"] = "youtube", ["jutuba"] = "youtube", ["jutubie"] = "youtube", ["jutubem"] = "youtube",
        // Spotify
        ["spotifya"] = "spotify", ["spotify'a"] = "spotify", ["spotify"] = "spotify", ["spotifie"] = "spotify",
        // CS2
        ["csa"] = "cs2", ["csie"] = "cs2", ["countera"] = "cs2", ["counter strike'a"] = "cs2", ["counter strike'a 2"] = "cs2",
        // Notatnik
        ["notatnika"] = "notatnik", ["notatniku"] = "notatnik", ["notatnikiem"] = "notatnik",
        // Kalkulator
        ["kalkulatora"] = "kalkulator", ["kalkulatorze"] = "kalkulator", ["kalkulatorem"] = "kalkulator",
        // Eksplorator
        ["eksploratora"] = "eksplorator", ["eksploratorze"] = "eksplorator",
        // Menedżer zadań (dopełniacz/biernik przed czasownikiem)
        ["menedzera zadan"] = "menedzer zadan", ["menedżera zadań"] = "menedzer zadan", ["menedzerowi zadan"] = "menedzer zadan",
        // Spotify/Edge/other
        ["edge'a"] = "edge", ["edgea"] = "edge", ["edżem"] = "edge",
        ["vscoda"] = "vscode", ["vs code'a"] = "vscode", ["vs codem"] = "vscode",
        ["telegramu"] = "telegram", ["telegrama"] = "telegram", ["telegramie"] = "telegram",
        ["whatsappa"] = "whatsapp", ["whatsapp"] = "whatsapp",
        ["figmy"] = "figma", ["figme"] = "figma",
        ["gita"] = "git", ["gitem"] = "git",
        ["gmaila"] = "gmail", ["gmailie"] = "gmail",
        ["faceita"] = "faceit", ["faceicie"] = "faceit",
        ["chatgpta"] = "chatgpt", ["chatgpt"] = "chatgpt", ["czatgpta"] = "chatgpt",
        ["worda"] = "word", ["wordzie"] = "word", ["wordem"] = "word",
        ["excela"] = "excel", ["excelu"] = "excel", ["excelem"] = "excel",
        ["painta"] = "paint", ["paincie"] = "paint",
    };

    /// <summary>Lista słów wyzwalających akcje (uruchom/otwórz/włącz…), używana do wykrycia pozycji „przed czasownikiem".</summary>
    private static readonly Regex ActionVerbPattern = new(
        @"\b(?:w[lł][aą]cz|uruchom|otw[oó]rz|odpal|zamknij|zabij|instaluj|odinstaluj|usu[nń]|skopiuj|przenie[sś]|zmie[nń] nazw|[sś]ci[aą]gnij|pobierz)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Czasowniki (z przydawkami), po których oczekujemy nazwy aplikacji w bierniku.</summary>
    private static readonly Regex VerbThenTargetPattern = new(
        @"^(?<lead>(?:mi\s+)?(?:szybko\s+|teraz\s+|prosze\s+|proszę\s+|już\s+)?)" +
        @"(?<verb>w[lł][aą]cz(?:y[cć]|aj)?|uruchom(?:i[cć]|iaj)?|otw[oó]rz(?:y[cć]|aj)?|odpal(?:i[cć]|aj)?|zamknij)" +
        @"(?<rest>\s+.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Prefiksy uprzejmości/pytań, które nie zmieniają intencji wykonania.</summary>
    private static readonly Regex PolitenessPrefix = new(
        @"^(?:czy\s+(?:mo[zż]esz|mo[zż]na|mo[gż]emy)|mo[zż]esz\s+(?:mi\s+)?|mo[gż]emy\s+|chcia[lł]bym\s+|chc[eę]\s+|poprosz[eę]\s+|prosz[eę]\s+|zr[oó]b\s+mi\s+|daj\s+mi\s+|mo[zż]esz\s+prosz[eę]\s+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Negacje: wyraźne „nie rób" oraz rozkazy z partykułą przeczącą.</summary>
    private static readonly Regex NegationPattern = new(
        @"\b(?:nie\s+(?:w[lł][aą]czaj|uruchamiaj|otwieraj|odpalaj|zamknij|usuwaj|instaluj|odinstalowuj|rob|r[oó]b)|ani\s+s[aą]d|zostaw\s|nie\s+r[oó]b\s+tego|odpu[sś][cć]|anuluj\s+to)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Pytania o zgodę/zasięg intencji — nie wykonujemy akcji na ich podstawie.</summary>
    private static readonly Regex PermissionQuestionPattern = new(
        @"^(?:czy\s+(?:mo[zż]esz|mo[zż]na|umiesz|potrafisz)|jak\s+by[sś]z|da[lł]o?\s+by\s+si[eę]|czy\s+da\s+si[eę])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Liczebniki słowne → cyfry (dla timerów i powtórzeń).</summary>
    private static readonly Dictionary<string, int> SpokenNumbers = new(StringComparer.Ordinal)
    {
        ["zero"] = 0, ["jeden"] = 1, ["jedna"] = 1, ["jedno"] = 1, ["dwa"] = 2, ["dwie"] = 2, ["trzy"] = 3, ["cztery"] = 4,
        ["piec"] = 5, ["pięć"] = 5, ["szesc"] = 6, ["sześć"] = 6, ["siedem"] = 7, ["osiem"] = 8, ["dziewiec"] = 9, ["dziewięć"] = 9,
        ["dziesiec"] = 10, ["dziesięć"] = 10, ["kwadrans"] = 15, ["poltora"] = 2, ["półtory"] = 2,
        ["pieknascie"] = 15, ["piętnaście"] = 15, ["dwadziescia"] = 20, ["dwadzieścia"] = 20, ["trzydziesci"] = 30, ["trzydzieści"] = 30,
        ["czterdziesci"] = 40, ["czterdzieści"] = 40, ["piecdziesiat"] = 50, ["pięćdziesiąt"] = 50,
    };

    /// <summary>Jednostki czasu słownie z mnożnikiem w sekundach.</summary>
    private static readonly Dictionary<string, int> TimeUnitsSeconds = new(StringComparer.Ordinal)
    {
        ["sekund"] = 1, ["sekundy"] = 1, ["sekunde"] = 1, ["sekundę"] = 1, ["sek"] = 1, ["s"] = 1,
        ["minut"] = 60, ["minuty"] = 60, ["minute"] = 60, ["minutę"] = 60, ["min"] = 60, ["m"] = 60,
        ["godzin"] = 3600, ["godziny"] = 3600, ["godzine"] = 3600, ["godzinę"] = 3600, ["godz"] = 3600, ["h"] = 3600,
    };

    public static string StripDiacritics(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        string formD = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (char c in formD)
        {
            if (char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Wyrównuje polecenie do formy kanonicznej: usuwa uprzejmości, sprowadza odmienione nazwy
    /// aplikacji do nazw bazowych tam, gdzie poprzedzają czasownik akcji. Zwraca oryginalny tekst,
    /// gdy nie ma czego wyrównać (nie ryzykuje zmiany intencji).
    /// </summary>
    public static string CanonicalizeCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return command;
        string text = command.Trim().TrimEnd('?', '!', '.', ' ').Trim();
        string withoutPoliteness = PolitenessPrefix.Replace(text, "");
        if (withoutPoliteness.Length > 0 && withoutPoliteness.Length < text.Length) text = withoutPoliteness;

        // Wyrównanie odmiany tylko w konstrukcji „czasownik + nazwa".
        Match verb = VerbThenTargetPattern.Match(text);
        if (!verb.Success) return text;
        string rest = verb.Groups["rest"].Value.Trim();
        string lead = verb.Groups["lead"].Value;
        string canonical = InflectTargetPhrase(rest);
        return lead + verb.Groups["verb"].Value + " " + canonical;
    }

    /// <summary>Sprowadza frazę z dopełnieniem do nazwy bazowej (bez ruszania dalszych słów, np. „i steama").</summary>
    public static string InflectTargetPhrase(string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase)) return phrase;
        // Podział na segmenty „i / oraz / a potem / , „ aby nie ruszać spójników wieloetapowych.
        string[] separators = { " i ", " oraz ", " a potem ", ", ", " potem " };
        List<string> parts = [.. phrase.Split(separators, StringSplitOptions.None).Select(p => p.Trim())];
        List<string> rebuilt = [];
        foreach (string part in parts)
        {
            string trimmed = part.TrimEnd('.', '!', '?').Trim();
            string lower = trimmed.ToLowerInvariant();
            // Najpierw pełne frazy (np. „menedżera zadań"), potem pojedyncze słowa z końca.
            if (InflectedToCanonical.TryGetValue(lower, out string? full))
            {
                rebuilt.Add(full);
                continue;
            }
            string[] words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 0)
            {
                string last = words[^1].ToLowerInvariant();
                if (InflectedToCanonical.TryGetValue(last, out string? single))
                {
                    words[^1] = single;
                    rebuilt.Add(string.Join(' ', words));
                    continue;
                }
            }
            rebuilt.Add(trimmed);
        }
        return string.Join(" i ", rebuilt);
    }

    public static bool ContainsNegation(string text) => NegationPattern.IsMatch(text ?? "");

    /// <summary>Czy tekst zawiera czasownik akcji (nawet w formie negowanej) — używane do blokady negacji.</summary>
    public static bool ContainsActionVerb(string text) => ActionVerbPattern.IsMatch(text ?? "");

    public static bool IsPermissionQuestion(string text) => PermissionQuestionPattern.IsMatch((text ?? "").Trim());

    /// <summary>Zamienia słowne liczebniki na cyfry (tylko przy jednostkach czasu lub „razy").</summary>
    public static string ConvertSpokenNumbers(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        string result = text;
        foreach ((string word, int value) in SpokenNumbers)
        {
            result = Regex.Replace(
                result,
                @"\b" + Regex.Escape(word) + @"\b(?=\s+(?:sekund|sekundy|sekundę|minut|minuty|minutę|min|godzin|godziny|godzinę|godz|raz[y]?)\b)",
                value.ToString(),
                RegexOptions.IgnoreCase);
        }
        return result;
    }

    /// <summary>Parsuje czas trwania z tekstu typu „5 minut", „dwa i pół minuty", „kwadrans". Zwraca sekundy lub null.</summary>
    public static int? TryParseDurationSeconds(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string normalized = StripDiacritics(text.ToLowerInvariant());

        Match combined = Regex.Match(normalized, @"(\d{1,4})\s*(?:i\s*(?:p[oó][lł])?\s*)?(d{0})");
        // Prosty przypadek: liczba + jednostka, ewentualnie dwie pary („1 godzina 20 minut").
        MatchCollection pairs = Regex.Matches(normalized, @"(\d{1,4})\s*(sekund|sekundy|sekunde|sek|minut|minuty|minute|min|godzin|godziny|godzine|godz|h)\b");
        int total = 0;
        foreach (Match pair in pairs)
        {
            if (!int.TryParse(pair.Groups[1].Value, out int amount)) continue;
            string unit = pair.Groups[2].Value;
            int multiplier = unit.StartsWith("sek") ? 1 : unit.StartsWith("min") || unit == "m" ? 60 : 3600;
            total += amount * multiplier;
        }
        if (total > 0) return total;

        // Formy słowne: „kwadrans", „pół godziny", „trzydzieści minut".
        if (normalized.Contains("kwadrans")) return 15 * 60;
        if (Regex.IsMatch(normalized, @"p[oó][lł]\s*godzin")) return 30 * 60;
        foreach ((string word, int value) in SpokenNumbers)
        {
            Match spoken = Regex.Match(normalized, @"\b" + Regex.Escape(StripDiacritics(word)) + @"\b\s*(sekund|sekundy|sekunde|sek|minut|minuty|minute|min|godzin|godziny|godzine|godz|h)\b");
            if (spoken.Success)
            {
                string unit = spoken.Groups[1].Value;
                int multiplier = unit.StartsWith("sek") ? 1 : unit.StartsWith("min") || unit == "m" ? 60 : 3600;
                return value * multiplier;
            }
        }
        return null;
    }

    /// <summary>Zwraca listę sugestii diagnostycznych, dlaczego polecenie mogło nie zostać rozpoznane.</summary>
    public static IReadOnlyList<string> GetNormalizationHints(string command)
    {
        List<string> hints = [];
        if (string.IsNullOrWhiteSpace(command)) return hints;
        if (ContainsNegation(command)) hints.Add("Polecenie zawiera negację — wykonanie zostało zatrzymane.");
        if (IsPermissionQuestion(command)) hints.Add("To pytanie o możliwość, nie rozkaz — nie uruchamiam akcji.");
        string canonical = CanonicalizeCommand(command);
        if (!string.Equals(canonical, command.Trim(), StringComparison.Ordinal))
            hints.Add("Rozumiem to jako: „" + canonical + "”.");
        return hints;
    }
}
