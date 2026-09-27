using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>0.96 · JARVIS — warstwa sterowania komputerem, której brakowało: okna, multimedia,
/// ekran i zasilanie, historia schowka oraz szukanie plików po nazwie.
/// <para>Zasady, których tu nie łamię:
/// <b>1.</b> Nic nie udaje sukcesu — gdy system odrzuci zdarzenie (UIPI, brak sesji pulpitu,
/// brak prawa zamykania) dostajesz wprost „nie udało się”.
/// <b>2.</b> Zamykam okna przez WM_CLOSE, czyli dokładnie tak, jak kliknięcie „X” — program może
/// zapytać o zapis. Nie zabijam procesów.
/// <b>3.</b> Polecenie jest zgodą (nie pytam drugi raz), ale zamknięcie, restart i uśpienie mają
/// okno do odwołania: licznik i „anuluj zamknięcie”.
/// <b>4.</b> Nie zamykam własnego okna komendą „zamknij okno” — zanim cokolwiek zamknę,
/// sprawdzam, czy aktywne okno nie należy do mnie.</para></summary>
public static class JarvisToolkit
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    /// <summary>Zwraca odpowiedź, gdy polecenie jest jednym z narzędzi; null = nie moje.</summary>
    public static string? TryHandle(string command)
    {
        string raw = (command ?? "").Trim();
        if (raw.Length == 0) return null;
        string text = ConversationMemoryService.Normalize(raw).Trim().TrimEnd('.', '!', '?', ' ');
        if (text.Length == 0) return null;

        // ============================ OKNA ============================
        if (text is "okna" or "lista okien" or "jakie okna" or "otwarte okna" or "pokaz okna" or "jakie mam okna")
            return Core.WindowManager.Describe();

        if (text is "minimalizuj wszystko" or "minimalizuj okna" or "schowaj okna" or "schowaj wszystko" or "ukryj okna")
        {
            bool ok = Core.InputSender.ShowDesktop();
            return ok ? "Pulpit: wszystkie okna zminimalizowane (skrót Windows+D)." : "Nie udało się zminimalizować okien — system nie przyjął skrótu (aktywne okno może mieć wyższe uprawnienia).";
        }
        if (text is "minimalizuj okno" or "minimalizuj" or "zminimalizuj okno" or "schowaj okno")
            return Simple("Okno zminimalizowane.", "Nie udało się zminimalizować okna — nie widzę aktywnego okna.", Core.WindowManager.MinimizeActive());
        if (text is "maksymalizuj okno" or "maksymalizuj" or "zmaksymalizuj okno" or "rozciagnij okno")
            return Simple("Okno zmaksymalizowane.", "Nie udało się zmaksymalizować okna — nie widzę aktywnego okna.", Core.WindowManager.MaximizeActive());
        if (text is "przywroc okno" or "normalny rozmiar okna" or "przywroc rozmiar okna" or "odklein okno")
            return Simple("Okno przywrócone do zwykłego rozmiaru.", "Nie udało się przywrócić okna — nie widzę aktywnego okna.", Core.WindowManager.RestoreActive());
        if (text is "okno w lewo" or "dokuj w lewo" or "lewa polowa" or "lewa połowa")
            return Simple("Okno zadokowane do lewej połowy ekranu (Windows+←).", "Nie udało się zadokować okna — system nie przyjął skrótu.", Core.InputSender.SnapLeft());
        if (text is "okno w prawo" or "dokuj w prawo" or "prawa polowa" or "prawa połowa")
            return Simple("Okno zadokowane do prawej połowy ekranu (Windows+→).", "Nie udało się zadokować okna — system nie przyjął skrótu.", Core.InputSender.SnapRight());
        if (text is "pelny ekran" or "fullscreen" or "f11" or "tryb pelnoekranowy")
            return Simple("Wysłałem F11 — większość programów przełącza wtedy pełny ekran.", "Nie udało się wysłać F11.", Core.InputSender.Tap(Core.InputSender.F11));
        if (text is "przelacz okno" or "nastepne okno" or "alt tab" or "przelacz na nastepne okno" or "zmien okno")
            return Simple("Przełączam okno (Alt+Tab).", "Nie udało się przełączyć okna — system nie przyjął skrótu.", Core.InputSender.AltTab());

        if (text is "zamknij okno" or "zamknij aktywne okno" or "zamknij biezace okno")
        {
            var active = Core.WindowManager.Foreground();
            if (active == null) return "Nie widzę aktywnego okna — nie mam czego zamknąć.";
            if (Core.WindowManager.IsOwnWindow(active.Handle))
                return "Aktywne okno to Sentinel. Nie zamykam się tą komendą — użyj „X” w oknie albo wyjdź z ikony w zasobniku. " +
                       "Dzięki temu „zamknij okno” nie potrafi przypadkiem wyłączyć asystenta, w którym masz dane.";
            return Simple("Wysłałem zamknięcie do okna „" + Short(active.Title) + "” — to samo co kliknięcie „X”, więc program może zapytać o zapisanie pracy.",
                "Nie udało się zamknąć aktywnego okna.", Core.WindowManager.CloseActive());
        }

        var activate = Regex.Match(raw, @"^(?:przelacz na|przejdz do|aktywuj okno|pokaz okno)\s*:?\s*(.+)$", RegexOptions.IgnoreCase);
        if (!activate.Success) activate = Regex.Match(raw, @"^(?:przelacz|przejdz)\s+na\s+okno\s*:?\s*(.+)$", RegexOptions.IgnoreCase);
        if (activate.Success)
        {
            string fragment = activate.Groups[1].Value.Trim().Trim('.', '!', '?');
            if (fragment.Length == 0) return "Podaj fragment tytułu okna, np. „przełącz na: chrome”.";
            var window = Core.WindowManager.Activate(fragment);
            return window != null
                ? "Przełączam na okno „" + Short(window.Title) + "”."
                : "Nie znalazłem widocznego okna z „" + fragment + "” w tytule. „okna” pokazuje, co jest otwarte.";
        }

        // ============================ PULPITY WIRTUALNE (#804) ============================
        if (text is "pulpity" or "pulpity wirtualne" or "wirtualne pulpity" or "lista pulpitow")
            return "Pulpity wirtualne obsługuję skrótami systemowymi Windows (Ctrl+Windows+…), bo Microsoft nie daje " +
                   "publicznego API do ich liczenia: nie potrafię uczciwie powiedzieć, ile ich masz otwartych.\n" +
                   "· „nowy pulpit” — Ctrl+Win+D\n· „pulpit w lewo” / „pulpit w prawo” — Ctrl+Win+← / →\n" +
                   "· „zamknij pulpit” — Ctrl+Win+F4 (zamyka bieżący pulpit, okna przechodzą na sąsiedni)";
        if (text is "nowy pulpit" or "nowy pulpit wirtualny" or "dodaj pulpit" or "utworz pulpit")
            return Simple("Nowy pulpit wirtualny (Ctrl+Win+D) — otwarte okna zostają na poprzednim.",
                "Nie udało się utworzyć pulpitu wirtualnego — system nie przyjął skrótu.", Core.InputSender.NewDesktop());
        if (text is "pulpit w lewo" or "poprzedni pulpit" or "pulpit wirtualny w lewo")
            return Simple("Przełączam na pulpit po lewej (Ctrl+Win+←).",
                "Nie udało się przełączyć pulpitu — system nie przyjął skrótu.", Core.InputSender.DesktopLeft());
        if (text is "pulpit w prawo" or "nastepny pulpit" or "pulpit wirtualny w prawo")
            return Simple("Przełączam na pulpit po prawej (Ctrl+Win+→).",
                "Nie udało się przełączyć pulpitu — system nie przyjął skrótu.", Core.InputSender.DesktopRight());
        if (text is "zamknij pulpit" or "zamknij pulpit wirtualny" or "usun pulpit")
            return Simple("Zamykam bieżący pulpit wirtualny (Ctrl+Win+F4) — okna przechodzą na sąsiedni pulpit, nic nie tracisz.",
                "Nie udało się zamknąć pulpitu — system nie przyjął skrótu (albo jest to jedyny pulpit).", Core.InputSender.CloseDesktop());

        // ============================ MULTIMEDIA ============================
        if (text is "pauza" or "wstrzymaj" or "wznow" or "wznow odtwarzanie" or "odtworz" or "play" or "play pause" or "graj" or "pauzuj")
            return Simple("Play/pauza — wysłałem klawisz multimediów (działa na aktywnym odtwarzaczu systemu).",
                "Nie udało się wysłać klawisza multimediów.", Core.InputSender.PlayPause());
        if (text is "nastepny utwor" or "nastepna piosenka" or "kolejny utwor" or "dalej" or "next")
            return Simple("Następny utwór.", "Nie udało się przełączyć utworu.", Core.InputSender.NextTrack());
        if (text is "poprzedni utwor" or "poprzednia piosenka" or "wstecz" or "cofnij utwor" or "previous")
            return Simple("Poprzedni utwór.", "Nie udało się przełączyć utworu.", Core.InputSender.PreviousTrack());
        if (text is "zatrzymaj odtwarzanie" or "stop odtwarzania" or "zatrzymaj muzyke" or "stop")
            return Simple("Zatrzymałem odtwarzanie.", "Nie udało się zatrzymać odtwarzania.", Core.InputSender.StopMedia());
        if (text is "glosniej" or "podglosnij" or "daj glosniej" or "volume up")
            return ChangeVolume(+10);
        if (text is "ciszej" or "scisz" or "daj ciszej" or "przycisz" or "volume down")
            return ChangeVolume(-10);

        // ============================ EKRAN I ZASILANIE ============================
        if (text is "zablokuj ekran" or "zablokuj komputer" or "zablokuj" or "lock")
            return Simple("Ekran zablokowany (Windows+L). Odblokujesz go hasłem, PIN-em lub odciskiem palca.",
                "Nie udało się zablokować ekranu — system odmówił (np. sesja zdalna bez prawa blokady).", Core.PowerManager.Lock());
        if (text is "wygasz ekran" or "zgas ekran" or "wygasz monitor" or "wylacz ekran")
            return Simple("Monitor wygaszony — dowolny ruch myszy albo klawisz przywraca obraz.",
                "Nie udało się wygasić monitora.", Core.PowerManager.MonitorOff());
        if (text is "uspij komputer" or "uspij" or "uspienie" or "sleep" or "wprowadz komputer w sen")
            return Arm("sleep", 20, "uśpienie");
        if (text is "zamknij komputer" or "wylacz komputer" or "zamknij system" or "wylacz system" or "shutdown" or "zamknij kompa")
            return Arm("shutdown", 60, "zamknięcie");
        if (text is "restart komputera" or "zrestartuj komputer" or "restartuj komputer" or "restart" or "reboot" or "uruchom ponownie komputer")
            return Arm("restart", 60, "restart");
        if (text is "anuluj zamkniecie" or "anuluj zamykanie" or "przerwij zamykanie" or "nie zamykaj" or "anuluj wylaczanie" or "anuluj uspienie")
            return Core.PowerManager.Abort()
                ? "Odwołane. Komputer zostaje włączony — nic nie zostało zamknięte."
                : "Nie mam uzbrojonego zamykania, więc nie ma czego odwoływać.";

        // ============================ SCHOWEK ============================
        if (text is "historia schowka" or "schowek historia" or "ostatnie w schowku" or "co kopiowalem")
            return Core.ClipboardHistory.Describe();
        var clip = Regex.Match(text, @"^schowek\s+(\d{1,2})$");
        if (clip.Success) return RestoreClipboard(int.Parse(clip.Groups[1].Value, Pl));

        // ============================ PLIKI ============================
        var findFile = Regex.Match(raw, @"^(?:znajdz|znajdź|szukaj|wyszukaj)\s+plik(?:u)?\s*:?\s+(.+)$", RegexOptions.IgnoreCase);
        if (findFile.Success)
        {
            string pattern = findFile.Groups[1].Value.Trim().Trim('"', '\'', '.', '!', '?');
            if (pattern.Length == 0) return "Podaj fragment nazwy pliku, np. „znajdź plik: raport”.";
            var roots = Core.FileFinder.DefaultRoots();
            return roots.Count == 0
                ? "Nie znam folderów użytkownika na tym systemie — szukanie plików jest niedostępne."
                : Core.FileFinder.Describe(roots, pattern);
        }

        return null;
    }

    private static string Simple(string success, string failure, bool ok) => ok ? success : failure;

    private static string ChangeVolume(int delta)
    {
        int? current = Core.AudioVolume.GetVolumePercent();
        if (current == null) return "Sterowanie głośnością jest niedostępne na tym systemie (brak domyślnego urządzenia audio).";
        int target = Math.Clamp(current.Value + delta, 0, 100);
        bool? ok = Core.AudioVolume.SetVolumePercent(target);
        if (ok != true) return "Nie udało się zmienić głośności.";
        bool? muted = Core.AudioVolume.IsMuted();
        return "Głośność: " + current.Value + "% → " + target + "%" + (muted == true ? " (wciąż wyciszone — „przywróć dźwięk”)" : "") + ".";
    }

    /// <summary>Uzbraja akcję z opóźnieniem i mówi, jak ją odwołać. Samo polecenie jest zgodą —
    /// nie pytam drugi raz, ale zawsze zostawiam okno do odwołania.</summary>
    private static string Arm(string kind, int seconds, string label)
    {
        string description = Core.PowerManager.Arm(kind, seconds);
        if (description.Length == 0) return "Nie rozpoznałem akcji zasilania.";
        return "Przyjąłem: " + description + ". Wypisuję odliczanie systemowe (" + label + " za " + seconds + " s). " +
               "Masz czas, żeby się rozmyślić: wpisz „anuluj zamknięcie”. " +
               "Zamknięcie idzie przez systemowy shutdown.exe, więc Windows może jeszcze zapytać o niezapisane dane — nie omijam tego.";
    }

    private static string RestoreClipboard(int number)
    {
        string? entry = Core.ClipboardHistory.Get(number);
        if (entry == null)
        {
            var items = Core.ClipboardHistory.Entries();
            return items.Count == 0
                ? "Historia schowka jest pusta w tej sesji — najpierw coś skopiuj: „kopiuj: tekst”."
                : "Nie mam wpisu numer " + number + ". Mam " + items.Count + " — „historia schowka” pokazuje listę.";
        }
        try
        {
            System.Windows.Clipboard.SetText(entry);
            return "Wkleiłem do schowka wpis " + number + " (" + entry.Length + " znaków): „" + Short(entry) + "”.";
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            return "Schowek jest niedostępny w tej sesji — spróbuj ponownie.";
        }
    }

    private static string Short(string text)
    {
        string flat = text.Replace("\r", " ").Replace("\n", " ");
        return flat.Length <= 60 ? flat : flat[..59] + "…";
    }
}
