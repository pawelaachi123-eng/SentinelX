namespace SentinelX.Core;

public enum SlashKind { Command, Tab, Page }

/// <summary>One entry of the „//” palette in the chat box. Entries either run a chat command,
/// switch a Centrum tab, or open a top-level page. Nothing here executes on its own —
/// the user still picks the entry explicitly (Tab + Enter or click).</summary>
public sealed record SlashEntry(string Trigger, string Label, string Hint, SlashKind Kind, string Target);

public static class SlashCatalog
{
    public static IReadOnlyList<SlashEntry> Entries { get; } =
    [
        // 0.96 · JARVIS: desktop, media, power, routines (all local, all reversible in time)
        new("okna", "Otwarte okna", "lista widocznych okien · tylko odczyt", SlashKind.Command, "okna"),
        new("minimalizuj", "Minimalizuj wszystko", "powrót na pulpit · Windows+D", SlashKind.Command, "minimalizuj wszystko"),
        new("przelacz", "Przełącz okno", "Alt+Tab", SlashKind.Command, "przełącz okno"),
        new("pauza", "Play / pauza", "klawisz multimediów", SlashKind.Command, "pauza"),
        new("nastepny", "Następny utwór", "klawisz multimediów", SlashKind.Command, "następny utwór"),
        new("glosniej", "Głośniej", "+10% głośności", SlashKind.Command, "głośniej"),
        new("ciszej", "Ciszej", "-10% głośności", SlashKind.Command, "ciszej"),
        new("zablokuj", "Zablokuj ekran", "Windows+L · odwracalne", SlashKind.Command, "zablokuj ekran"),
        new("wygasz", "Wygasz ekran", "monitor w stan spoczynku", SlashKind.Command, "wygasz ekran"),
        new("restart", "Restart komputera", "60 s na odwołanie", SlashKind.Command, "restart komputera"),
        new("anulujzamkniecie", "Anuluj zamknięcie", "odwołuje odliczanie", SlashKind.Command, "anuluj zamknięcie"),
        new("rutyny", "Rutyny", "Twoje sekwencje kroków", SlashKind.Command, "rutyny"),
        new("poranek", "Rutyna poranek", "uruchamia startową rutynę", SlashKind.Command, "uruchom rutynę: poranek"),
        new("rano", "Dzień dobry", "briefing dnia i komputera", SlashKind.Command, "dzień dobry"),
        new("dobranoc", "Dobranoc", "podsumowanie dnia", SlashKind.Command, "dobranoc"),
        new("pomodoro", "Pomodoro 25", "licznik pracy z przypomnieniem", SlashKind.Command, "pomodoro 25"),
        new("schowek", "Historia schowka", "ostatnie kopie tej sesji", SlashKind.Command, "historia schowka"),
        new("plik", "Znajdź plik", "szukanie po nazwie · tylko odczyt", SlashKind.Command, "znajdź plik: "),
        // 0.97 · automatyzacja i narzędzia z listy 1550 (lokalnie, bez chmury i bez rejestracji w systemie)
        new("zaplanuj", "Zaplanuj polecenie", "np. //zaplanuj 7:30 dzień dobry · działa w aplikacji", SlashKind.Command, "zaplanuj: 7:30 "),
        new("zaplanowane", "Harmonogram", "lista zaplanowanych poleceń", SlashKind.Command, "zaplanowane"),
        new("obserwuj", "Obserwuj folder", "podgląd zmian · bez automatycznych akcji", SlashKind.Command, "obserwuj: "),
        new("co nowego", "Co nowego w folderze", "ostatnie zmiany w plikach", SlashKind.Command, "co nowego w folderze"),
        new("kopie", "Kopie zapasowe", "ZIP + foldery z manifestem i rotacją", SlashKind.Command, "kopie zapasowe"),
        new("spojnosc", "Spójność danych", "SHA-256 plików Sentinela · tylko odczyt", SlashKind.Command, "spójność danych"),
        new("dziennik", "Dziennik zdarzeń", "JSON z rotacją · lokalny plik", SlashKind.Command, "dziennik"),
        new("pulpit", "Nowy pulpit wirtualny", "Ctrl+Win+D", SlashKind.Command, "nowy pulpit"),
        new("haslo", "Siła hasła", "ocena w pamięci · bez zapisywania", SlashKind.Command, "siła hasła: "),
        new("csv", "Analizuj CSV", "profil kolumn, braków i duplikatów", SlashKind.Command, "analizuj csv: "),
        new("plikach", "Przeszukaj pliki", "treść plików tekstowych · tylko odczyt", SlashKind.Command, "przeszukaj pliki: "),
        new("kalendarz", "Eksport kalendarza", "terminy i przypomnienia do .ics", SlashKind.Command, "eksportuj kalendarz"),
        new("schowekauto", "Bezpieczny schowek 30 s", "auto-czyszczenie po skopiowaniu", SlashKind.Command, "schowek auto 30"),
        new("mapa", "Mapa 1550 funkcji", "co mam, a czego nie i dlaczego", SlashKind.Command, "mapa funkcji"),
        new("inflacja", "Inflacja", "ile realnie wart jest Twój pieniądz", SlashKind.Command, "inflacja: 1000 5 3"),
        new("bmr", "BMR / TDEE", "zapotrzebowanie kalorii ze wzoru", SlashKind.Command, "bmr: 80 180 30 m"),
        // system facts (read-only, with evidence)
        new("diag", "Diagnostyka komputera", "pełny raport tylko do odczytu", SlashKind.Command, "diagnostyka komputera"),
        new("diagnostyka", "Diagnostyka komputera", "alias: to samo co //diag", SlashKind.Command, "diagnostyka komputera"),
        new("ram", "Ile mam RAM", "pomiar pamięci fizycznej", SlashKind.Command, "ile mam RAM"),
        new("cpu", "Użycie CPU", "odczyt obciążenia procesora", SlashKind.Command, "użycie CPU"),
        new("dysk", "Pokaz dyski", "pojemność i wolne miejsce", SlashKind.Command, "pokaz dyski"),
        new("procesy", "Top procesy", "co zużywa RAM i CPU", SlashKind.Command, "top procesy"),
        new("internet", "Test internetu", "ping, DNS i połączenie", SlashKind.Command, "test internetu"),
        new("ip", "Moje IP", "lokalny adres IPv4, bez sieci", SlashKind.Command, "moje ip"),
        // memory and work
        new("pamiec", "Co pamiętasz", "profil i wspomnienia", SlashKind.Command, "co pamietasz"),
        new("zadania", "Zadania", "lista otwartych zadań", SlashKind.Command, "zadania"),
        new("przypomnienia", "Przypomnienia", "lista aktywnych przypomnień", SlashKind.Command, "przypomnienia"),
        new("projekty", "Projekty", "lista projektów", SlashKind.Command, "projekty"),
        new("snapshot", "Snapshot", "zapisz odczyt stanu", SlashKind.Command, "snapshot"),
        new("snapshoty", "Snapshoty", "lista zapisanych odczytów", SlashKind.Command, "snapshoty"),
        new("backup", "Backup", "kopię danych z SHA-256", SlashKind.Command, "backup"),
        new("archiwa", "Archiwa", "miesięczne archiwa rozmów", SlashKind.Command, "archiwa"),
        new("archiwizuj", "Archiwizuj rozmowy", "przenieś stare miesiące do archiwum", SlashKind.Command, "archiwizuj rozmowy"),
        // Sentinel about Sentinel
        new("samokontrola", "Samokontrola", "spójność plików i magazynów", SlashKind.Command, "samokontrola"),
        new("propozycje", "Propozycje", "co warto zrobić — decyzja należy do Ciebie", SlashKind.Command, "propozycje"),
        new("lekcje", "Lekcje", "czego nauczyłem się z Twoich poprawek", SlashKind.Command, "lekcje"),
        new("wersja", "Wersja", "wersja aplikacji i systemu", SlashKind.Command, "wersja"),
        new("nowego", "Co nowego", "skrócona lista zmian 0.91", SlashKind.Command, "co nowego"),
        // everyday helpers
        new("pomoc", "Pomoc", "pełna lista poleceń", SlashKind.Command, "pomoc"),
        new("skroty", "Skróty", "tabelka skrótów klawiszowych", SlashKind.Command, "skróty"),
        new("plan", "Plan dnia", "zadania i przypomnienia na dziś", SlashKind.Command, "plan dnia"),
        new("statystyki", "Statystyki", "ile danych przechowuję lokalnie", SlashKind.Command, "statystyki"),
        new("godzina", "Która godzina", "lokalny zegar", SlashKind.Command, "ktora godzina"),
        new("data", "Dzisiejsza data", "lokalna data", SlashKind.Command, "dzisiejsza data"),
        new("haslo", "Generuj hasło", "16 znaków, lokalnie", SlashKind.Command, "haslo 16"),
        new("uuid", "Generuj UUID", "losowy identyfikator", SlashKind.Command, "uuid"),
        new("lotto", "Lotto", "6 losowych liczb z 49", SlashKind.Command, "lotto"),
        // 0.92 · safe file work
        new("duplikaty", "Duplikaty w folderze", "raport identycznych treści (SHA-256), tylko odczyt", SlashKind.Command, "duplikaty: "),
        new("porzadki", "Raport porządkowy", "co zajmuje miejsce w folderze, tylko odczyt", SlashKind.Command, "porzadki: "),
        new("sprzatanie", "Usuń duplikaty", "zostawia 1 plik w grupie, reszta do Kosza po zgodzie", SlashKind.Command, "usuń duplikaty: "),
        new("nazwy", "Zmień nazwy plików", "podgląd zmian, wykonanie dopiero po zgodzie", SlashKind.Command, "zmien nazwy: "),
        // panels inside Centrum and pages
        new("rozmowa", "Rozmowa", "wróć do czatu", SlashKind.Tab, "rozmowa"),
        new("historia", "Historia", "zakładka Historii", SlashKind.Tab, "historia"),
        new("glos", "Głos", "zakładka Głosu", SlashKind.Tab, "glos"),
        new("system", "System", "zakładka Systemu", SlashKind.Tab, "system"),
        new("gry", "Gaming", "zakładka Gier", SlashKind.Tab, "gry"),
        new("ai", "AI", "zakładka AI", SlashKind.Tab, "ai"),
        new("akcje", "Akcje", "zakładka Akcji", SlashKind.Tab, "akcje"),
        new("ustawienia", "Ustawienia", "strona Ustawień", SlashKind.Page, "settings"),
    ];

