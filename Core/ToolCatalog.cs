namespace SentinelX.Core;

/// <summary>One row of the Tools page (0.95). Entries are data only — running a tool goes through
/// the normal engine path, so every tool keeps its permissions, evidence and history.
/// <paramref name="Preview"/> is the whole command for tools without arguments, and the command
/// prefix for tools that take one (<paramref name="Example"/> is then the sample argument).</summary>
public sealed record ToolEntry(string Id, string Category, string Title, string Hint, string Preview, bool TakesArgument, string Example);

/// <summary>The searchable catalogue behind „Narzędzia”. It lists what Sentinel really has:
/// every entry below was verified against the router (UtilityToolbox / read-only catalogue),
/// so the page can never offer something the assistant cannot do.</summary>
public static class ToolCatalog
{
    public const string AllCategories = "Wszystkie";

    public static IReadOnlyList<string> Categories { get; } =
    [
        AllCategories, "Warsztat 0.95", "Tekst i kody", "Liczby i czas", "Pliki i foldery",
        "Dokumenty PL", "System", "Losowe i rozrywka"
    ];

    public static IReadOnlyList<ToolEntry> Entries { get; } =
    [
        // ---------------- 0.95 · WARSZTAT (nowe narzędzia) ----------------
        new("diff", "Warsztat 0.95", "Porównaj dwa teksty", "Różnice linia po linii; separator ||| (albo linia ---)",
            "porownaj teksty:", true, "ala ma kota ||| ala ma psa"),
        new("regex", "Warsztat 0.95", "Test wyrażenia regularnego", "Dopasowania i grupy, limit 500 ms chroni przed zawieszeniem",
            "regex:", true, "\\d+ ||| mam 12 kotów i 3 psy"),
        new("filehash", "Warsztat 0.95", "Skrót SHA-256 i MD5 pliku", "Tylko odczyt; pliki do 2 GB; pokazuje rozmiar i datę zmiany",
            "sha256 pliku:", true, "pobrane\\setup.exe"),
        new("extract", "Warsztat 0.95", "Wyciągnij dane z tekstu", "E-maile, linki, adresy IPv4 i liczby — bez otwierania czegokolwiek",
            "wyciagnij:", true, "Napisz na biuro@example.com albo wejdź na https://example.com, IP 10.0.0.7"),
        new("sortlines", "Warsztat 0.95", "Posortuj wiersze", "Porządek alfabetyczny; wiersze rozdzielaj „ | ”, bo czat spłaszcza entery",
            "posortuj linie:", true, "zebra | kot | Ala"),
        new("uniquelines", "Warsztat 0.95", "Usuń duplikaty wierszy", "Zostawia pierwsze wystąpienie i mówi, ile usunął; separator „ | ”",
            "usun duplikaty linii:", true, "kot | pies | kot"),
        new("amount", "Warsztat 0.95", "Kwota słownie", "Poprawna polska odmiana złotych i groszy (do 999 999 999,99)",
            "kwota slownie:", true, "1234,56"),
        new("seconds", "Warsztat 0.95", "Sekundy na czas", "Zamienia sekundy na dni, godziny, minuty i sekundy",
            "sekundy:", true, "3661"),
        new("toseconds", "Warsztat 0.95", "Czas na sekundy", "Czyta zapis 2h 15m 10s, 90min, 1d",
            "na sekundy:", true, "2h 15m 10s"),
        new("passstrength", "Warsztat 0.95", "Moc hasła", "Entropia, typowe słabości i szacowany czas złamania; hasła nie zapisuję",
            "moc hasla:", true, "Tr0ub4dor&3-xK"),
        new("qr", "Warsztat 0.95", "Kod QR z tekstu", "Zapisuje PNG lokalnie; nic nie jest wysyłane",
            "qr:", true, "https://github.com/pawelaachi123-eng/SentinelX"),
        new("qrwifi", "Warsztat 0.95", "Kod QR do sieci Wi-Fi", "Format: nazwa sieci|hasło — telefon łączy się po zeskanowaniu",
            "qr wifi:", true, "MojaSiec|tajnehaslo"),

        // ---------------- tekst i kody ----------------
        new("words", "Tekst i kody", "Policz słowa i znaki", "Słowa, znaki, zdania — lokalnie", "ile slow:", true, "ala ma kota"),
        new("chars", "Tekst i kody", "Policz znaki i zdania", "Osobne liczniki znaków i zdań", "ile znakow:", true, "Ala ma kota. Kot ma Alę."),
        new("base64", "Tekst i kody", "Zakoduj base64", "Tekst → base64", "base64:", true, "tajna wiadomość"),
        new("base64back", "Tekst i kody", "Odkoduj base64", "base64 → tekst", "dekoduj base64:", true, "dGFqbmEgd2lhZG9tb8WbxIc="),
        new("texthash", "Tekst i kody", "Skrót SHA-256 tekstu", "Stały wynik dla tego samego tekstu", "hash tekstu:", true, "abc"),
        new("json", "Tekst i kody", "Sprawdź i sformatuj JSON", "Walidacja plus czytelne wcięcia", "json:", true, "{\"a\":1,\"b\":[2,3]}"),
        new("slug", "Tekst i kody", "Zrób slug", "Adres przyjazny dla URL, bez polskich znaków", "slug:", true, "Zażółć gęślą jaźń"),
        new("translit", "Tekst i kody", "Transliteruj", "Usuwa diakrytyki, zachowuje litery", "transliteruj:", true, "Zażółć gęślą jaźń"),
        new("morse", "Tekst i kody", "Kod Morse’a", "Zamiana tekstu na kropki i kreski", "morse:", true, "sos"),
        new("morseback", "Tekst i kody", "Odkoduj Morse’a", "Kropki i kreski → tekst", "dekoduj morse:", true, "... --- ..."),
        new("binary", "Tekst i kody", "Zapisz binarnie", "Bajty znaków w systemie dwójkowym", "binarnie:", true, "Ala"),
        new("binback", "Tekst i kody", "Odkoduj binarnie", "Ciąg zer i jedynek → tekst", "dekoduj binarnie:", true, "01000001 01101100 01100001"),
        new("hextxt", "Tekst i kody", "Zapisz w hex", "Kody bajtów w systemie szesnastkowym", "hex:", true, "Ala"),
        new("hexback", "Tekst i kody", "Odkoduj hex", "Hex → tekst", "dekoduj hex:", true, "416c61"),
        new("rot13", "Tekst i kody", "ROT13", "Klasyczne przesunięcie liter o 13", "rot13:", true, "ala ma kota"),
        new("title", "Tekst i kody", "Zmień na tytuł", "Każde słowo z wielkiej litery", "tytul:", true, "ala ma kota"),
        new("upper", "Tekst i kody", "Wielkie litery", "Cały tekst wielkimi literami", "wielkie litery:", true, "ala ma kota"),
        new("lower", "Tekst i kody", "Małe litery", "Cały tekst małymi literami", "male litery:", true, "ALA MA KOTA"),
        new("reverse", "Tekst i kody", "Odwróć tekst", "Znaki w odwrotnej kolejności", "odwroc tekst:", true, "kota"),
        new("palindrome", "Tekst i kody", "Sprawdź palindrom", "Czy czyta się tak samo w obie strony", "palindrom:", true, "kajak"),
        new("anagram", "Tekst i kody", "Sprawdź anagram", "Dwie części oddzielone przecinkiem", "anagram:", true, "kot, tok"),

        // ---------------- liczby i czas ----------------
        new("calc", "Liczby i czas", "Kalkulator", "Kolejność działań, nawiasy, potęgi, ułamki z przecinkiem",
            "policz 12,5*4 + (2^10)/4", false, ""),
        new("percentof", "Liczby i czas", "Procent z liczby", "Ile wynosi X% z Y", "procent:", true, "15 z 240"),
        new("share", "Liczby i czas", "Jaki to procent", "Jakim procentem całości jest część", "ile to procent:", true, "30 z 240"),
        new("vat", "Liczby i czas", "VAT 23%", "Netto → brutto (i odwrotnie z dopiskiem „brutto”)", "vat:", true, "100"),
        new("sqrt", "Liczby i czas", "Pierwiastek", "Pierwiastek kwadratowy", "pierwiastek:", true, "144"),
        new("factorial", "Liczby i czas", "Silnia", "Silnia liczby (bezpieczny zakres)", "silnia:", true, "10"),
        new("gcd", "Liczby i czas", "NWD i NWW", "Dwie liczby w jednej komendzie", "nwd 12 8", false, ""),
        new("prime", "Liczby i czas", "Czy liczba pierwsza", "Test pierwszości", "czy pierwsza 97", false, ""),
        new("divisors", "Liczby i czas", "Dzielniki liczby", "Wszystkie dzielniki", "dzielniki 12", false, ""),
        new("fib", "Liczby i czas", "Ciąg Fibonacciego", "Wybrany wyraz ciągu", "fibonacci 10", false, ""),
        new("stats", "Liczby i czas", "Średnia, mediana, suma", "Lista liczb po przecinkach", "srednia:", true, "2, 4, 6"),
        new("units", "Liczby i czas", "Przelicz jednostki", "km/mile, kg/lb, °C/°F, GB/MB i więcej", "przelicz:", true, "100 km na mile"),
        new("roman", "Liczby i czas", "Liczby rzymskie", "Cyfry arabskie ↔ rzymskie", "rzymskie 2026", false, ""),
        new("roll", "Liczby i czas", "Losuj z zakresu", "Zakres „od-do”", "losuj:", true, "1-100"),
        new("days", "Liczby i czas", "Ile dni do daty", "Też „ile dni od …”", "ile dni do:", true, "24.12"),
        new("weekday", "Liczby i czas", "Dzień tygodnia", "Jaki dzień wypada w danej dacie", "jaki dzien tygodnia:", true, "1.1.2030"),
        new("age", "Liczby i czas", "Ile mam lat", "Wiek z daty urodzenia", "wiek:", true, "01.01.1990"),
        new("workdays", "Liczby i czas", "Dni robocze", "Bez weekendów, w podanym przedziale", "dni robocze:", true, "1.1.2024 do 31.1.2024"),
        new("easter", "Liczby i czas", "Wielkanoc", "Data wielkanocy w danym roku", "wielkanoc:", true, "2027"),
        new("worldclock", "Liczby i czas", "Czas na świecie", "Strefy bez DST-zgadywania: tokio, londyn, nowy jork…", "czas w:", true, "tokio"),
        new("bmi", "Liczby i czas", "BMI", "Waga i wzrost, z uczciwym zastrzeżeniem", "bmi", true, "80 180"),
        new("weekofyear", "Liczby i czas", "Tydzień i dzień roku", "Numer tygodnia ISO i dzień roku", "tydzien roku", false, ""),

        // ---------------- pliki i foldery ----------------
        new("duplicates", "Pliki i foldery", "Znajdź duplikaty", "Identyczne treści (SHA-256) w folderze — tylko odczyt", "duplikaty:", true, "C:\\Users\\Ty\\Pobrane"),
        new("clutter", "Pliki i foldery", "Raport porządkowy", "Co zajmuje miejsce w folderze — tylko odczyt", "porzadki:", true, "C:\\Users\\Ty\\Pobrane"),
        new("openfolder", "Pliki i foldery", "Folder danych Sentinela", "Otwiera folder z rozmowami, wspomnieniami i raportami", "otworz folder sentinel", false, ""),
        new("snapshot", "Pliki i foldery", "Odcisk stanu komputera", "Zapis jednego odczytu CPU/RAM/dysków", "snapshot", false, ""),
        new("snapshots", "Pliki i foldery", "Lista odcisków stanu", "Zapisane odczyty i ich porównania", "snapshoty", false, ""),

        // ---------------- dokumenty PL ----------------
        new("pesel", "Dokumenty PL", "Sprawdź PESEL", "Suma kontrolna i data urodzenia, lokalnie", "pesel:", true, "90010112349"),
        new("nip", "Dokumenty PL", "Sprawdź NIP", "Suma kontrolna, lokalnie", "nip:", true, "1234567802"),
        new("iban", "Dokumenty PL", "Sprawdź IBAN", "Suma kontrolna i format, lokalnie", "iban:", true, "PL61 1090 1014 0000 0712 1981 2874"),
        new("color", "Dokumenty PL", "Kolor hex → RGB", "RGB, HSL i kontrast wobec czerni i bieli", "kolor:", true, "1fa2c3"),
        new("rgb", "Dokumenty PL", "Kolor RGB → hex", "Trzy liczby 0–255", "rgb 31 162 195", false, ""),

        // ---------------- system ----------------
        new("ip", "System", "Moje IP w sieci lokalnej", "Adresy IPv4 bez 127.0.0.1, bez sieci", "moje ip", false, ""),
        new("hostname", "System", "Nazwa komputera", "Nazwa maszyny, lokalnie", "nazwa komputera", false, ""),
        new("cores", "System", "Liczba rdzeni", "Rdzenie logiczne procesora", "ile rdzeni", false, ""),
        new("arch", "System", "Architektura", "System i proces aplikacji", "architektura", false, ""),
        new("version", "System", "Wersja", "Wersja Sentinel X, Windows i .NET", "wersja", false, ""),
        new("doctor", "System", "Diagnostyka komputera", "Raport tylko do odczytu z dowodami", "diagnostyka komputera", false, ""),
        new("security", "System", "Status zabezpieczeń", "Defender, zapora, aktualizacje — odczyt", "status zabezpieczen", false, ""),
        new("services", "System", "Lista usług", "Usługi Windows i ich stan", "lista uslug", false, ""),
        new("startupapps", "System", "Programy w autostarcie", "Co startuje razem z Windows", "programy autostartu", false, ""),
        new("events", "System", "Zdarzenia Windows", "Ostatnie zdarzenia systemowe", "zdarzenia windows", false, ""),

        // ---------------- losowe i rozrywka ----------------
        new("password", "Losowe i rozrywka", "Wygeneruj hasło", "Kryptograficzny generator, nigdzie nie zapisuje", "haslo:", true, "20"),
        new("pin", "Losowe i rozrywka", "Wygeneruj PIN", "4–12 cyfr", "pin:", true, "6"),
        new("uuid", "Losowe i rozrywka", "Wygeneruj UUID", "Identyfikator w wersji 4", "uuid", false, ""),
        new("lotto", "Losowe i rozrywka", "Lotto", "Sześć liczb z 49", "lotto", false, ""),
        new("coin", "Losowe i rozrywka", "Rzut monetą", "Orzeł albo reszka", "rzut moneta", false, ""),
        new("dice", "Losowe i rozrywka", "Rzut kością", "Jedna albo kilka kości", "rzuc kostka", false, ""),
        new("pick", "Losowe i rozrywka", "Wybierz losowo", "Wybór z listy po przecinkach", "wybierz losowo:", true, "pizza, sushi, pierogi")
    ];

