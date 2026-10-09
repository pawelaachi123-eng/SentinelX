using System.Text.RegularExpressions;

namespace SentinelX.Services.Creative;

/// <summary>
/// Converts ordinary Polish or English requests into a bounded creative brief.
/// This is a fast deterministic router, not an LLM prompt executor: generated text is never run as C# or Luau.
/// </summary>
public static class RobloxGameRequestAnalyzer
{
    private const int MaximumPromptLength = 6_000;
    private static readonly TimeSpan RegexLimit = TimeSpan.FromMilliseconds(150);

    private static readonly Regex Platform = new(
        @"\b(?:roblox|robloks|robloxie|robloxa|robloxowa|robloxowym|robloxowego|robloxowej|robloxowy|robloxowym|robloxowe|robloxowska|robloxowskiego)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexLimit);
    private static readonly Regex CreationVerb = new(
        @"\b(?:stw[oó]rz\w*|zrob\w*|zbuduj\w*|zaprojektuj\w*|wygeneruj\w*|przygotuj\w*|opracuj\w*|napisz\w*|wykonaj\w*|make\w*|create\w*|build\w*|design\w*|generate\w*|develop\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexLimit);
    private static readonly Regex GameNoun = new(
        @"\b(?:gr[aeęęy]|gier|grach|game|games|experience|experiences|world|worlds|playable|place)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexLimit);
    private static readonly Regex InformationalQuestion = new(
        @"^(?:jak\s+(?:mog[eę]|mozesz|mozna|zrobic|stworzyc|zbudowac|zaprojektowac)|co\s+to|opowiedz|wyjasnij|wytlumacz|how\s+(?:do|can|to)|what\s+is|explain)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexLimit);
    private static readonly Regex NegatedCreationIntent = new(
        @"\b(?:nie|don['’]?t|do\s+not|never)\s+(?:[\p{L}\p{N}_]+[\s,]+){0,3}(?:stw[oó]rz\w*|zrob\w*|zbuduj\w*|zaprojektuj\w*|wygeneruj\w*|make\w*|create\w*|build\w*|design\w*|generate\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexLimit);
    private static readonly Regex ExplicitName = new(
        @"(?:nazwij(?:\s+(?:ja|ją|gre|grę|projekt))?|nazwa(?:\s+gry)?|tytul(?:\s+gry)?|name\s+it|call\s+it|title\s+it)\s*(?:to|jest|:)?\s*[""“„']?(?<name>[^""”„'\r\n,.;!?]{2,60}?)(?=\s+(?:(?:i|oraz|and)\s+(?:zaprojektuj\w*|stw[oó]rz\w*|zrob\w*|zbuduj\w*|wykonaj\w*|dodaj\w*|uwzgl[eę]dnij\w*|design\w*|create\w*|build\w*|make\w*|include\w*|support\w*))\b|[.,;!?]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexLimit);
    private static readonly Regex QuotedName = new(
        @"[""“„'](?<name>[^""”„'\r\n]{2,60})[""”„']",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexLimit);

    private static readonly (string Genre, string[] Cues)[] GenreCues =
    [
        ("tower-defense", ["tower defense", "tower-defense", "obrona wiezy", "obrona wież", "wieza obronna"]),
        ("racing", ["wyscig", "wyscigi", "race", "racing", "samochod", "samochodowy", "kart", "drift"]),
        ("tycoon", ["tycoon", "magnat", "imperium", "biznes", "factory", "fabryka"]),
        ("obby", ["obby", "parkour", "tor przeszkod", "przeszkod", "platformow"]),
        ("horror", ["horror", "strasz", "groza", "scary", "monster", "potwor"]),
        ("survival", ["survival", "przetrwan", "battle royale", "ostatni zywy", "ostatni ocala"]),
        ("arena", ["arena", "pvp", "walka", "combat", "battle", "pojedyn"]),
        ("roleplay", ["roleplay", "role play", "odgrywanie rol", "rp", "miasto role", "city role"]),
        ("story", ["story", "fabu", "opowiesc", "przygoda", "quest", "misj"]),
        ("pets", ["pet simulator", "zwierzak", "zwierzeta", "pets", "pupil"]),
        ("simulator", ["simulator", "symulator", "clicker", "kliker", "idle", "zbieranie", "farm"]),
    ];

    private static readonly (string Key, string[] Cues)[] ThemeCues =
    [
        ("space", ["kosmos", "kosmicz", "space", "galakty", "planeta", "gwiazd", "orbita"]),
        ("underwater", ["ocean", "oceanicz", "morze", "podwod", "woda", "deep sea"]),
        ("fantasy", ["fantasy", "fantaz", "smok", "magia", "czar", "elf", "mitolog"]),
        ("cyberpunk", ["cyberpunk", "neon", "futurystycz", "sci-fi", "science fiction", "technolog"]),
        ("cozy", ["cozy", "przytul", "spokoj", "relaks", "cute", "slodk"]),
        ("nature", ["las", "jungle", "dzungl", "natura", "zwierz", "dinozaur", "dino"]),
        ("city", ["miasto", "city", "ulic", "metropoli"]),
        ("horror", ["horror", "strasz", "groza", "noc", "mrok", "creepy"]),
        ("winter", ["zima", "zimow", "snieg", "lodow", "winter", "ice"]),
    ];

    /// <summary>Returns false for discussion/questions and ordinary Roblox coding questions. No special command syntax is required.</summary>
    public static bool TryAnalyze(string? input, out RobloxGameProjectSpec spec)
    {
        spec = new("", "", "");
        string source = (input ?? "").Trim();
        if (source.Length is < 8 or > MaximumPromptLength || source.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'))) return false;
        string normalized = ConversationMemoryService.Normalize(source).ToLowerInvariant();

        try
        {
            if (NegatedCreationIntent.IsMatch(normalized)) return false;
            if (!Platform.IsMatch(normalized) || !GameNoun.IsMatch(normalized) || !CreationVerb.IsMatch(normalized)) return false;
            if (InformationalQuestion.IsMatch(normalized) && !Regex.IsMatch(normalized, @"\b(?:zrob mi|stworz mi|zbuduj mi|make me|create me|build me)\b", RegexOptions.CultureInvariant, RegexLimit))
                return false;

            string genre = DetectGenre(normalized);
            string theme = DetectTheme(normalized);
            string name = ExtractName(source, genre, theme);
            string slug = RobloxGameProjectGenerator.SlugifyName(name);
            if (slug.Length == 0) return false;

            spec = new RobloxGameProjectSpec(genre, name, slug)
            {
                OriginalPrompt = source,
                Theme = theme,
                ArtDirection = DetectArtDirection(normalized, theme),
                CoreLoop = RobloxGameProjectGenerator.GetLoopFor(genre, theme),
                TargetDevice = DetectDevicePriority(normalized),
                GrowthIntent = ContainsAny(normalized, "popular", "trend", "viral", "hit", "wybij", "popularn", "trenduj", "viralow"),
                PolishLanguage = IsPolishRequest(normalized)
            };
            return true;
        }
        catch (RegexMatchTimeoutException)
        {
            spec = new("", "", "");
            return false;
        }
    }

    public static string DetectGenre(string normalized)
    {
        string text = ConversationMemoryService.Normalize(normalized).ToLowerInvariant();
        foreach (var (genre, cues) in GenreCues)
            if (ContainsAny(text, cues))
                return genre switch
                {
                    "horror" or "survival" => "rounds",
                    "pets" or "simulator" => "simulator",
                    "roleplay" or "story" or "arena" or "tower-defense" => "custom",
                    _ => genre
                };
        return "custom";
    }

    public static string DetectTheme(string normalized)
    {
        string text = ConversationMemoryService.Normalize(normalized).ToLowerInvariant();
        foreach (var (theme, cues) in ThemeCues)
            if (ContainsAny(text, cues)) return theme;
        return "adventure";
    }

    private static string ExtractName(string source, string genre, string theme)
    {
        try
        {
            Match named = ExplicitName.Match(source);
            if (named.Success)
            {
                string explicitName = RobloxGameProjectGenerator.CleanNameForIntent(named.Groups["name"].Value);
                if (explicitName.Length > 1) return explicitName;
            }
            Match quoted = QuotedName.Match(source);
            if (quoted.Success)
            {
                string quotedName = RobloxGameProjectGenerator.CleanNameForIntent(quoted.Groups["name"].Value);
                if (quotedName.Length > 1 && !LooksLikeSentence(quotedName)) return quotedName;
            }
        }
        catch (RegexMatchTimeoutException) { }

        string themeName = theme switch
        {
            "space" => "Starfall",
            "underwater" => "Abyssal",
            "fantasy" => "Mythic",
            "cyberpunk" => "Neon",
            "cozy" => "Moonlit",
            "nature" => "Wildwood",
            "city" => "Skyline",
            "horror" => "Midnight",
            "winter" => "Frostbound",
            _ => "Brightforge"
        };
        string mode = genre switch
        {
            "simulator" => "Frontier",
            "obby" => "Skyline Rush",
            "tycoon" => "Workshop",
            "rounds" => theme == "horror" ? "After Dark" : "Last Light",
            "racing" => "Velocity",
            "tower-defense" => "Defenders",
            "arena" => "Showdown",
            "roleplay" => "Stories",
            "story" => "Chronicles",
            _ => "Worlds"
        };
        string title = genre == "obby" ? mode : themeName + " " + mode;
        return RobloxGameProjectGenerator.CleanNameForIntent(title);
    }

    private static bool LooksLikeSentence(string value) => value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 7;

    private static string DetectArtDirection(string normalized, string theme)
    {
        string style = ContainsAny(normalized, "low poly", "low-poly", "lowpoly", "stylized", "stylizow") ? "low-poly, stylizowane kształty" : "czytelny styl stylizowany";
        if (ContainsAny(normalized, "pixel", "piksel")) style = "pixel-art, ograniczona paleta";
        else if (ContainsAny(normalized, "realist", "realistic")) style = "oszczędny realizm z priorytetem wydajności";
        string light = theme switch
        {
            "horror" => "kontrastowe nocne światło z bezpieczną opcją ograniczenia efektów",
            "space" or "cyberpunk" => "kontrolowane neony, mocny kontrast i ciemne tło",
            "cozy" => "miękkie ciepłe światło i zaokrąglone sylwetki",
            "underwater" => "chłodne błękity, warstwowa głębia i czytelne punkty orientacyjne",
            _ => "kolorystyczne strefy, wyraźne sylwetki i czytelne punkty orientacyjne"
        };
        return style + "; " + light + "; mobilny budżet efektów i kontrast tekstu są częścią projektu.";
    }

    private static string DetectDevicePriority(string normalized) => ContainsAny(normalized, "mobile", "telefon", "telefonach", "dotyk", "mobile-first", "na komork", "na komór")
        ? "mobile-first, touch + desktop"
        : "cross-platform, desktop + touch";

    private static bool IsPolishRequest(string normalized) => ContainsAny(normalized, "stworz", "zrob", "zbuduj", "zaprojektuj", "gra", "gry", "na robloxie");

    private static bool ContainsAny(string text, params string[] terms) => terms.Any(term => text.Contains(term, StringComparison.Ordinal));
}