    /// <summary>Exact-match resolution for a typed „//trigger” (without the slashes).</summary>
    public static SlashEntry? TryResolve(string trigger) =>
        Entries.FirstOrDefault(x => string.Equals(x.Trigger, trigger.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Filtering for the live palette. Empty query shows the curated highlights.</summary>
    public static IReadOnlyList<SlashEntry> Filter(string query)
    {
        string normalized = ConversationMemoryService.Normalize(query ?? "");
        if (normalized.Length == 0) return Entries.Take(9).ToList();
        var terms = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Entries
            .Select(entry => (Entry: entry, Score: Score(entry, terms)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Entry)
            .ToList();
    }

    private static int Score(SlashEntry entry, string[] terms)
    {
        string trigger = ConversationMemoryService.Normalize(entry.Trigger);
        string label = ConversationMemoryService.Normalize(entry.Label);
        string hint = ConversationMemoryService.Normalize(entry.Hint);
        int score = 0;
        foreach (string term in terms)
        {
            if (trigger == term) score += 100;
            else if (trigger.StartsWith(term, StringComparison.Ordinal)) score += 60;
            else if (trigger.Contains(term, StringComparison.Ordinal)) score += 30;
            if (label.Contains(term, StringComparison.Ordinal)) score += 15;
            if (hint.Contains(term, StringComparison.Ordinal)) score += 5;
        }
        return score;
    }
}
