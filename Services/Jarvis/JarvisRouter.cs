using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using SentinelX.Services.Actions;

namespace SentinelX.Services.Jarvis;

/// <summary>
/// 0.96 · warstwa JARVIS: jedna powierzchnia poleceń dla pogody, multimediów, domu, agenta, wizji,
/// indeksu semantycznego i sekwencji. Zwraca null, gdy zdanie nie jest nasze — wtedy routing idzie
/// dalej, a model językowy dostaje polecenie dopiero na końcu łańcucha (jak zawsze).
///
/// Rozpoznawanie jest dwuczęściowe i celowo takie jest: pasujemy po tekście znormalizowanym
/// (odporność na polskie znaki i literówki odmiany), a ARGUMENTY wycinamy z oryginalu, żeby
/// nazwa miasta czy sekwencji zachowała „ż”, „ą” i wielkie litery.
///
/// Zasady bezpieczeństwa:
///  • pogoda to jedyne polecenie, które dzwoni na zewnątrz — wymaga włączenia w ustawieniach i mówi o tym;
///  • sterowanie domem jest domyślnie wyłączone i NIE trafiło do narzędzi agenta;
///  • sekwencje odrzucają kroki niszczące, a zgoda „potwierdź” nigdy nie jest automatyczna;
///  • brak modelu, mikrofonu lub urządzenia = uczciwa odmowa z instrukcją naprawy.
/// </summary>
public sealed class JarvisRouter
{
    private readonly WeatherService weather;
    private readonly MediaService media;
    private readonly HomeAutomationService home;
    private readonly AgentService agent;
    private readonly VisionService vision;
    private readonly SemanticMemoryIndex index;
    private readonly RoutineService routines;
    private readonly StepRunner steps;
    private readonly Func<JarvisSettings> settings;

    public JarvisRouter(WeatherService weather, MediaService media, HomeAutomationService home, AgentService agent,
        VisionService vision, SemanticMemoryIndex index, RoutineService routines, StepRunner steps,
        Func<JarvisSettings>? settings = null)
    {
        this.weather = weather; this.media = media; this.home = home; this.agent = agent;
        this.vision = vision; this.index = index; this.routines = routines; this.steps = steps;
        this.settings = settings ?? (() => new JarvisSettings());
    }

