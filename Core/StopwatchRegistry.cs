namespace SentinelX.Core;

/// <summary>0.95 · stoper sesji (jak w każdym asystencie): „stoper start”, „stoper” (ile leci),
/// „stoper stop”. Żyje w pamięci procesu — po restarcie startuje od zera, bez udawania.</summary>
public static class StopwatchRegistry
{
    private static readonly object gate = new();
    private static DateTime? startedAt;

    public static bool IsRunning { get { lock (gate) return startedAt != null; } }

    /// <summary>Startuje stoper. Zwraca opis; already = był już uruchomiony (nie resetuję).</summary>
    public static string Start()
    {
        lock (gate)
        {
            if (startedAt != null) return "Stoper już działa od " + startedAt.Value.ToString("HH:mm:ss") + " (" + FormatElapsed(DateTime.Now - startedAt.Value) + "). Napisz „stoper stop”, aby zatrzymać.";
            startedAt = DateTime.Now;
            return "Stoper wystartował o " + startedAt.Value.ToString("HH:mm:ss") + ". Napisz „stoper”, aby sprawdzić, albo „stoper stop”.";
        }
    }

    /// <summary>Zatrzymuje i zwraca wynik; gdy nie działał — informuje o tym.</summary>
    public static string Stop()
    {
        lock (gate)
        {
            if (startedAt == null) return "Stoper nie działa. Napisz „stoper start”, aby wystartować.";
            TimeSpan elapsed = DateTime.Now - startedAt.Value;
            startedAt = null;
            return "Stoper zatrzymany: " + FormatElapsed(elapsed) + ".";
        }
    }

    /// <summary>Bieżący czas albo informacja, że stoper nie działa.</summary>
    public static string Status()
    {
        lock (gate)
        {
            return startedAt == null
                ? "Stoper nie działa. Napisz „stoper start”, aby wystartować."
                : "Stoper leci: " + FormatElapsed(DateTime.Now - startedAt.Value) + " (start o " + startedAt.Value.ToString("HH:mm:ss") + ").";
        }
    }

    public static string FormatElapsed(TimeSpan span)
    {
        if (span.TotalHours >= 1) return (int)span.TotalHours + " godz " + span.Minutes + " min " + span.Seconds + " s";
        if (span.TotalMinutes >= 1) return span.Minutes + " min " + span.Seconds + " s";
        return span.Seconds + "," + (span.Milliseconds / 100).ToString("0") + " s";
    }
}
