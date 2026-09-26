using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace SentinelX.Utilities;

/// <summary>
/// 0.96 · wykończenie okna po windowsowemu: ciemny pasek tytułu, zaokrąglone rogi i tło Mica/Akryl.
/// Wszystko przez atrybuty DWM — bez nowych pakietów i bez WindowChrome. Trzy uczciwe ograniczenia:
///  • atrybuty 20/33 wymagają Windows 10 1903/1809, a tło (38) realnie działa od Windows 11 22H2;
///  • na starszym systemie każda próba kończy się kodem błędu, który tu jest po prostu ignorowany,
///    a okno zostaje na tle z tokenów Sx* — żaden element interfejsu nie znika;
///  • efekt przeszklenia widać tylko tam, gdzie okno nie jest zamalowane, dlatego dla Mica/Akryl
///    podkład okna dostaje częściową przezroczystość LICZONĄ z tokenu SxBackgroundColor (nie z liczb
///    w kodzie), a przy „Brak” wraca do pełnego krycia.
/// </summary>
public static class WindowEffects
{
    private const int UseImmersiveDarkMode = 20;
    private const int WindowCornerPreference = 33;
    private const int SystemBackdropType = 38;
    private const int SpRound = 2;

    /// <summary>Stosuje ustawienia do okna. Zawsze bezpieczne: błąd DWM = brak efektu, nie wyjątek.</summary>
    public static string Apply(Window window, string backdrop, bool darkTitleBar)
    {
        if (window == null) return "Brak okna — efekt nie zastosowany.";
        IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return "Okno nie ma jeszcze uchwytu — efekt zostanie pominięty.";
        var notes = new List<string>();
        TrySet(hwnd, UseImmersiveDarkMode, darkTitleBar ? 1 : 0, "ciemny pasek tytułu", notes);
        TrySet(hwnd, WindowCornerPreference, (int)SpRound, "zaokrąglone rogi", notes);
        int kind = backdrop switch { "Mica" => 2, "Akryl" => 3, "Brak" => 1, _ => 0 };
        if (kind == 0) kind = 2;
        bool glass = kind is 2 or 3;
        TrySet(hwnd, SystemBackdropType, kind, glass ? "tło " + backdrop : "tło zwykłe", notes);
        TryExtendFrame(hwnd, glass);
        ApplyWash(window, glass);
        return notes.Count == 0 ? "Windows 10 nie udostępnił efektów DWM — okno wygląda dokładnie tak jak w motywie." : "Zastosowano: " + string.Join(", ", notes) + ".";
    }

    /// <summary>Przezroczystość podkładu liczona z tokenu motywu, z powrotem na pełne krycie przy „Brak”.</summary>
    private static void ApplyWash(Window window, bool glass)
    {
        try
        {
            if (!glass) { if (window.Background is SolidColorBrush solid && solid.Opacity < 0.99) window.ClearValue(System.Windows.Controls.Control.BackgroundProperty); return; }
            if (window.TryFindResource("SxBackgroundColor") is not Color color) return;
            window.Background = new SolidColorBrush(Color.FromArgb(216, color.R, color.G, color.B));
        }
        catch (Exception ex) { AppLog.Write(ex); }
    }

    private static void TryExtendFrame(IntPtr hwnd, bool glass)
    {
        try
        {
            var margins = new MARGINS { left = -1, right = -1, top = -1, bottom = glass ? -1 : 0 };
            _ = DwmExtendFrameIntoClientArea(hwnd, ref margins);
        }
        catch (Exception ex) { AppLog.Write(ex); }
    }

    private static void TrySet(IntPtr hwnd, int attribute, int value, string label, List<string> notes)
    {
        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
            int status = DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
            // 0x80070057 (E_INVALIDARG) = system nie zna tego atrybutu; 0x8000FFFF = brak wsparcia.
            if (status == 0) notes.Add(label);
        }
        catch (Exception ex) { AppLog.Write(ex); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int left, right, top, bottom; }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
}