    /// <summary>Filtering for the Tools page: a category chip plus a free-text query.
    /// An empty query shows everything in the chosen category, in catalogue order.</summary>
    public static IReadOnlyList<ToolEntry> Search(string? query, string? category)
    {
        string selected = string.IsNullOrWhiteSpace(category) ? AllCategories : category!;
        var byCategory = Entries.Where(entry => selected == AllCategories || entry.Category == selected);
        string normalized = ConversationMemoryService.Normalize(query ?? "");
        if (normalized.Length == 0) return byCategory.ToArray();
        string[] terms = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return byCategory.Where(entry =>
        {
            string haystack = ConversationMemoryService.Normalize($"{entry.Title} {entry.Hint} {entry.Preview} {entry.Example} {entry.Category}");
            return terms.All(term => haystack.Contains(term, StringComparison.Ordinal));
        }).ToArray();
    }

    /// <summary>The full command for a tool row: the argument (trimmed) replaces the sample when the tool takes one.</summary>
    public static string BuildCommand(ToolEntry entry, string? argument)
    {
        if (!entry.TakesArgument) return entry.Preview;
        string value = (argument ?? "").Trim();
        return value.Length == 0 ? entry.Preview : entry.Preview + " " + value;
    }

    public static ToolEntry? ById(string id) => Entries.FirstOrDefault(entry => entry.Id == id);
}
