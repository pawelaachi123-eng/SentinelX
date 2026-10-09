namespace SentinelX.Core;

/// <summary>Known, non-destructive command phrases and stems, in normalized form.
/// Typo repair only ever rewrites text towards these — destructive commands (delete, clear, kill,
/// confirm) are deliberately absent, so a mistyped word can never reach them.</summary>
public static class IntentCatalog
{
    /// <summary>Documented short forms. Deterministic and listed by the „skróty” command.</summary>
    public static IReadOnlyDictionary<string, string> Abbreviations { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["mz"] = "uruchom menedzer zadan",
        ["dk"] = "diagnostyka komputera",
        ["sp"] = "status pamieci",
        ["sn"] = "snapshot",
        ["sns"] = "snapshoty",
        ["cs"] = "wlacz cs2",
        ["dc"] = "wlacz discord",
        ["st"] = "wlacz steam",
        ["ch"] = "wlacz chrome",
        ["nt"] = "wlacz notatnik",
        ["kl"] = "wlacz kalkulator",
        ["yt"] = "wlacz youtube",
        ["zs"] = "status zabezpieczen",
        ["tp"] = "top procesy",
        ["ti"] = "test internetu",
        ["zd"] = "zadania",
        ["pr"] = "projekty",
        ["pm"] = "co pamietasz",
        ["sd"] = "samokontrola",
        ["pp"] = "propozycje",
        ["lk"] = "lekcje",
        ["im"] = "moje ip",
        ["nc"] = "nazwa komputera"
    };

    public static IReadOnlyList<string> Phrases { get; } = Build();

