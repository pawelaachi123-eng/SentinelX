using SentinelX.Models;
namespace SentinelX.Core;
public static class CommandCatalog
{
    public static IReadOnlyList<PaletteEntry> Entries { get; } =
    [
        new("Ile mam RAM?", "Pomiar pamięci fizycznej · bez modelu AI", "pamiec memory ram", CommandText: "ile mam RAM"),
        new("Co zużywa pamięć?", "Lista procesów według użycia RAM", "procesy proces task ram", CommandText: "top procesy"),
        new("Sprawdź połączenie z internetem", "Diagnostyka sieci · wykonuje połączenia testowe", "siec ping network internet", CommandText: "test internetu"),
        new("Użycie CPU", "Odczyt obciążenia procesora", "cpu procesor performance", CommandText: "użycie CPU"),
        new("Użycie GPU", "Rzeczywisty licznik GPU; niedostępny odczyt będzie oznaczony", "gpu karta grafika", CommandText: "użycie GPU"),
        new("Diagnostyka komputera", "Raport tylko do odczytu", "doctor diagnostyka system zdrowie", CommandText: "diagnostyka komputera"),
        new("Uruchom kalkulator", "Uruchomienie aplikacji z weryfikacją procesu", "aplikacja app kalkulator calculator", CommandText: "uruchom kalkulator"),
        new("Utwórz notatkę", "Edytuj nazwę i treść przed wysłaniem", "plik notatka file note", CommandText: "utwórz plik notatka.txt: "),
        new("Wolna pamięć RAM", "Dostępna pamięć fizyczna · odczyt lokalny", "ram pamiec wolna available", CommandText: "wolny RAM"),
        new("Użycie RAM w procentach", "Procent wykorzystanej pamięci · z dowodem", "ram pamiec procent", CommandText: "procent RAM"),
        new("Wolne miejsce na dyskach", "Odczyt pojemności dysków stałych · bez AI", "dyski miejsce storage", CommandText: "wolne miejsce na dyskach"),
        new("Czas pracy komputera", "Uptime z zegara systemowego", "uptime czas", CommandText: "czas pracy komputera"),
        new("Dzisiejsza data", "Data lokalna · bez połączenia z siecią", "data kalendarz dzien", CommandText: "dzisiejsza data"),
        new("Która godzina?", "Lokalny zegar i przesunięcie strefy czasowej", "godzina zegar czas", CommandText: "która godzina"),
        new("Eksport historii JSON", "Zapis lokalny do 200 stanów akcji · zawiera prywatne dane", "eksport historia audit json", CommandText: "eksportuj historię json"),
        new("Eksport historii CSV", "Arkusz z ochroną przed formułami · do 200 stanów akcji", "eksport historia audit csv", CommandText: "eksportuj historię csv"),
        new("Otwarte okna", "Lista widocznych okien · tylko odczyt", "okna windows manager pulpit", CommandText: "okna"),
        new("Minimalizuj wszystko", "Powrót na pulpit · Windows+D", "minimalizuj okna pulpit", CommandText: "minimalizuj wszystko"),
        new("Play / pauza", "Sterowanie odtwarzaczem · klawisz multimediów", "muzyka multimedia pauza play", CommandText: "pauza"),
        new("Zablokuj ekran", "Windows+L · odwracalne hasłem", "blokada ekran lock", CommandText: "zablokuj ekran"),
        new("Restart komputera", "60 sekund na odwołanie", "restart zasilanie reboot", CommandText: "restart komputera"),
        new("Dzień dobry", "Briefing dnia i stanu komputera", "briefing poranek raport rano", CommandText: "dzień dobry"),
        new("Dobranoc", "Podsumowanie dnia i wyciszenie", "dobranoc wieczor podsumowanie", CommandText: "dobranoc"),
        new("Rutyny", "Twoje sekwencje kroków", "rutyna scena makro automat", CommandText: "rutyny"),
        new("Pomodoro 25", "Licznik pracy z przypomnieniem", "pomodoro praca licznik skupienie", CommandText: "pomodoro 25"),
        new("Historia schowka", "Ostatnie kopie tej sesji", "schowek clipboard historia", CommandText: "historia schowka"),
        new("Znajdź plik", "Szukanie po nazwie w folderach użytkownika", "plik szukanie finder nazwa", CommandText: "znajdź plik: "),
        new("Co pamiętasz?", "Odczyt profilu i trwałych wspomnień", "pamiec wspomnienia profil", CommandText: "co pamiętasz"),
        new("Nowa rozmowa", "Nowa sesja · bez usuwania wspomnień", "nowa rozmowa sesja", CommandText: "nowa rozmowa"),
        new("Usuń wszystkie wspomnienia", "HIGH · wymaga osobnej zgody w oknie", "usun pamiec prywatnosc", CommandText: "usuń wszystkie wspomnienia"),
        new("Zapisz odczyt diagnostyczny", "Obraz stanu w jednej chwili · tylko odczyt", "snapshot odczyt diagnostyka stan", CommandText: "snapshot"),
        new("Porównaj dwa odczyty", "Różnice między odczytami · bez wniosków o przyczynie", "snapshot porownanie roznice diagnostyka", CommandText: "porównaj snapshoty"),
        new("Eksportuj porównanie odczytów", "Zapis lokalny Markdown + JSON z SHA-256", "eksport snapshot porownanie raport", CommandText: "eksportuj porównanie"),
        new("Szukaj w rozmowie", "Szukanie wyłącznie w aktywnej rozmowie", "szukaj rozmowa tresc", CommandText: "szukaj w rozmowie: "),
        new("Eksportuj rozmowę (Markdown)", "Plik lokalny z treścią rozmowy · dane prywatne", "eksport rozmowa markdown", CommandText: "eksportuj rozmowę markdown"),
        new("Ponów odpowiedź", "Powtarza ostatnie polecenie; wynik może się różnić", "ponow powtorz jeszcze raz", CommandText: "ponów"),
        new("Dodaj zadanie", "Zadanie z terminem i priorytetem", "zadanie task lista", CommandText: "dodaj zadanie: "),
        new("Ustaw przypomnienie", "Termin musi być konkretny i zapisany za zgodą", "przypomnienie reminder alarm", CommandText: "przypomnij mi jutro o 18 o "),
        new("Rdzeń Sentinela", "Stan event busa, kolejki, flag, dziennika i bezpieczników", "rdzen core kolejka metryki", CommandText: "rdzen"),
        new("Zdrowie systemu", "Sprawdzenia zdrowia z czasem każdego kroku", "zdrowie health stan systemu", CommandText: "zdrowie"),
        new("Metryki i profil", "Liczniki, wskaźniki i histogramy (p50/p95)", "metryki pomiary profil wydajnosc", CommandText: "metryki"),
        new("Kolejka zadań", "Priorytety, ponowienia, kolejka zwrotów", "kolejka zadania priorytet retry", CommandText: "kolejka"),
        new("Cron — najbliższe terminy", "Opis wyrażenia i 5 następnych terminów", "cron harmonogram terminy", CommandText: "cron nastepne: 0 8 * * 1-5"),
        new("Flagi funkcji", "Zasięg procentowy i stan flag", "flagi feature flag", CommandText: "flagi"),
        new("Kopia danych", "ZIP z manifestem SHA-256 i rotacją", "kopia backup zip", CommandText: "kopia danych"),
        new("Sejf (AES-256-GCM)", "Sekrety lokalne; hasło wyłącznie z panelu", "sejf vault sekrety", CommandText: "sejf"),
        new("Diff tekstów", "Porównanie linii: dodane, usunięte, bez zmian", "diff porownaj teksty", CommandText: "diff: "),
        new("Regex — test", "Trafienia i grupy wzorca z limitem czasu", "regex wzorzec wyrazenie", CommandText: "regex: \\d+ | abc123"),
        new("IP i podsieci", "Sieć, rozgłoszenie, zakres hostów, podział", "ip podsiec maska cidr", CommandText: "ip: 192.168.1.10/24"),
        new("Kwota słownie", "Liczba po polsku z poprawną odmianą", "slownie liczba kwota", CommandText: "liczba slownie: 1234,56"),
        new("Statystyki tekstu", "Znaki, słowa, zdania, czytanie, najczęstsze słowa", "statystyki tekstu analiza", CommandText: "statystyki tekstu: "),
        new("Skróty i szablony plików", "Dockerfile, compose, CI, systemd, tsconfig…", "szablon dockerfile compose ci", CommandText: "szablony"),
        new("Skan kodu", "Języki, linie, TODO — tylko odczyt, z limitami", "skan kodu analiza todo", CommandText: "skan kodu: "),
        new("Zależności kodu", "Importy, moduły zewnętrzne, cykle", "zaleznosci kodu importy cykle", CommandText: "zaleznosci kodu: "),
        new("Audyt kodu (heurystyka)", "Wzorce ryzyka: eval, sekrety, słabe skróty", "bezpieczenstwo kodu sast audyt", CommandText: "bezpieczenstwo kodu: "),
        new("Studio narzędzi", "Wszystkie narzędzia lokalne w jednym miejscu: kategorie, przykłady, wynik", "studio narzedzia toolbox lokalne", PageKey: "studio"),
        new("Analiza danych", "Kwartyle, korelacja, regresja, histogram, metryki klasyfikacji", "analiza danych statystyki kwartyle korelacja regresja", PageKey: "studio:analiza"),
        new("Zdrowie — arytmetyka", "BMR, TDEE, makro, tętno, WHtR, 1RM, tempo, woda, sen, plan wagi", "zdrowie bmr tdee makro tetno waga tempo", PageKey: "studio:zdrowie"),
        new("Komunikacja", "Limity SMS i wpisu, szkice maila, agenda, protokół, ton, czytelność", "sms mail agenda protokol ton czytelnosc", PageKey: "studio:komunikacja"),
        new("Prywatność danych", "Mapa danych, duże pliki, wiek danych, podgląd retencji, szyfrowanie", "prywatnosc dane retencja szyfrowanie telemetria", PageKey: "studio:prywatnosc"),
        new("Modele lokalne", "Co zmieści się w RAM, role modeli, presety, KV cache", "modele lokalne model karta dopasuj kv", PageKey: "studio:modele"),
        new("Centrum", "Rozmowa i lokalne polecenia — wszystkie zakładki w jednym miejscu", "czat chat rozmowa centrum center", PageKey: "command"),
        new("Pamięć", "Wspomnienia, profil i prywatność", "pamiec memory wspomnienia", PageKey: "memory"),
        new("Projekty", "Kontekst projektów i ich notatki", "projekt projects kontekst", PageKey: "projects"),
        new("Zadania", "Zadania, terminy i przypomnienia", "zadania task przypomnienia", PageKey: "tasks"),
        new("Diagnostyka", "Odczyty stanu i ich porównania", "diagnostyka snapshot odczyt porownanie", PageKey: "diagnostics"),
        new("System", "CPU, RAM, dyski, sieć i procesy", "monitor statystyki", PageKey: "system"),
        new("Gaming", "Wykrywanie gry i nakładka z metrykami", "gra cs2 overlay nakladka", PageKey: "gaming"),
        new("Voice", "Mikrofon, modele i kalibracja głosu", "glos mikrofon vad asr voice", PageKey: "voice"),
        new("AI", "Połączenie z Ollama i wybór modelu", "ollama model ai", PageKey: "ai"),
        new("Actions", "Zadania, zgody i dowody wykonania", "akcje zadania uprawnienia permission dowody", PageKey: "actions"),
        new("History", "Historia akcji i rozmów", "historia audit log", PageKey: "history"),
        new("Settings", "Konfiguracja i walidacja ustawień", "ustawienia settings motyw wyglad", PageKey: "settings")
    ];
    public static IReadOnlyList<PaletteEntry> Search(string query)
    {
        string normalized = ConversationMemoryService.Normalize(query);
        var terms = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Entries.Select((entry, order) => new
        {
            Entry = entry, Order = order,
            Text = ConversationMemoryService.Normalize($"{entry.Title} {entry.Description} {entry.Keywords} {entry.CommandText}"),
            Title = ConversationMemoryService.Normalize(entry.Title)
        }).Where(item => terms.All(term => item.Text.Contains(term, StringComparison.Ordinal)))
          .OrderByDescending(item => normalized.Length > 0 && item.Title.StartsWith(normalized, StringComparison.Ordinal))
          .ThenBy(item => item.Order).Select(item => item.Entry).ToArray();
    }
}