    /// <summary>Null = to nie polecenie Jarvis. Inny tekst = obsłużone w całości tutaj.</summary>
    public async Task<string?> ProcessAsync(string raw, string text, CancellationToken token, Action<string>? onDelta = null)
    {
        text = (text ?? "").Trim().TrimEnd('?', '!', '.', ' ');
        raw = (raw ?? "").Trim();
        if (text.Length == 0) return null;
        try { return await RouteAsync(raw, text, token, onDelta); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
    }

    private async Task<string?> RouteAsync(string raw, string text, CancellationToken token, Action<string>? onDelta)
    {
        // ---- pogoda (Open-Meteo, jawne użycie sieci) ----
        if (TryWeatherCity(raw, text, out string city)) return Describe(await weather.GetAsync(city, token));
        if (IsOneOf(text, "czy bedzie padac", "czy beda opady", "czy bedzie snieg", "prognoza opadow", "ile bedzie stopni"))
            return Describe(await weather.RainCheckAsync("", token));

        // ---- multimedia ----
        if (MediaCommands.Match(text, out string mediaLabel) is { } key) return Describe(media.Send(key, mediaLabel));
        if (IsOneOf(text, "co gra", "co teraz gra", "jaka muzyka gra", "co leci", "player status")) return Describe(media.Status());
        if (TryVolume(text, out int delta)) return Describe(media.NudgeVolume(delta));

        // ---- inteligentny dom (tylko Home Assistant lokalnie) ----
        if (IsOneOf(text, "dom", "dom status", "dom: status", "co w domu", "stan domu", "home assistant status")) return Describe(await home.StatusAsync(token));
        if (IsOneOf(text, "dom lista", "dom: lista", "encje", "jakie mam urzadzenia", "dom urzadzenia")) return Describe(await home.DescribeEntitiesAsync(20, token));
        if (TryHomeCommand(raw, text, out string device, out bool on)) return Describe(await home.SetAsync(device, on, token));
        if (TryScene(raw, text, out string scene)) return Describe(await home.SetAsync(scene, on: true, token));

        // ---- agent z narzędziami ----
        if (IsOneOf(text, "agent status", "tryb agenta", "czy agent dziala", "agent")) return AgentStatus();
        if (IsOneOf(text, "narzedzia agenta", "agent narzedzia", "co agent moze")) return AgentToolCatalog.Describe();
        if (TryAgentGoal(raw, text, out string goal)) return await agent.RunAsync(goal, onDelta, token);

        // ---- wizja lokalna ----
        if (IsOneOf(text, "co jest na ekranie", "co widac na ekranie", "co jest na moim ekranie", "opisz ekran", "opisz moj ekran"))
            return await vision.DescribeScreenAsync(DescribeScreenInstruction, token);
        if (IsOneOf(text, "przeczytaj ekran", "przeczytaj co jest na ekranie", "przeczytaj tekst na ekranie"))
            return await vision.DescribeScreenAsync(ReadScreenInstruction, token);
        if (TryReadScreen(raw, text, out string instruction)) return await vision.DescribeScreenAsync(instruction, token);

        // ---- pamięć semantyczna (dodatek, nie zamiennik) ----
        if (IsOneOf(text, "indeks semantyczny", "indeks semantyczny status", "status indeksu")) return index.Status();
        if (IsOneOf(text, "indeks semantyczny: zbuduj", "zbuduj indeks", "zbuduj indeks semantyczny", "przelicz indeks"))
            return await index.BuildAsync(onDelta, token);
        if (IsOneOf(text, "indeks semantyczny: usun", "usun indeks", "usun indeks semantyczny")) return index.Clear();
        if (TrySemanticQuery(raw, text, out string query)) return await SearchSemanticallyAsync(query, token);

        // ---- sekwencje ----
        if (IsOneOf(text, "sekwencje", "moje sekwencje", "list sekwencji", "rutiny", "sekwencja")) return routines.Describe();
        if (TryRoutineCreate(raw, text, out string name, out string sequenceSteps)) return Describe(routines.Create(name, sequenceSteps));
        if (TryRoutine("podglad", raw, text, out string previewName))
            return routines.Find(previewName) is { } preview ? RoutineService.Preview(preview) : NieMaSekwencji(previewName);
        if (TryRoutine("usun", raw, text, out string removeName)) return Describe(routines.Delete(removeName));
        if (TryRoutine("run", raw, text, out string runName)) return await RunRoutineAsync(runName, onDelta, token);

        if (IsOneOf(text, "jarvis", "warstwa jarvis", "co masz nowego", "nowe funkcje jarvis")) return Overview();
        return null;
    }

    private const string DescribeScreenInstruction =
        "Opisz krótko, co widać na tym zrzucie ekranu: jakie okna, co na nich jest, ewentualne błędy lub ostrzeżenia.";
    private const string ReadScreenInstruction =
        "Przeczytaj dokładnie i po kolei cały czytelny tekst widoczny na tym zrzucie ekranu. Nie dodawaj nic od siebie.";

    // ------------------------------------------------------------------ rodziny poleceń

    private async Task<string> SearchSemanticallyAsync(string query, CancellationToken token)
    {
        if (!index.IsEnabled)
            return index.Status() + "\n\nTymczasem działa wyszukiwanie tekstowe: „szukaj wszystkiego: " + query + "”.";
        IReadOnlyList<(string Kind, string Text, double Score)> found = await index.SearchAsync(query, 6, token);
        if (found.Count == 0)
            return $"Indeks nie znalazł nic podobnego do „{query}”. Być trzeba go odświeżyć: „indeks semantyczny: zbuduj”.";
        var lines = new StringBuilder();
        lines.Append("Wyszukiwanie semantyczne · najbliższe wpisy (indeks liczony lokalnie, bez sieci zewnętrznej):\n");
        foreach ((string kind, string text, double score) in found)
            lines.Append($"· {score.ToString("0.00", CultureInfo.GetCultureInfo("pl-PL"))} [{kind}] {Clip(text, 180)}\n");
        lines.Append("Podobieństwo wektorowe nie jest prawdą o treści — sprawdź wpis, zanim coś na jego podstawie zrobisz.");
        return lines.ToString();
    }

    private async Task<string> RunRoutineAsync(string name, Action<string>? onProgress, CancellationToken token)
    {
        Routine? routine = routines.Find(name);
        if (routine == null) return NieMaSekwencji(name);
        RoutineResult result = await routines.RunAsync(routine, steps, onProgress, token);
        return result.Message + (result.Evidence.Length > 0 && result.Success ? "\n\nPodsumowanie: " + result.Evidence : "");
    }

    private static string NieMaSekwencji(string name) =>
        $"Nie ma sekwencji „{name}”. Listę pokaże „sekwencje”, a nową zapiszesz tak: „utwórz sekwencję: poranek = który jest dzień; plan dnia”.";

    private string AgentStatus()
    {
        JarvisSettings config = settings();
        var lines = new StringBuilder();
        lines.Append("🤖 TRYB AGENTA\n").Append(agent.StatusText()).Append('\n');
        lines.Append(config.AgentEnabled
            ? "Agent może prosić o wykonanie narzędzi tylko-do-odczytu. Zgody „potwierdź” nie ma i nie będzie.\n"
            : "Żeby uruchomić: Ustawienia → Jarvis → „Tryb agenta”. Bez włączenia nic nie wykona.\n");
        lines.Append("Przykład: „agent: sprawdź, czy mam miejsce na dysku i czy karta nie grzeje, a potem podsumuj”.\n");
        lines.Append("Pełna lista narzędzi: „narzedzia agenta”.");
        return lines.ToString();
    }

    private static string Overview() =>
        """
        WARSTWA JARVIS (0.96) — to, co doszło obok klasycznych poleceń

        Pogoda: pogoda · pogoda Kraków · czy będzie padać · temperatura na zewnątrz
          → wymaga internetu (Open-Meteo, bez klucza API). Bez sieci pokazuję ostatni zapis z jasną adnotacją.
        Multimedia: pauza · następny utwór · poprzedni utwór · stop odtwarzanie · wycisz odtwarzacz · co gra · głośniej · ciszej
          → klawisze systemowe: Spotify, VLC, odtwarzacze w przeglądarce. Tytułu utworu nie czytamy.
        Dom (Home Assistant, lokalnie): dom status · dom lista · dom: włącz światło salon · scena: noc
          → wyłączone, dopóki nie włączysz w ustawieniach; token wyłącznie ze zmiennej SENTINEL_HA_TOKEN.
        Agent (lokalny model + narzędzia): agent: <cel> · agent status · narzedzia agenta
          → 20 narzędzi wyłącznie do odczytu, limit kroków, transkrypcja każdego kroku z dowodem.
        Wizja: co jest na ekranie · przeczytaj ekran · opisz ekran
          → tylko lokalny model wizyjny, obraz nie jest zapisywany na dysk.
        Pamięć semantyczna: indeks semantyczny: zbuduj · szukaj semantycznie: fraza · indeks semantyczny: status
          → dodatek do wyszukiwania tekstowego, które zostało bez zmian.
        Sekwencje: utwórz sekwencję: nazwa = krok1; krok2 · sekwencje · podgląd sekwencji: nazwa · uruchom sekwencję: nazwa
          → kroki niszczące odrzucane przy zapisie i przy odczycie, zgoda nigdy nie jest automatyczna.

        Wszystko idzie przez ten sam potok co pozostałe polecenia: STOP awaryjny, centrum zgód, audyt z requestId.
        """;

    // ------------------------------------------------------------------ rozpoznawanie (czyste, testowalne)

    public static bool IsOneOf(string text, params string[] phrases)
    {
        foreach (string phrase in phrases) if (text.Equals(phrase, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>„pogoda”, „pogoda Kraków”, „sprawdź pogodę w Krakowie”, „prognoza dla Zakopanego”.</summary>
    public static bool TryWeatherCity(string raw, string text, out string city)
    {
        city = "";
        if (IsOneOf(text, "pogoda", "jaka pogoda", "pogoda dzis", "temperatura na dworze", "temperatura na zewnatrz", "ile stopni")) return true;
        Match match = Regex.Match(raw,
            @"^(?:(?:pogoda|prognoza|przewidywanie)\s*(?:dla|w|we|na)?\s*|(?:sprawdz|sprawdź)\s+(?:pogode|pogodę|pogoda)\s*(?:dla|w|we)?\s*)(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        if (!match.Success) return false;
        string value = Tail(match.Groups[1].Value);
        if (value.Length is 0 or > 64) return false;
        if (ConversationMemoryService.Normalize(value).Length == 0) return false;
        city = value;
        return true;
    }

    /// <summary>„głośniej”, „ciszej 10”, „głośniej o 5”. Zawsze pełne procenty, bez ułamków.</summary>
    public static bool TryVolume(string text, out int deltaPercent)
    {
        deltaPercent = 0;
        Match match = Regex.Match(text, @"^(glosniej|ciszej)(?:\s+(?:o\s+)?(\d{1,2}))?(?:\s*(?:%|procent))?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        if (!match.Success) return false;
        int amount = match.Groups[2].Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : 5;
        if (amount is < 1 or > 50) return false;
        deltaPercent = match.Groups[1].Value.Equals("ciszej", StringComparison.OrdinalIgnoreCase) ? -amount : amount;
        return true;
    }

    /// <summary>Przedrostek „dom:”/„home assistant:” ALBO rzeczownik urządzenia domowego — bez tego
    /// „włącz spotify” i „włącz notatnik” zostałyby odebrane launcherowi aplikacji. Nazwę encji
    /// przekazujemy w całości (bez wycinania słowa „światło”): tak łatwiej trafić w encję „Światło salon”.</summary>
    public static bool TryHomeCommand(string raw, string text, out string device, out bool on)
    {
        device = ""; on = false;
        const string verbs = @"(?:wlacz|włącz|wylacz|wyłącz|zapal|zgas|zgaś|gasz|gas|turn\s+on|turn\s+off)";
        Match gate = Regex.Match(text, @"^(?:(?:dom|urzadzenia|home assistant)\s*[:\-]?\s*)?(" + verbs + @")\s+(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        if (!gate.Success) return false;
        bool prefixed = Regex.IsMatch(text, @"^(?:dom|urzadzenia|home assistant)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(150));
        string rest = gate.Groups[2].Value.Trim();
        if (!prefixed && !LooksLikeHomeDevice(ConversationMemoryService.Normalize(rest))) return false;
        on = gate.Groups[1].Value.StartsWith("wlacz", StringComparison.OrdinalIgnoreCase) || gate.Groups[1].Value.StartsWith("włącz", StringComparison.OrdinalIgnoreCase)
            || gate.Groups[1].Value.StartsWith("zapal", StringComparison.OrdinalIgnoreCase) || gate.Groups[1].Value.Contains("on", StringComparison.OrdinalIgnoreCase);
        Match fromRaw = Regex.Match(raw, @"^(?:(?:dom|urządzenia|urzadzenia|home assistant)\s*[:\-]?\s*)?" + verbs + @"\s+(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        device = TrimWord(fromRaw.Success ? fromRaw.Groups[1].Value : rest);
        return device.Length is > 0 and <= 80;
    }

    public static bool LooksLikeHomeDevice(string normalized) =>
        normalized.StartsWith("swiatlo", StringComparison.Ordinal) || normalized.StartsWith("swiatla", StringComparison.Ordinal)
        || normalized.Contains("swiatlo ", StringComparison.Ordinal) || normalized.Contains("swiatla ", StringComparison.Ordinal)
        || normalized.StartsWith("lamp", StringComparison.Ordinal) || normalized.Contains("lampa", StringComparison.Ordinal)
        || normalized.Contains("lampy", StringComparison.Ordinal) || normalized.Contains("gniazdk", StringComparison.Ordinal)
        || normalized.Contains("zaluzj", StringComparison.Ordinal) || normalized.Contains("ogrzew", StringComparison.Ordinal)
        || normalized.Contains("klimat", StringComparison.Ordinal) || normalized.Contains("czujnik", StringComparison.Ordinal)
        || normalized.Contains("scena", StringComparison.Ordinal);

    public static bool TryScene(string raw, string text, out string scene)
    {
        scene = "";
        if (!Regex.IsMatch(text, @"^(scena|tryb\s+domu)\s*[:\-]?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(150))) return false;
        Match match = Regex.Match(raw, @"^(?:scena|tryb\s+domu)\s*[:\-]?\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        if (!match.Success) return false;
        scene = Tail(match.Groups[1].Value);
        return scene.Length > 0 && scene.Length <= 60;
    }

    public static bool TryAgentGoal(string raw, string text, out string goal)
    {
        goal = "";
        if (!text.StartsWith("agent", StringComparison.Ordinal)) return false;
        Match match = Regex.Match(raw, @"^agent\s*[:\-]?\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        if (!match.Success) return false;
        string value = Tail(match.Groups[1].Value);
        if (value.Length == 0 || IsOneOf(ConversationMemoryService.Normalize(value), "status", "co umiesz", "narzedzia", "narzędzia")) return false;
        goal = value;
        return true;
    }

    public static bool TryReadScreen(string raw, string text, out string instruction)
    {
        instruction = "";
        if (!Regex.IsMatch(text, @"^(przeczytaj|opisz)\b.*ekran", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200))) return false;
        Match match = Regex.Match(raw, @"^(?:przeczytaj|opisz)\s+(?:mi\s+)?(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        string what = match.Success ? Tail(match.Groups[1].Value) : "";
        instruction = what.Length == 0 || ConversationMemoryService.Normalize(what).Contains("ekran", StringComparison.Ordinal)
            ? DescribeScreenInstruction
            : "Na tym zrzucie ekranu jest fragment: „" + what + "”. Odpowiedz tylko na podstawie obrazu i nie dopisuj nic, czego nie widać.";
        return true;
    }

    public static bool TrySemanticQuery(string raw, string text, out string query)
    {
        query = "";
        if (!Regex.IsMatch(text, @"^szukaj\s+(semantycznie|podobnie)|^semantycznie\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200))) return false;
        Match match = Regex.Match(raw, @"^szukaj\s+(?:semantycznie|podobnie)\s*[:\-]?\s*(.+)$|^semantycznie\s*[:\-]?\s*(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        if (!match.Success) return false;
        query = Tail(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
        return query.Length > 0 && query.Length <= 200;
    }

    /// <summary>Nazwa i kroki brane z originalu — dzielimy po pierwszym „=”, bez liczenia offsetów.</summary>
    public static bool TryRoutineCreate(string raw, string text, out string name, out string steps)
    {
        name = ""; steps = "";
        if (!Regex.IsMatch(text, @"^(?:(?:utworz|dodaj|zapisz|stworz)\s+)?sekwencj", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200))) return false;
        int equals = raw.IndexOf('=');
        if (equals <= 0 || equals >= raw.Length - 1) return false;
        string head = raw[..equals].Trim();
        int colon = head.IndexOf(':');
        string label = colon >= 0 ? head[(colon + 1)..].Trim() : head;
        label = Regex.Replace(label, @"^(?:utworz|dodaj|zapisz|stworz|utwórz)\s+", "", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)).Trim();
        label = Regex.Replace(label, @"^sekwencj\w*\s*", "", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)).Trim();
        name = TrimWord(label);
        steps = raw[(equals + 1)..].Trim();
        return name.Length is > 0 and <= 60 && steps.Length > 0;
    }

    /// <summary>„uruchom sekwencję: poranek”, „podgląd sekwencji poranek”, „usuń sekwencję: poranek”.
    /// Wstęp rozpoznajemy na tekście znormalizowanym, nazwę wycinamy z originala — dlatego dwa wzorce.</summary>
    public static bool TryRoutine(string verb, string raw, string text, out string name)
    {
        name = "";
        string pattern = verb switch
        {
            "podglad" => @"^(?:podglad|podgląd|podlad|podgląd|preview)\s+sekwencj\w*\s*[:\-]?\s*(.+)$",
            "usun" => @"^(?:usun|usuń|usuwam)\s+sekwencj\w*\s*[:\-]?\s*(.+)$",
            _ => @"^(?:uruchom|wlacz|włącz|start|odtworz|odtwórz|wykonaj)\s+sekwencj\w*\s*[:\-]?\s*(.+)$",
        };
        if (!Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200))) return false;
        Match match = Regex.Match(raw, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        if (!match.Success) return false;
        name = TrimWord(match.Groups[1].Value);
        return name.Length is > 0 and <= 60;
    }

    private static string TrimWord(string value) => (value ?? "").Trim().Trim('"', '”', '„', '\'', '.', ',').Trim();

    /// <summary>Czyskiwany ogon argumentu: cudzysłowy, dwukropek na początku, oddechy na końcu.</summary>
    private static string Tail(string value)
    {
        string text = (value ?? "").Trim();
        text = text.TrimStart(':', '-', ' ');
        text = TrimWord(text);
        if (text.EndsWith('?') || text.EndsWith('!')) text = text[..^1];
        return text.Trim();
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    private static string Describe(WeatherOutcome outcome) =>
        outcome.Message + "\n\n" + (outcome.Success ? "Źródło: " + outcome.Evidence : "Co dalej: sprawdź internet i Ustawienia → Jarvis. " + outcome.Evidence);

    private static string Describe(MediaOutcome outcome) => outcome.Message + "\n\nDowód: " + outcome.Evidence;
    private static string Describe(HomeOutcome outcome) => outcome.Message + (outcome.Evidence.Length > 0 ? "\n\nDowód: " + outcome.Evidence : "");
    private static string Describe(RoutineResult result) => result.Message + (result.Evidence.Length > 0 ? "\n\nDowód: " + result.Evidence : "");
}
