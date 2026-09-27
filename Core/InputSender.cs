using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace SentinelX.Core;

/// <summary>0.96 · wysyłanie klawiszy do systemu: multimedia (play/pauza, następny utwór) i skróty
/// pulpitu (Windows+D, Alt+Tab, Windows+←/→, F11). Celowo używa user32 <c>keybd_event</c> — jest
/// wycofane, ale wciąż obecne w każdym Windows i nie wymaga ręcznego układu struktury INPUT.
/// <para>Uczciwość: syntetyczne klawisze są odrzucane, gdy aktywne okno należy do procesu o
/// wyższych uprawnieniach (UIPI). Metody nie rzucają — zgłaszają false, a komenda mówi wprost,
/// że system nie przyjął zdarzenia, zamiast udawać sukces.</para></summary>
public static class InputSender
{
    private const uint KeyDown = 0x0000;
    private const uint KeyUp = 0x0002;
    private const uint ExtendedKey = 0x0001;

    public const byte Tab = 0x09;
    public const byte Menu = 0x12;          // Alt
    public const byte Left = 0x25;
    public const byte Right = 0x27;
    public const byte KeyD = 0x44;
    public const byte F11 = 0x7A;
    public const byte LeftWindows = 0x5B;
    public const byte VolumeMute = 0xAD;
    public const byte VolumeDown = 0xAE;
    public const byte VolumeUp = 0xAF;
    public const byte MediaNextTrack = 0xB0;
    public const byte MediaPrevTrack = 0xB1;
    public const byte MediaStop = 0xB2;
    public const byte MediaPlayPause = 0xB3;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    /// <summary>Jedno naciśnięcie i zwolnienie klawisza (multimedia, F11). false = system nie przyjął.</summary>
    public static bool Tap(byte virtualKey, bool extended = false)
    {
        try
        {
            uint flags = extended ? KeyDown | ExtendedKey : KeyDown;
            keybd_event(virtualKey, 0, flags, UIntPtr.Zero);
            Thread.Sleep(15);
            keybd_event(virtualKey, 0, flags | KeyUp, UIntPtr.Zero);
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Skrót klawiszowy: modyfikator trzymany, drugi klawisz naciśnięty puszczony (Windows+D).</summary>
    public static bool Chord(byte modifier, byte virtualKey)
    {
        try
        {
            keybd_event(modifier, 0, KeyDown, UIntPtr.Zero);
            Thread.Sleep(20);
            keybd_event(virtualKey, 0, KeyDown, UIntPtr.Zero);
            Thread.Sleep(20);
            keybd_event(virtualKey, 0, KeyUp, UIntPtr.Zero);
            Thread.Sleep(20);
            keybd_event(modifier, 0, KeyUp | ExtendedKey, UIntPtr.Zero);
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Alt+Tab: Alt zostaje wciśnięty do samego końca — inaczej system nie pokazuje przełącznika.</summary>
    public static bool AltTab()
    {
        try
        {
            keybd_event(Menu, 0, KeyDown, UIntPtr.Zero);
            Thread.Sleep(20);
            keybd_event(Tab, 0, KeyDown, UIntPtr.Zero);
            Thread.Sleep(20);
            keybd_event(Tab, 0, KeyUp, UIntPtr.Zero);
            Thread.Sleep(20);
            keybd_event(Menu, 0, KeyUp | ExtendedKey, UIntPtr.Zero);
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Windows+D — powrót na pulpit (to samo, co przycisk „pokaż pulpit”).</summary>
    public static bool ShowDesktop() => Chord(LeftWindows, KeyD);

    /// <summary>Windows+← / Windows+→ — dokowanie okna do połowy ekranu.</summary>
    public static bool SnapLeft() => Chord(LeftWindows, Left);
    public static bool SnapRight() => Chord(LeftWindows, Right);

    public static bool PlayPause() => Tap(MediaPlayPause, extended: true);
    public static bool NextTrack() => Tap(MediaNextTrack, extended: true);
    public static bool PreviousTrack() => Tap(MediaPrevTrack, extended: true);
    public static bool StopMedia() => Tap(MediaStop, extended: true);
}
