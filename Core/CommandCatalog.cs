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
        new("Pogoda teraz", "Open-Meteo · wymaga internetu · bez klucza API", "pogoda temperatura miasto forecast", CommandText: "pogoda"),
        new("Czy będzie padać", "Prognoza opadów na dziś dla miasta z ustawień", "pogoda deszcz snieg pada", CommandText: "czy będzie padać"),
        new("Co gra", "Stan odtwarzacza systemowego · bez czytania tytułu", "muzyka player spotify gra", CommandText: "co gra"),
        new("Pauza odtwarzacza", "Klawisz play/pause wysłany do systemu", "pauza play muzyka sterowanie", CommandText: "pauza"),
        new("Zrzut dla modelu", "Co jest na ekranie · lokalny model wizyjny", "ekran wizja okno opisz", CommandText: "co jest na ekranie"),
        new("Tryb agenta", "Czy model może prosić o narzędzia i jaki ma limit", "agent narzędzia tryb model", CommandText: "agent status"),
        new("Narzędzia agenta", "Pełna lista dozwolonych odczytów", "agent narzędzia lista", CommandText: "narzedzia agenta"),
        new("Sekwencje", "Nazwane listy kroków z podglądem przed wykonaniem", "sekwencje rutyny kroki automat", CommandText: "sekwencje"),
        new("Indeks semantyczny", "Status pamięci wektorowej · liczonej lokalnie", "pamiec semantyczna indeks wektory", CommandText: "indeks semantyczny"),
        new("Stan domu", "Home Assistant w sieci lokalnej · token ze zmiennej", "dom swiatlo home assistant scena", CommandText: "dom status"),
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
