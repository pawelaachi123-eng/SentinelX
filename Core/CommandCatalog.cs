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
                // 0.97 · automatyzacja i narzędzia z listy 1550 (lokalnie: harmonogram, watchdog, kopie, dane)
        new("Zaplanuj polecenie", "Harmonogram w aplikacji · bez rejestracji w systemie", "cron harmonogram zaplanuj schedule", CommandText: "zaplanuj: 7:30 "),
        new("Harmonogram", "Lista zaplanowanych poleceń", "zaplanowane harmonogram cron lista", CommandText: "zaplanowane"),
        new("Obserwuj folder", "Podgląd zmian w folderze · bez automatycznych akcji", "watchdog folder obserwuj zmiany pliki", CommandText: "obserwuj: "),
        new("Co nowego w folderze", "Ostatnie zdarzenia w obserwowanych folderach", "watchdog zdarzenia nowe pliki", CommandText: "co nowego w folderze"),
        new("Kopie zapasowe", "Lista archiwów ZIP i folderów z manifestem", "backup kopie archiwum rotacja zip", CommandText: "kopie zapasowe"),
        new("Spójność danych", "SHA-256 każdego pliku danych · tylko odczyt", "integrity sha256 sumy kontrolne spójność", CommandText: "spójność danych"),
        new("Dziennik zdarzeń", "Lokalny dziennik JSON z rotacją", "logi dziennik json zdarzenia audit", CommandText: "dziennik"),
        new("Nowy pulpit wirtualny", "Ctrl+Win+D · pulpity Windows", "pulpit wirtualny desktop vdesk", CommandText: "nowy pulpit"),
        new("Siła hasła", "Ocena hasła w pamięci · bez zapisu i bez sieci", "hasło bezpieczeństwo siła password", CommandText: "siła hasła: "),
        new("Analizuj CSV", "Profil danych: kolumny, braki, duplikaty", "csv dane profil kolumny statystyka", CommandText: "analizuj csv: "),
        new("Przeszukaj pliki", "Treść plików tekstowych · tylko odczyt", "szukaj treść grep pliki tekst", CommandText: "przeszukaj pliki: "),
        new("Porównaj pliki", "Różnice linia po linii (diff)", "diff porównaj pliki różnice", CommandText: "porównaj pliki: "),
        new("Eksport kalendarza", "Terminy i przypomnienia do pliku .ics", "kalendarz ics eksport terminy", CommandText: "eksportuj kalendarz"),
        new("Bezpieczny schowek", "Auto-czyszczenie schowka po 30 s", "schowek bezpieczeństwo clipboard czyść", CommandText: "schowek auto 30"),
        new("Mapa 1550 funkcji", "Co Sentinel ma z listy 1550, a czego nie i dlaczego", "mapa 1550 funkcje zakres lista", CommandText: "mapa funkcji"),
        new("Inflacja", "Realna wartość kwoty po latach", "inflacja pieniądz wartość procent", CommandText: "inflacja: 1000 5 3"),
        new("Cel oszczędzania", "Ile miesięcy do celu", "oszczędzanie cel finansowy wkład", CommandText: "cel oszczędzania: 20000 1500 4"),
        new("Spłata długu", "Kula śnieżna czy lawina · liczby, nie domysły", "dług spłata snowball avalanche", CommandText: "spłata długu: 5000 200 12"),
        new("BMR i TDEE", "Zapotrzebowanie kalorii ze wzoru", "bmr tdee kalorie metabolizm dieta", CommandText: "bmr: 80 180 30 m"),
        new("Cykle snu", "Pory pobudki z pełnych cykli 90 min", "sen cykle pobudka regeneracja", CommandText: "cykle snu: 23:00"),
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
