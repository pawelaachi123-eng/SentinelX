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
