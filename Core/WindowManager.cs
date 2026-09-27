using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace SentinelX.Core;

/// <summary>Jedno okno z listy „okna”: uchwyt i tytuł. Uchwyt jest wartością systemu,
/// ważną tylko do momentu zamknięcia okna — dlatego nigdy nie jest zapisywany.</summary>
public sealed record WindowInfo(IntPtr Handle, string Title);

/// <summary>0.96 · JARVIS nad pulpitem: lista okien, przełączanie, dokowanie, minimalizacja,
/// maksymalizacja i zamykanie aktywnego okna. Wszystko przez user32 — bez WinForms, bez pakietów.
/// <para>Zasada: zamykanie wysyła WM_CLOSE, czyli to samo, co kliknięcie „X” — program może
/// zapytać o zapisanie pracy. Sentinel nie zabija procesów.</para></summary>
public static class WindowManager
{
    private const int SW_MINIMIZE = 6;
    private const int SW_MAXIMIZE = 3;
    private const int SW_RESTORE = 9;
    private const uint WM_CLOSE = 0x0010;
    private const int MaxTitleLength = 200;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// <summary>Widoczne okna z niepustym tytułem, w kolejności systemu. Pusta lista = brak sesji pulpitu.</summary>
    public static IReadOnlyList<WindowInfo> ListWindows(int max = 25)
    {
        var found = new List<WindowInfo>();
        IntPtr shell;
        try { shell = GetShellWindow(); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return []; }

        EnumWindowsProc callback = (handle, _) =>
        {
            if (found.Count >= max) return false;
            try
            {
                if (handle == shell || !IsWindowVisible(handle)) return true;
                string title = TitleOf(handle);
                if (title.Length == 0 || title.Length > MaxTitleLength || title == "Program Manager") return true;
                found.Add(new WindowInfo(handle, title));
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
            return true;
        };
        try { EnumWindows(callback, IntPtr.Zero); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return []; }
        GC.KeepAlive(callback);
        return found;
    }

    /// <summary>Tytuł okna albo „” (okno bez tytułu, nieistniejący uchwyt, brak user32).</summary>
    public static string TitleOf(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return "";
        try
        {
            int length = GetWindowTextLengthW(handle);
            if (length <= 0) return "";
            var builder = new StringBuilder(Math.Min(length + 1, MaxTitleLength));
            return GetWindowTextW(handle, builder, builder.Capacity) > 0 ? builder.ToString() : "";
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return ""; }
    }

    /// <summary>Aktywne okno (pierwszy plan). Zwraca null, gdy nie udało się odczytać.</summary>
    public static WindowInfo? Foreground()
    {
        try
        {
            IntPtr handle = GetForegroundWindow();
            if (handle == IntPtr.Zero) return null;
            string title = TitleOf(handle);
            return title.Length == 0 ? null : new WindowInfo(handle, title);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }

    /// <summary>Czy okno należy do procesu Sentinela. Dzięki temu „zamknij okno” nie potrafi
    /// zamknąć samego asystenta, gdy jego okno jest akurat na pierwszym planie.</summary>
    public static bool IsOwnWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return false;
        try
        {
            GetWindowThreadProcessId(handle, out uint processId);
            return processId != 0 && processId == (uint)Environment.ProcessId;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    public static bool MinimizeActive() => Show(WindowAction.Minimize);
    public static bool MaximizeActive() => Show(WindowAction.Maximize);
    public static bool RestoreActive() => Show(WindowAction.Restore);

    /// <summary>Wysyła WM_CLOSE do aktywnego okna — to samo co „X”. Program może odmówić (np. zapytać o zapis).</summary>
    public static bool CloseActive()
    {
        try
        {
            IntPtr handle = GetForegroundWindow();
            if (handle == IntPtr.Zero) return false;
            SendMessageW(handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    /// <summary>Przełącza na okno, którego tytuł zawiera fragment (bez rozróżniania wielkości liter).
    /// Zwraca okno, które zostało wybrane, albo null, gdy nic nie pasuje lub system odmówił.</summary>
    public static WindowInfo? Activate(string fragment)
    {
        if (string.IsNullOrWhiteSpace(fragment)) return null;
        var windows = ListWindows(60);
        WindowInfo? match = windows.FirstOrDefault(x => x.Title.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        if (match == null) return null;
        try
        {
            if (IsIconic(match.Handle)) ShowWindow(match.Handle, SW_RESTORE);
            return SetForegroundWindow(match.Handle) ? match : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return null; }
    }

    private enum WindowAction { Minimize, Maximize, Restore }

    private static bool Show(WindowAction action)
    {
        try
        {
            IntPtr handle = GetForegroundWindow();
            if (handle == IntPtr.Zero) return false;
            return ShowWindow(handle, action switch
            {
                WindowAction.Minimize => SW_MINIMIZE,
                WindowAction.Maximize => SW_MAXIMIZE,
                _ => SW_RESTORE,
            });
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    /// <summary>Czytelna lista okien do odpowiedzi w czacie (tylko odczyt).</summary>
    public static string Describe(int max = 15)
    {
        var windows = ListWindows(max);
        if (windows.Count == 0) return "Nie widzę otwartych okien — ta sesja może nie mieć pulpitu (np. zdalna, bez GUI).";
        var active = Foreground();
        var lines = windows.Select((x, i) =>
            $"{i + 1}. {Truncate(x.Title, 70)}{(active != null && x.Handle == active.Handle ? "  ← aktywne" : "")}");
        return "Otwarte okna (" + windows.Count + "):\n" + string.Join("\n", lines) +
            "\nPrzełącz: „przełącz na: fragment tytułu”. Sterowanie: „minimalizuj okno”, „maksymalizuj okno”, „okno w lewo”.";
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";
}
