namespace SentinelX.Core;

/// <summary>
/// 0.99 · RADA NAPRAWY — konkretny następny krok po porażce.
/// Zasada: rada ma być WYKONYWALNA (co otwórz, co sprawdź, co zmień), a nie ogólnikiem
/// typu „spróbuj ponownie”. Mapowana po rodzinie akcji (prefiks typu) i treści błędu.
/// </summary>
public static class RecoveryAdvisor
{
    private static readonly (string Family, string Advice)[] ByFamily =
    [
        ("MEASURE_", "Ponów odczyt wprost (np. „ile mam RAM?”) — jeśli znowu pusto, sprawdź w Menedżerze zadań, czy liczniki systemu odpowiadają."),
        ("OPEN_", "Sprawdź, czy aplikacja istnieje w Start; jeśli tak, uruchom ją ręcznie i podaj mi jej dokładną nazwę — dodam ją do rozpoznawanych."),
        ("CLOSE_", "Sprawdź nazwę procesu w Menedżerze zadań (zakładka Szczegóły) i podaj ją wprost — zamkam po nazwie procesu, nie po oknie."),
        ("CLEANUP", "Otwórz Ustawienia → System → Pamięć i sprawdź folder „Pliki tymczasowe”; mi powiedz, ile miejsca pokazuje — porównam z moim dowodem."),
        ("NETWORK_", "Sprawdź, czy inne urządzenia mają internet; jeśli tak — zrestartuj router i poproś mnie o ponowną diagnostykę sieci."),
        ("FILE_", "Podaj pełną ścieżkę pliku i sprawdź, czy nie otwierasz go w innym programie — zamknięcie blokady zwykle wystarcza."),
        ("SMARTHOME_", "Sprawdź, czy urządzenie odpowiada w aplikacji producenta; dopiero potem ponawiaj polecenie — nie powtarzam poleceń na ślepo."),
        ("MEMORY_", "Wypisz notatkę jeszcze raz krócej — długie notatki tną się przy zapisie; sprawdź ją potem na stronie Pamięć."),
    ];

    /// <summary>Wykonywalna rada naprawy; nigdy pusta — zawsze coś konkretnego.</summary>
    public static string Advise(string actionType, string error)
    {
        foreach ((string family, string advice) in ByFamily)
        {
            if (actionType.StartsWith(family, StringComparison.Ordinal))
            {
                return advice + " (przyczyna: " + Short(error) + ")";
            }
        }
        if (RetryAdvisor.TransientCause(error) is { Length: > 0 } cause)
        {
            return "Przyczyna wygląda na chwilową (" + cause + ") — odczekaj chwilę i ponów to samo polecenie.";
        }
        return "Sformułuj polecenie wprost i konkretnie (nazwa + działanie); historia dowodów po lewej pokaże, na którym kroku stanęło.";
    }

    private static string Short(string text)
    {
        string clean = (text ?? "").Replace("\n", " ").Trim();
        return clean.Length <= 90 ? clean : clean[..90] + "…";
    }
}
