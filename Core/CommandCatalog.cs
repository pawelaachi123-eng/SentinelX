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
        new("Command Center", "Rozmowa i lokalne polecenia", "czat chat rozmowa centrum", PageKey: "command"),
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