    private static IReadOnlyList<string> Build()
    {
        var phrases = new HashSet<string>(StringComparer.Ordinal);
        // Read-only measurements are already a curated alias table — reuse it instead of duplicating.
        foreach (string alias in ReadOnlyIntentCatalog.Aliases.Keys) phrases.Add(alias);

        string[] extra =
        [
            // launching and folders
            "wlacz cs2", "wlacz discord", "wlacz steam", "wlacz chrome", "wlacz brave", "wlacz spotify",
            "wlacz notatnik", "wlacz kalkulator", "wlacz youtube", "wlacz vs code", "wlacz firefox", "wlacz edge",
            "wlacz vlc", "wlacz obs", "wlacz minecraft", "wlacz valorant", "wlacz league of legends",
            "wlacz epic games", "wlacz battle net", "wlacz telegram", "wlacz whatsapp", "wlacz paint",
            "uruchom cs2", "uruchom discord", "uruchom steam", "uruchom chrome", "uruchom brave",
            "uruchom notatnik", "uruchom kalkulator", "uruchom menedzer zadan", "uruchom eksplorator",
            "otworz ustawienia", "otworz pobrane", "otworz dokumenty", "otworz pulpit", "otworz folder sentinel",
            // diagnostics, network, processes
            "top procesy", "co zjada ram", "test internetu", "status sieci", "historia akcji",
            "diagnostyka komputera", "raport komputera", "eksportuj raport", "status zabezpieczen",
            "zdarzenia windows", "programy autostartu", "lista uslug",
            // ai
            "modele ai", "status ai", "test ai", "lista modeli", "sprawdz ai", "model ai auto",
            // memory and conversations
            "co pamietasz", "status pamieci", "ile pamietasz", "pokaz rozmowy", "lista rozmow", "moje rozmowy",
            "nowa rozmowa", "nowa sesja", "co poszlo do modelu", "co powiedzialem wczesniej",
            "szukaj w rozmowie", "eksportuj rozmowe markdown", "eksportuj pamiec",
            // tasks, projects, snapshots
            "zadania", "lista zadan", "przypomnienia", "dodaj zadanie",
            "projekty", "lista projektow", "moje projekty", "pokaz projekty", "aktywny projekt", "status projektu",
            "snapshot", "snapshoty", "lista snapshotow", "porownaj snapshoty", "eksportuj porownanie",
            // archives, backup, help
            "archiwizuj rozmowy", "archiwa", "backup", "statystyki", "pomoc", "co umiesz", "skroty",
            "toolbox", "narzedzia sentinel",
            // utility stems (argument commands)
            "ile dni do", "ile dni od", "jaki dzien tygodnia", "haslo", "generuj haslo", "uuid", "guid",
            "ile slow", "policz slowa", "przelicz", "konwertuj", "procent", "ile to procent", "vat",
            "base64", "dekoduj base64", "hash tekstu", "sha256", "json", "sprawdz json", "slug",
            "transliteruj", "wielkie litery", "male litery", "odwroc tekst", "losuj", "rzuc kostka",
            "wybierz losowo", "bmi", "rzymskie", "z rzymskich", "kolor", "kalkulator", "policz",
            "szukaj wszystkiego", "podsumuj dzien", "plan dnia", "co dzis",
            // 0.91 · CENTRUM: offline tools, Polish identifiers, system facts and meta commands
            "ile znakow", "ile zdan", "palindrom", "anagram", "rot13", "tytul",
            "morse", "dekoduj morse", "binarnie", "dekoduj binarnie", "hex", "dekoduj hex",
            "pesel", "nip", "iban", "rgb",
            "pierwiastek", "silnia", "nwd", "nww", "czy pierwsza", "dzielniki", "fibonacci",
            "srednia", "mediana", "suma", "min", "max", "zaokraglij", "zmiana procentowa",
            "rzut moneta", "lotto", "pin",
            "czas w toki", "czas w londyn", "czas w berlin", "czas w paryz", "czas w nowy jork",
            "czas w chicago", "czas w los angeles", "czas w seoul",
            "tydzien roku", "dzien roku", "ile dni do konca roku", "wiek", "dni robocze", "wielkanoc",
            "moje ip", "nazwa komputera", "ile rdzeni", "architektura",
            "wersja", "co nowego", "lekcje", "samokontrola", "propozycje",
            "szukaj zadan", "zrob zadanie", "notatka",
            // 0.95 · WARSZTAT: nowe narzędzia offline i strona z katalogiem
            "porownaj teksty", "diff", "regex", "sprawdz wzor", "sha256 pliku", "md5 pliku", "hash pliku",
            "wyciagnij", "wyciagnij z tekstu", "posortuj linie", "posortuj wiersze",
            "unikalne linie", "tylko unikalne linie", "kwota slownie", "slownie",
            "sekundy", "na sekundy", "ile to sekund", "moc hasla", "sila hasla", "qr", "qr wifi", "narzedzia",
            // Roblox/Luau source templates, heuristic audits and offline OBJ primitives.
            "roblox", "roblox pomoc", "roblox szablony", "roblox kod", "roblox gra", "roblox projekt", "szablony roblox", "luau pomoc",
            "stworz gre na robloxie", "zrob gre na robloxie", "zbuduj gre na robloxie", "stworz gre roblox", "create a roblox game", "build a roblox game",
            "luau sprawdz", "sprawdz luau", "model 3d", "modeluj 3d", "druk 3d", "zbuduj model 3d", "generuj model 3d", "zrob model 3d"
        ];
        foreach (string phrase in extra) phrases.Add(phrase);
        return phrases.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Every word that appears in a known phrase — the vocabulary used for word-level repair.</summary>
    public static IReadOnlySet<string> Vocabulary { get; } = new HashSet<string>(
        Phrases.SelectMany(phrase => phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Concat(["leaderstats", "sprint", "checkpoint", "remote", "remoteevent", "shop", "cube", "box", "plane", "sphere", "cylinder", "cone", "kostka", "klocek", "kula", "walec", "stozek", "platforma",
                "simulator", "symulator", "sim", "obby", "parkour", "tycoon", "magnat", "rounds", "survival", "przetrwanie", "rundy", "custom", "wlasna", "sandbox", "crystal", "miner"]),
        StringComparer.Ordinal);
}
