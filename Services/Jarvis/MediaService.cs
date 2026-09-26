using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace SentinelX.Services.Jarvis;

/// <summary>Klawisze multimedialne, które rozumie system Windows (te same, co są na klawiaturze).</summary>
public enum MediaKey { PlayPause, Next, Previous, Stop, Mute, VolumeUp, VolumeDown }

/// <summary>Wynik akcji multimedialnej: tekst + dowód. Brak potwierdzenia nie jest udawany sukcesem.</summary>
public sealed record MediaOutcome(bool Success, string Message, string Evidence)
{
    public static MediaOutcome Sent(MediaKey key, string label, int inserted) => inserted > 0
        ? new MediaOutcome(true, label + " — klawisz systemowy wysłany.\nSkutku nie potwierdzam osobnym odczytem: Windows nie udostępnia nam tytułu utworu bez integracji ze SMTC.",
            $"SendInput INPUT_KEYBOARD vk={MediaKeys.Name(key)} · wstrzyknięte zdarzenia: {inserted} · {DateTimeOffset.Now:O}")
        : new MediaOutcome(false, label + " — system odrzucił wstrzyknięcie klawisza (0 zdarzeń). Sesja bez pulpitu albo blokada polityką.",
            $"SendInput INPUT_KEYBOARD vk={MediaKeys.Name(key)} zwróciło 0");
    public static MediaOutcome Failed(string message, string evidence = "") => new(false, message, evidence);
    public static MediaOutcome Verified(string message, string evidence) => new(true, message, evidence);

}

/// <summary>Kody wirtualne klawiszy multimedialnych i ich nazwy do dowodu. Osobna klasa, żeby
/// zarówno wynik (opis), jak i transporter (wysyłka) czytały tę samą tablicę.</summary>
internal static class MediaKeys
{
    internal static string Name(MediaKey key) => key switch
    {
        MediaKey.PlayPause => "VK_MEDIA_PLAY_PAUSE (0xB3)", MediaKey.Next => "VK_MEDIA_NEXT_TRACK (0xB0)",
        MediaKey.Previous => "VK_MEDIA_PREV_TRACK (0xB1)", MediaKey.Stop => "VK_MEDIA_STOP (0xB2)",
        MediaKey.Mute => "VK_VOLUME_MUTE (0xAD)", MediaKey.VolumeUp => "VK_VOLUME_UP (0xAF)",
        _ => "VK_VOLUME_DOWN (0xAE)"
    };

    internal static ushort VirtualKey(MediaKey key) => key switch
    {
        MediaKey.PlayPause => 0xB3, MediaKey.Next => 0xB0, MediaKey.Previous => 0xB1, MediaKey.Stop => 0xB2,
        MediaKey.Mute => 0xAD, MediaKey.VolumeUp => 0xAF, _ => 0xAE
    };
}

/// <summary>
/// 0.96 · sterowanie odtwarzaniem. Świadomie NIE używamy WinRT/SMTC: wymagałoby to zmiany
/// TFM na net9.0-windows10.0.xxxxx, a bramka architektury pilnuje net9.0-windows. Zamiast tego
/// wysyłamy te same klawisze, które wysyła klawiatura multimedialna — obsługuje je Spotify, VLC,
/// przeglądarka, Winamp i każdy inny odtwarzacz podpięty do systemowej transmisji mediów.
/// Status odtwarzacza ustalamy z listy procesów (uczciwie: nazwa aplikacji, nie tytuł utworu).
/// </summary>
public sealed class MediaService
{
    private readonly Action<MediaKey>? sender;

