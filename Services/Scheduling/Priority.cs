namespace SentinelX.Services.Scheduling;

/// <summary>
/// Priorytety zadań w kolejce tła. Polecenie użytkownika (USER/CRITICAL) ma zawsze
/// pierwszeństwo nad indeksowaniem i konserwacją (BACKGROUND/IDLE).
/// </summary>
public enum JobPriority
{
    Idle = 0,        // najniższy — kosmetyka, czyszczenie cache
    Background = 10, // indeksowanie, prefetch, aktualizacje
    Normal = 20,     // domyślne akcje systemowe
    User = 40,       // polecenie użytkownika (głos / czat)
    Critical = 80    // zdrowie systemu, crash recovery, stop awaryjny
}
