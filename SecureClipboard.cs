using System.Text;
using System.Windows;

namespace SentinelX;

/// <summary>0.97 · secure clipboard: optional auto-clear after N seconds (#145). Clearing happens on the UI thread via <see cref="Poll"/>.</summary>
public static class SecureClipboard
{
    private static DateTime clearAt = DateTime.MinValue;
    private static bool armed;

    public const int DefaultSeconds = 30;
    public const int MaxSeconds = 3600;

    public static bool Armed => armed;
    public static DateTime ClearAt => armed ? clearAt : DateTime.MinValue;

    /// <summary>Ile sekund po skopiowaniu wyczyścić schowek (0 = wyłączone). Ustawiane komendą „schowek auto 30”.</summary>
    public static int AutoClearSeconds { get; set; }

    public static TimeSpan Remaining(DateTime now) => armed && clearAt > now ? clearAt - now : TimeSpan.Zero;

    /// <summary>Ustawia tekst w schowku i (opcjonalnie) uzbraja automatyczne czyszczenie.</summary>
    public static bool Set(string text, int autoClearSeconds = 0, DateTime? now = null)
    {
        if (autoClearSeconds < 0 || autoClearSeconds > MaxSeconds) autoClearSeconds = Math.Clamp(autoClearSeconds, 0, MaxSeconds);
        try
        {
            Clipboard.SetText(text ?? "");
            armed = autoClearSeconds > 0;
            clearAt = armed ? (now ?? DateTime.Now).AddSeconds(autoClearSeconds) : DateTime.MinValue;
            return true;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            armed = false; clearAt = DateTime.MinValue;
            AppLog.Write("SecureClipboard", "Nie udało się zapisać do schowka: " + ex.Message);
            return false;
        }
    }

    /// <summary>Wołane z tiku interfejsu: czyści schowek, gdy minął czas. Zwraca true, gdy faktycznie wyczyścił.</summary>
    public static bool Poll(DateTime? now = null)
    {
        if (!armed) return false;
        if ((now ?? DateTime.Now) < clearAt) return false;
        ClearInternal();
        return true;
    }

    public static bool Clear(DateTime? now = null)
    {
        bool wasArmed = armed;
        ClearInternal(now);
        return wasArmed;
    }

    public static string Describe(DateTime now)
    {
        var builder = new StringBuilder();
        if (!armed)
        {
            builder.AppendLine("BEZPIECZNY SCHOWEK · automatyczne czyszczenie jest wyłączone.");
            builder.AppendLine("Włącz: schowek auto 30 — wtedy zawartość kopiowana komendą „kopiuj:” zniknie po 30 s.");
            return builder.ToString().TrimEnd();
        }
        TimeSpan left = Remaining(now);
        builder.AppendLine("BEZPIECZNY SCHOWEK · czyszczenie za " + (left.TotalSeconds < 1 ? 0 : (int)Math.Ceiling(left.TotalSeconds)) + " s.");
        builder.AppendLine("Wyczyść teraz: wyczyść schowek · wyłącz: schowek auto wyłącz.");
        builder.AppendLine("Czyszczenie działa tylko wtedy, gdy aplikacja jest uruchomiona — Sentinel nie steruje schowkiem w tle po zamknięciu.");
        return builder.ToString().TrimEnd();
    }

    private static void ClearInternal(DateTime? now = null)
    {
        armed = false; clearAt = DateTime.MinValue;
        try { Clipboard.Clear(); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            AppLog.Write("SecureClipboard", "Nie udało się wyczyścić schowka: " + ex.Message);
        }
    }
}