    /// <summary>Znane odtwarzacze i edytory multimediów — pierwszy trafiony proces wygrywa.</summary>
    internal static readonly (string Process, string Label)[] Players =
    [
        ("spotify", "Spotify"), ("spotifywebhelper", "Spotify (w przeglądarce)"),
        ("vlc", "VLC media player"), ("wmplayer", "Windows Media Player"),
        ("musicbee", "MusicBee"), ("foobar2000", "foobar2000"), ("winamp", "Winamp"),
        ("itunes", "Apple iTunes"), ("powerdvd14", "CyberLink PowerDVD"), ("potplayermini", "Daum PotPlayer"),
        ("potplayer", "Daum PotPlayer"), ("mpv", "mpv"), ("mplayer", "MPlayer"),
        ("aimp", "AIMP"), ("irfanview", "IrfanView"), ("firefox", "Firefox"), ("chrome", "Chrome"),
        ("msedge", "Edge"), ("brave", "Brave"), ("opera", "Opera"), ("youtubemusic", "YouTube Music"),
        ("tidal", "TIDAL"), ("qobuz", "Qobuz"), ("discord", "Discord (muzyka bot)"),
    ];

    /// <summary>tworzy serwis; <paramref name="sender"/> pozwala podstawić transporter w testach.</summary>
    public MediaService(Action<MediaKey>? sender = null) => this.sender = sender;

    public MediaOutcome Send(MediaKey key, string label)
    {
        if (sender != null) { sender(key); return MediaOutcome.Sent(key, label, 1); }
        try
        {
            int inserted = InjectKey(MediaKeys.VirtualKey(key));
            return MediaOutcome.Sent(key, label, inserted);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or Win32Exception or InvalidOperationException)
        { return MediaOutcome.Failed(label + " — sterowanie klawiszami multimedialnymi jest niedostępne na tym systemie.", ex.Message); }
    }

    public MediaOutcome Next() => Send(MediaKey.Next, "Następny utwór");
    public MediaOutcome Previous() => Send(MediaKey.Previous, "Poprzedni utwór");
    public MediaOutcome PlayPause() => Send(MediaKey.PlayPause, "Pauza / wznowienie");
    public MediaOutcome Stop() => Send(MediaKey.Stop, "Zatrzymanie odtwarzania");
    public MediaOutcome MutePlayer() => Send(MediaKey.Mute, "Wyciszenie odtwarzacza");

    /// <summary>Skok głośności systemu z potwierdzeniem odczytem zwrotnym (Core Audio, bez modelu).</summary>
    public MediaOutcome NudgeVolume(int deltaPercent)
    {
        int? before = Core.AudioVolume.GetVolumePercent();
        if (before == null) return MediaOutcome.Failed("Sterowanie głośnością jest niedostępne na tym systemie (brak urządzenia wyjściowego).", "Core Audio: endpoint unavailable");
        int target = Math.Clamp(before.Value + deltaPercent, 0, 100);
        if (Core.AudioVolume.SetVolumePercent(target) != true)
            return MediaOutcome.Failed($"Nie udało się ustawić głośności na {target}%.", "Core Audio IAudioEndpointVolume::SetMasterVolumeLevelScalar odrzuciło zapis");
        int? after = Core.AudioVolume.GetVolumePercent();
        string evidence = $"IAudioEndpointVolume: {before.Value}% → {target}%; odczyt zwrotny {after?.ToString(CultureInfo.InvariantCulture) ?? "brak"} · {DateTimeOffset.Now:O}";
        return after == target
            ? MediaOutcome.Verified($"Głośność systemu: {after}%", evidence)
            : new MediaOutcome(true, $"Ustawiłem głośność na {target}%, ale odczyt zwrotny pokazał {(after?.ToString() ?? "brak")} — sprawdź mikser.", evidence + " · odczyt się rozjechał");
    }

