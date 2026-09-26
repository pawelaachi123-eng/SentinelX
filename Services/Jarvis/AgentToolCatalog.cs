namespace SentinelX.Services.Jarvis;

/// <summary>Jedno narzędzie, o które może poprosić model. <see cref="Command"/> to zdanie,
/// które realnie idzie przez potok poleceń — czyli dokładnie to, co użytkownik mógłby wpisać.
/// Argument jest wstawiany w <c>{argument}</c> i zawsze cytowany w transkrypcji.</summary>
public sealed record AgentTool(string Name, string Description, string Command, bool TakesArgument = false, string ArgumentHint = "")
{
    public const string ArgumentToken = "{argument}";

    /// <summary>Definicja w formacie OpenAI/Ollama dla pola <c>tools</c>.</summary>
    public object ToDefinition()
    {
        var properties = new Dictionary<string, object>();
        if (TakesArgument) properties["argument"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = ArgumentHint };
        return new Dictionary<string, object>
        {
            ["type"] = "function",
            ["function"] = new Dictionary<string, object>
            {
                ["name"] = Name,
                ["description"] = Description,
                ["parameters"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = properties,
                    ["required"] = TakesArgument ? new[] { "argument" } : Array.Empty<string>()
                }
            }
        };
    }

    /// <summary>Zdanie do wykonania. Pusty argument nie zamienia się w „…” wysłane do narzędzia —
    /// wyciągamy wtedy samą komendę bazową, bo to ona ma domyślne zachowanie (np. „pogoda” = miasto z ustawień).</summary>
    public string BuildCommand(string argument)
    {
        if (!Command.Contains(ArgumentToken, StringComparison.Ordinal)) return Command;
        string value = (argument ?? "").Trim().Replace("\n", " ", StringComparison.Ordinal);
        if (value.Length > 240) value = value[..240];
        return value.Length == 0
            ? Command.Replace(" " + ArgumentToken, "", StringComparison.Ordinal).Trim()
            : Command.Replace(ArgumentToken, value, StringComparison.Ordinal);
    }

    public string Label => TakesArgument ? $"{Name}({ArgumentToken})" : Name;
}

/// <summary>
/// Katalog narzędzi agenta (0.96). Żelazna zasada: wyłącznie odczyty i liczenia.
/// Nic stąd nie usuwa plików, nic nie zatwierdza, nic nie zmienia stanu systemu poza
/// odczytem; polecenia niszczące są tu nieobecne tak samo jak w katalogu napraw literówek
/// (tests/UnderstandingRegression pilnuje, by „usun/zamknij/wyłącz/wyłącz komputer” nie weszły
/// do żadnego katalogu, który model może wybrać).
/// </summary>
public static class AgentToolCatalog
{
    public static IReadOnlyList<AgentTool> Tools { get; } = Build();

    public static AgentTool? Find(string name) =>
        Tools.FirstOrDefault(x => x.Name.Equals((name ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Opis dla strony AI i dla polecenia „agent: co umiesz”. Bez ukrytych możliwości.</summary>
    public static string Describe() =>
        "Dozwolone narzędzia agenta (" + Tools.Count + ", wyłącznie odczyty i liczenia):\n" +
        string.Join("\n", Tools.Select(x => "· `" + x.Label + "` — " + x.Description + " → „" + x.BuildCommand("…") + "”")) +
        "\n\nCzego agent NIE ma: usuwania plików, zamykania aplikacji, zmian w ustawieniach, sterowania domem, " +
        "czegokolwiek z „potwierdź”. Tego nie da się odblokować przez prompt.";

    private static IReadOnlyList<AgentTool> Build() =>
    [
        // system (read-only measurements)
        new("ram", "Ile pamięci RAM jest używane i ile jest razem", "ile mam ramu"),
        new("cpu", "Aktualne użycie procesora", "użycie CPU"),
        new("gpu", "Użycie karty graficznej; brak odczytu jest oznaczony, nie zerem", "użycie GPU"),
        new("disks", "Wolne miejsce na dyskach", "wolne miejsce na dyskach"),
        new("processes", "Procesy według użycia pamięci", "top procesy"),
        new("uptime", "Jak długo działa komputer", "czas pracy komputera"),
        new("battery", "Stan baterii", "stan baterii"),
        new("clock", "Lokalna godzina", "która godzina"),
        new("date", "Dzisiejsza data z dniem tygodnia", "dzisiejsza data"),
        new("network", "Test internetu: ping, DNS, czas odpowiedzi", "test internetu"),
        new("diagnostics", "Pełny raport diagnostyczny komputera (tylko odczyt)", "diagnostyka komputera"),
        new("version", "Wersja Sentinel X i systemu", "wersja"),
        // user's own local data
        new("memories", "Trwałe wspomnienia i profil użytkownika", "co pamiętasz"),
        new("tasks", "Otwarte zadania z terminami", "zadania"),
        new("reminders", "Aktywne przypomnienia", "przypomnienia"),
        new("projects", "Projekty i ich statusy", "projekty"),
        new("day_plan", "Plan na dziś: zadania, przypomnienia, przypięte wspomnienia", "plan dnia"),
        new("facts", "Ostatnie odczyty z tej sesji", "fakty"),
        new("search", "Wyszukiwanie po wszystkich lokalnych danych (wspomnienia, rozmowa, zadania, projekty)", "szukaj wszystkiego: " + AgentTool.ArgumentToken, true, "fraza do wyszukania"),
        // deterministic math tools — no network, no model guessing
        new("calculate", "Dokładne działanie arytmetyczne (własny parser, nie model)", "policz " + AgentTool.ArgumentToken, true, "wyrażenie, np. 19.99*3+4.5"),
        new("unit_convert", "Przeliczenie jednostek", "przelicz " + AgentTool.ArgumentToken, true, "np. 5 km na mile"),
        new("days_between", "Ile dni dzieli dwie daty", "ile dni miedzy " + AgentTool.ArgumentToken, true, "1.1.2026 a 24.12.2026"),
        // weather is the only tool that may leave the machine — and it is labelled as such
        new("weather", "Prognoza z Open-Meteo dla miasta (wymaga internetu, jawne w ustawieniach)", "pogoda " + AgentTool.ArgumentToken, true, "miasto, puste = miasto z ustawień"),
    ];
}

/// <summary>Jeden wybór modelu: identyfikator wezwania, nazwa funkcji i argument JSON.</summary>
public sealed record AgentToolCall(string Id, string Name, string RawArguments)
{
    /// <summary>Wyciąga <c>argument</c> z JSON-u narzędzia. Znosi zarówno obiekt, jak i string
    /// (mniejsze modele często odsyłają „{"argument":"Kraków"}” jako tekst).</summary>
    public string Argument
    {
        get
        {
            string json = (RawArguments ?? "").Trim();
            if (json.Length == 0) return "";
            if (json.StartsWith('"') && json.EndsWith('"') && json.Length > 1) json = json[1..^1];
            if (json.Length > 0 && json[0] != '{') return json;
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return "";
                foreach (System.Text.Json.JsonProperty property in document.RootElement.EnumerateObject())
                    if (property.Value.ValueKind == System.Text.Json.JsonValueKind.String) return property.Value.GetString() ?? "";
                foreach (System.Text.Json.JsonProperty property in document.RootElement.EnumerateObject())
                    return property.Value.ToString();
                return "";
            }
            catch (System.Text.Json.JsonException) { return ""; }
        }
    }
}
