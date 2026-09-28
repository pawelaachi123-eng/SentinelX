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
            // 0.94 · zrozumienie, pamięć i nowe narzędzia offline
            "zrozum", "co wiesz o mnie", "podsumuj rozmowe", "fakty", "ostatnie fakty",
            "eksportuj historie json", "eksportuj historie csv", "eksportuj historie",
            "wznow rozmowe", "przypomnij", "zapamietaj",
            "znizka", "napiwek", "raty", "rata kredytu", "odsetki", "procent skladany",
            "logarytm", "potega", "modulo", "reszta z dzielenia", "wartosc bezwzgledna",
            "sin", "cos", "tan", "sinus", "cosinus", "tangens", "srednia wazona",
            "rownanie kwadratowe", "kalendarz", "dodaj dni", "odejmij dni", "ile dni miedzy",
            "rok przestepny", "kwartal",
            "ean", "isbn", "luhn", "karta platnicza", "regon",
            "literuj", "czestotliwosc slow", "powtorzenia slow", "skrable", "punkty scrabble",
            "posortuj slowa", "bez powtorzen", "odwroc slowa", "tylko cyfry", "tylko litery",
            "wylosuj karte", "kostka", "cytat",
            "nazwa uzytkownika", "rozdzielczosc ekranu", "bateria", "stan baterii",
            "strefa czasu", "czas w strefie", "czas utc",
            // 0.95 · Jarvis: timer/stoper/budzik, głośność, schowek, zrzuty, self-repair/self-improve
            "timer", "budzik", "stoper", "stoper start", "stoper stop",
            "glosnosc", "wycisz", "przywroc dzwiek", "co w schowku", "kopiuj",
            "zrzut ekranu", "screenshot", "napraw sie", "ulepsz sie",
            // 0.96 · JARVIS: okna, multimedia, zasilanie, rutyny, briefing, schowek, pliki
            "okna", "lista okien", "minimalizuj wszystko", "minimalizuj okno", "maksymalizuj okno",
            "przywroc okno", "przelacz okno", "okno w lewo", "okno w prawo",
            "pelny ekran", "przelacz na",
            "pauza", "wstrzymaj", "wznow odtwarzanie", "nastepny utwor", "poprzedni utwor",
            "glosniej", "ciszej",
            "zablokuj ekran", "wygasz ekran", "uspij komputer",
            "restart komputera", "anuluj zamkniecie",
            "rutyny", "dodaj rutyne", "uruchom rutyne", 
            "dzien dobry", "briefing", "dobranoc", "pomodoro", "przerwa", "skupienie",
            "historia schowka", "znajdz plik", "szukaj pliku",
            // 0.97 · RDZEŃ + narzędzia deweloperskie, analiza kodu, finanse i produktywność
            "stan rdzenia", "kolejka zadan", "kolejka przetworz", "kolejka zwrotow", "zwroty",
            "cron opis", "cron nastepne", "flagi funkcji", "ustaw flage",
            "zdrowie systemu", "metryki rdzenia", "dziennik json", "bezpieczniki", "cykl zycia", "maszyna stanow",
            "workflow", "dag", "cache zapisz", "cache pokaz", "integralnosc plikow", "integralnosc zbuduj",
            "kopia danych", "kopie danych", "weryfikuj kopie", "sejf", "sejf pokaz", "sejf dodaj",
            "sejf odblokuj", "sejf zablokuj", "serializuj", "deserializuj",
            "diff", "regex", "semver", "podsiec", "jwt", "uuid7", "ulid",
            "json csharp", "sql tabela", "sql z json", "mock json", "base32", "base58", "crc32",
            "kody znakow", "z ascii", "csv markdown", "csv json", "spis tresci", "commit",
            "szablon", "szablony", "tokeny", "kontekst",
            "liczba slownie", "lorem", "statystyki tekstu", "roi", "break even", "amortyzacja",
            "inflacja", "oszczednosci", "macierz", "slajdy",
            "skan kodu", "statystyki kodu", "zaleznosci kodu", "bezpieczenstwo kodu",
            "duplikaty kodu", "licencje", "funkcje kodu", "drzewo kodu",
            // 0.97 · SEKCJA 2 (pierwszy przyrost) — modele lokalne: katalog i dobór, bez pobierania
            "modele lokalne", "katalog modeli", "katalog llm",
            "model karta", "model dopasuj", "model rola", "model audyt",
            "kwantyzacje", "kwantyzacja", "presety modelu", "preset modelu", "parametry modelu",
            "prompt szablony", "prompt szablon", "szablony promptow",
            "model kolejka", "model limity", "model polityka",
            "model kv", "model pamiec", "model porownaj", "model offline", "tryb bez modelu",
            // 0.97 · SEKCJA 2 (drugi przyrost) — zarządzanie: dobór do zadania, licencje, zgoda dwuetapowa
            "model do zadania", "dobierz model", "model licencje", "licencje modeli", "model licencja",
            "model info", "model uruchomione", "model procesy", "model status pobierania", "status pobierania modelu",
            "model pobierz", "pobierz model", "model kopiuj",
            // Uwaga: „model usun” celowo poza katalogiem — kontrakt bezpieczeństwa (destructive verbs
            // nigdy nie są kandydatami naprawy). Polecenie obsługuje regex w CommandRouter.
            // 0.97 · POZOSTAŁE SEKCJE — architektura (5), full-stack (6), wiedza (8), research (12),
            // smart home (18), cele (20), media (9–10), język/automatyzacja (7, 11, 13)
            "moduly", "cykle", "sprzezenie", "warstwy", "dlug techniczny", "adr", "styl", "c4", "kapacyt",
            "pojemnosc kolejki", "latencja", "migracja bazy", "wdrozenie kanary", "karta modulu",
            "api szkielet", "openapi", "encja ts", "encja csharp", "migracja sql", "sql indeks", "compose",
            "cors", "env", "dostep", "status http", "rest tabela", "walidacja", "relacja", "paginacja",
            "fiszki", "anki", "powtorki", "slownik pojec", "podobienstwo", "wspolne tematy", "mapa wiedzy",
            "indeks pojec", "pytania kontrolne",
            "energia", "koszt urzadzen", "termostat", "scena dom", "yaml automatyzacji", "mqtt", "prad",
            "luminy", "czujnik baterii", "tarif",
            "kontrast", "ppi", "proporcje", "bitrate wideo", "audio czas", "audio rozmiar", "tempo mowy", "db",
            "cytuj", "bibliografia", "wiarygodnosc", "plan badan", "slowa kluczowe", "zapytanie",
            "macierz porownania", "podsumuj notatki", "fakt zapisz", "pytania badawcze",
            "cel rozloz", "plan krokow", "czas na zadanie", "plan wycofania", "polityka autonomii",
            "ryzyko", "samoocena", "definicja sukcesu",
            "jezyk", "i18n", "webhook szablon", "token bucket", "retry plan", "sesje", "koszt spotkania",
            "godziny pracy", "plan tygodnia",
            // 0.97 · dokładki po green-CI: wykres jako obraz (19), indeks plików w RAM (8)
            "wykres", "indeks zbuduj", "indeks buduj", "indeks szukaj", "indeks znajdz", "indeks status", "status indeksu",
            "rag zbuduj", "rag szukaj", "rag status", "rag reset", "rag prompt", "rag model",
            // 0.98 · WiFi i Game Dev (Roblox)
            "wifi", "wifi on", "wifi off", "wifi status", "szukaj w sieci", "strona",
            "roblox nauka", "roblox plan nauki", "roblox struktura", "roblox projektowanie", "roblox modelowanie",
            "roblox budowanie", "roblox optymalizacja", "roblox wydajnosc", "roblox checklist", "roblox checklista",
            "roblox monetyzacja", "roblox skrypt", "roblox pojecie", "roblox szkic", "roblox gdd",
            "roblox najlepsze", "roblox szukaj", "roblox przyklad", "roblox nowosci", "roblox najnowsze",
            "roblox wygeneruj", "roblox generator",
            // 0.97 · SEKCJA 15 — analiza danych (nazwy własne, więc mogą stać w katalogu naprawy)
            "statystyki liczb", "analiza liczb", "opis zbioru", "kwartyle", "odchylenie", "wariancja",
            "skosnosc", "kurtoza", "wspolczynnik zmiennosci", "przedzial ufnosci", "korelacja", "regresja",
            "prognoza", "histogram", "normalizuj", "standaryzuj", "odleglosc", "macierz pomylek",
            "dokladnosc klasyfikacji", "outliery", "wygladzanie", "rangi", "percentyl", "test t",
            // 0.97 · SEKCJE 17 i 14 — zdrowie i komunikacja
            "bmr", "tdee", "makro", "hrmax", "tetno maksymalne", "whtr", "whr", "max powtorzen",
            "deficyt", "agenda", "protokol", "notatka ze spotkania", "follow up", "skroc do", "czytelnosc",
            // 0.97 · SEKCJA 3 — prywatność
            "prywatnosc", "audyt danych", "gdzie sa moje dane", "duze pliki danych", "retencja",
            "wiek danych", "szyfrowanie", "uprawnienia", "co wysylam", "eksport danych", "minimalizacja"
        ];
        foreach (string phrase in extra) phrases.Add(phrase);
        return phrases.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Every word that appears in a known phrase — the vocabulary used for word-level repair.</summary>
    public static IReadOnlySet<string> Vocabulary { get; } = new HashSet<string>(
        Phrases.SelectMany(phrase => phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries)),
        StringComparer.Ordinal);
}