    /// <summary>Kto gra? Lista procesów, bez udawania, że znamy tytuł utworu.</summary>
    public MediaOutcome Status()
    {
        string? found;
        try { found = DetectPlayer(Process.GetProcesses().Select(x => x.ProcessName)); }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException)
        { return MediaOutcome.Failed("Nie mogę odczytać listy procesów na tym systemie.", ex.Message); }
        if (found == null)
            return MediaOutcome.Verified("Nie widzę znanego odtwarzacza wśród działających procesów.\nNie odczytuję tytułu utworu — Sentinel nie ma dostępu do SMTC bez dodatkowej integracji.",
                "Process.GetProcesses · tabela " + Players.Length + " aplikacji · " + DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
        return MediaOutcome.Verified($"Wykryty odtwarzacz: {found}.\nSteruję go klawiszami: pauza, następny, poprzedni, stop. Tytułu utworu nie czytamy.",
            $"dopasowana nazwa procesu → {found} · {DateTimeOffset.Now:O}");
    }

    /// <summary>Pierwsza rozpoznana aplikacja multimedialna (czyste, testowalne).</summary>
    internal static string? DetectPlayer(IEnumerable<string> processNames)
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in processNames) if (!string.IsNullOrWhiteSpace(name)) present.Add(name.Trim());
        foreach ((string process, string label) in Players) if (present.Contains(process)) return label;
        return null;
    }

    // ------------------------------------------------------------------ P/Invoke

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 1;
    private const uint KEYEVENTF_KEYUP = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION { [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public INPUTUNION union; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    /// <summary>Wstrzykuje naciśnięcie i puszczenie klawisza. Zwraca liczbę przyjętych zdarzeń (0..2).</summary>
    internal static int InjectKey(ushort virtualKey)
    {
        var events = new INPUT[]
        {
            new() { type = INPUT_KEYBOARD, union = new INPUTUNION { ki = new KEYBDINPUT { wVk = virtualKey, dwFlags = KEYEVENTF_EXTENDEDKEY } } },
            new() { type = INPUT_KEYBOARD, union = new INPUTUNION { ki = new KEYBDINPUT { wVk = virtualKey, dwFlags = KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP } } },
        };
        uint sent = SendInput((uint)events.Length, events, Marshal.SizeOf<INPUT>());
        return (int)sent;
    }
}

/// <summary>Rozpoznawanie poleceń multimedialnych i głosowych (normalizacja już za nami).
/// Czysta funkcja — testy nie potrzebują systemu ani odtwarzacza.</summary>
public static class MediaCommands
{
    public static string? Describe(MediaKey key) => key switch
    {
        MediaKey.PlayPause => "pauza lub wznowienie", MediaKey.Next => "następny utwór", MediaKey.Previous => "poprzedni utwór",
        MediaKey.Stop => "stop", MediaKey.Mute => "wycisz odtwarzacz", MediaKey.VolumeUp => "głośniej", _ => "ciszej"
    };

    /// <summary>Zwraca klucz albo null, gdy zdanie nie jest poleceniem multimedialnym.</summary>
    public static MediaKey? Match(string normalizedText, out string label)
    {
        label = "";
        string text = normalizedText.Trim().TrimEnd('?', '!', '.', ' ');
        switch (text)
        {
            case "nastepny utwor": case "nastepny numer": case "next": case "next track": case "dalej muzycznie":
                label = "Następny utwór"; return MediaKey.Next;
            case "poprzedni utwor": case "poprzedni numer": case "previous": case "wczesniej utwor":
                label = "Poprzedni utwór"; return MediaKey.Previous;
            case "pauza": case "pauza muzyki": case "wstrzymaj muzyke": case "zatrzymaj muzyke": case "play":
            case "play/pauza": case "wznow odtwarzanie": case "odtworz": case "wznusz muzyczke":
                label = "Pauza / wznowienie"; return MediaKey.PlayPause;
            case "stop odtwarzanie": case "stop muzyka": case "zatrzymaj odtwarzacz":
                label = "Zatrzymanie odtwarzania"; return MediaKey.Stop;
            case "wycisz odtwarzacz": case "wycisz muzyke": case "media mute":
                label = "Wyciszenie odtwarzacza"; return MediaKey.Mute;
            default: return null;
        }
    }
}
