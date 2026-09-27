using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>
/// SEKCJA 3 · pozycje 121–185 — prywatność i bezpieczeństwo danych bez sieci. Ten moduł nic nie
/// wysyła i nic nie usuwa: pokazuje, gdzie leżą dane, ile zajmują, co byłoby stare przy zadanej
/// retencji (tylko podgląd), co jest zaszyfrowane, a co nie, i jakich uprawnień aplikacja używa.
/// Każda odpowiedź oddziela fakt (odczyt z dysku) od deklaracji (jak działa Sentinel).
/// </summary>
public static class PrivacyToolbox
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public static string? TryHandle(string command, string text)
    {
        string raw = command ?? "";

        if (text is "prywatnosc" or "prywatnosc danych" or "polityka prywatnosci" or "audyt prywatnosci" or "audyt danych")
            return Summary(AppPaths.Root);
        if (text is "gdzie sa moje dane" or "moje dane" or "gdzie dane" or "mapa danych")
            return DataMap(AppPaths.Root);
        var large = Regex.Match(text, @"^duze pliki danych[:\s]+(\d{1,5})$");
        if (large.Success) return LargeFiles(AppPaths.Root, double.Parse(large.Groups[1].Value, CultureInfo.InvariantCulture));
        var retention = Regex.Match(text, @"^retencja[:\s]+(\d{1,5})\s*(?:dni|d)?$");
        if (retention.Success) return Retention(AppPaths.Root, int.Parse(retention.Groups[1].Value, CultureInfo.InvariantCulture));
        if (text is "retencja" or "retencja danych") return RetentionPlan();
        if (text is "wiek danych" or "najstarsze dane" or "co jest stare")
            return DataAge(AppPaths.Root);
        if (text is "szyfrowanie" or "co jest szyfrowane" or "szyfrowanie danych") return Encryption();
        if (text is "uprawnienia" or "jakie masz uprawnienia" or "uprawnienia sentinela") return Permissions();
        if (text is "co wysylam" or "co wysylam na zewnatrz" or "czy wysylasz dane" or "telemetria")
            return Outgoing();
        if (text is "eksport danych" or "eksport moich danych" or "jak wyeksportowac dane") return ExportPlan();
        if (text is "minimalizacja" or "minimalizacja danych" or "jak ograniczyc dane") return Minimization();
        // Własny katalog tylko z dwukropkiem: „prywatnosc w internecie to temat rzeka” to zdanie, nie polecenie.
        var specific = Regex.Match(text, @"^(?:prywatnosc|audyt prywatnosci):\s*(.+)$");
        if (specific.Success) return Summary(specific.Groups[1].Value.Trim());
        return null;
    }

    private static string Human(double bytes)
    {
        if (bytes >= 1024 * 1024) return (bytes / 1024 / 1024).ToString("0.##", Pl) + " MB";
        if (bytes >= 1024) return (bytes / 1024).ToString("0.##", Pl) + " KB";
        return bytes.ToString("0", Pl) + " B";
    }

    /// <summary>Podsumowanie jednego podkatalogu katalogu danych — publiczne, bo <see cref="Scan"/>
    /// zwraca je na zewnątrz (prywatny typ w publicznej sygnaturze to błąd kompilacji).</summary>
    public sealed record DataEntry(string Name, long Bytes, int Files, DateTimeOffset Newest, DateTimeOffset Oldest);

    /// <summary>Odczyt katalogu danych: rozmiar i liczba plików w każdym podkatalogu pierwszego poziomu.</summary>
    public static IReadOnlyList<DataEntry> Scan(string directory)
    {
        var entries = new List<DataEntry>();
        if (!Directory.Exists(directory)) return entries;
        IEnumerable<string> directories;
        try { directories = Directory.EnumerateDirectories(directory).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(); }
        catch (Exception) { return entries; }
        foreach (string sub in directories)
        {
            long bytes = 0;
            int files = 0;
            DateTimeOffset newest = DateTimeOffset.MinValue, oldest = DateTimeOffset.MaxValue;
            try
            {
                foreach (string file in Directory.EnumerateFiles(sub, "*", SearchOption.AllDirectories))
                {
                    var info = new FileInfo(file);
                    bytes += info.Length;
                    files++;
                    DateTimeOffset written = info.LastWriteTimeUtc;
                    if (written > newest) newest = written;
                    if (written < oldest) oldest = written;
                }
            }
            catch (Exception) { /* brak dostępu do części plików nie może wywalić raportu */ }
            if (files == 0) oldest = newest = DateTimeOffset.MinValue;
            entries.Add(new DataEntry(Path.GetFileName(sub), bytes, files, newest, oldest));
        }
        long rootBytes = 0;
        int rootFiles = 0;
        try
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                rootBytes += new FileInfo(file).Length;
                rootFiles++;
            }
        }
        catch (Exception) { }
        if (rootFiles > 0) entries.Insert(0, new DataEntry("(pliki główne)", rootBytes, rootFiles, DateTimeOffset.MinValue, DateTimeOffset.MinValue));
        return entries;
    }

    public static string Summary(string directory) =>
        "Prywatność Sentinel X — stan faktyczny:" + Environment.NewLine +
        "· Wszystko działa lokalnie: brak telemetrii, brak konta, brak chmury. Model AI (jeśli go używasz) to Ollama na 127.0.0.1 — pytanie nie opuszcza komputera." + Environment.NewLine +
        "· Dane leżą w jednym katalogu: " + directory + " — sprawdź „gdzie sa moje dane”, ile zajmują i czego dotyczą." + Environment.NewLine +
        "· Co nie jest zapisywane nigdzie: hasła z sejfu (tylko zaszyfrowany plik), treść schowka, odpowiedzi modelu poza historią rozmowy, dane mikrofonu i kamery." + Environment.NewLine +
        "· Co jest zapisywane: rozmowa i wspomnienia (JSON w katalogu danych), zadania i projekty, snapshoty diagnostyki, kopie, dziennik zdarzeń i dziennik akcji." + Environment.NewLine +
        "· Kontrola po Twojej stronie: „wiek danych”, „duze pliki danych: 5”, „retencja: 90” (podgląd), „szyfrowanie”, „uprawnienia”, „eksport danych”, „minimalizacja”." + Environment.NewLine +
        "· Ten raport nic nie zmienia i nic nie usuwa — to odczyt katalogu i opis zachowania aplikacji.";

    public static string DataMap(string directory)
    {
        var entries = Scan(directory);
        if (entries.Count == 0)
            return "Nie widzę katalogu danych pod " + directory + " — jeszcze nic nie zostało zapisane. To dobra wiadomość dla prywatności: nie ma czego pokazywać.";
        long total = entries.Sum(x => x.Bytes);
        var lines = entries.Select(x => "· " + x.Name.PadRight(20) + Human(x.Bytes).PadLeft(10) + " · plików: " + x.Files);
        return "Gdzie są Twoje dane (odczyt z dysku, " + directory + "):" + Environment.NewLine +
            string.Join(Environment.NewLine, lines) + Environment.NewLine +
            "· razem: " + Human(total) + " w " + entries.Sum(x => x.Files) + " plikach" + Environment.NewLine +
            "· Rozmiary są prawdziwe, policzone teraz. Nic tu nie zmieniam ani nie wysyłam." ;
    }

    public static string LargeFiles(string directory, double minMegabytes)
    {
        double threshold = minMegabytes <= 0 ? 1 : minMegabytes;
        if (!Directory.Exists(directory)) return "Nie ma katalogu danych " + directory + " — nie ma czego przeglądać.";
        List<(string Path, long Bytes)> files = [];
        try
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                long length = new FileInfo(file).Length;
                if (length >= threshold * 1024 * 1024) files.Add((file, length));
            }
        }
        catch (Exception ex) { return "Nie przeskanowałem katalogu danych: " + ex.GetType().Name + " — " + ex.Message; }
        if (files.Count == 0) return "Brak plików danych większych niż " + Human(threshold * 1024 * 1024) + " w " + directory + ".";
        var lines = files.OrderByDescending(x => x.Bytes).Take(15).Select(x => "· " + Human(x.Bytes).PadLeft(10) + "  " + x.Path);
        return "Największe pliki danych (próg " + Human(threshold * 1024 * 1024) + ", " + files.Count + " plik(ów) powyżej progu):" + Environment.NewLine +
            string.Join(Environment.NewLine, lines) + Environment.NewLine +
            "· Zajrzałem tylko do rozmiarów — nie czytam treści tych plików ani ich nie ruszam.";
    }

    public static string RetentionPlan() =>
        "Retencja danych — co i jak długo trzyma Sentinel:" + Environment.NewLine +
        "· rozmowa: limit 720 wpisów, potem najstarsze schodzą z pamięci roboczej (archiwum zostaje, jeśli je zrobisz)" + Environment.NewLine +
        "· wspomnienia: do 500 pozycji, każda do 4000 znaków" + Environment.NewLine +
        "· snapshoty diagnostyki: limit liczony w sztukach, najstarsze usuwane automatycznie" + Environment.NewLine +
        "· kopie zapasowe: rotacja wg limitu kopii („kopie danych” pokaże stan)" + Environment.NewLine +
        "· dziennik zdarzeń: rotacja rozmiarowa pliku" + Environment.NewLine +
        "· Podgląd: „retencja: 90” pokaże, co byłoby stare przy 90 dniach — nic nie usuwa. Realne kasowanie zostaje w Twoich rękach („usuń snapshot N”, „usuń archiwum RRRR-MM”).";

    public static string Retention(string directory, int days)
    {
        if (days <= 0) return "Podaj liczbę dni większą od zera, np. „retencja: 90”.";
        if (!Directory.Exists(directory)) return "Nie ma katalogu danych " + directory + " — nie ma czego liczyć.";
        var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
        List<(string Path, DateTimeOffset Written)> old = [];
        try
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                DateTimeOffset written = new FileInfo(file).LastWriteTimeUtc;
                if (written < cutoff) old.Add((file, written));
            }
        }
        catch (Exception ex) { return "Nie policzyłem retencji: " + ex.GetType().Name + " — " + ex.Message; }
        if (old.Count == 0)
            return "Przy retencji " + days + " dni nie ma nic starszego w " + directory + " — wszystkie pliki są nowsze niż " + cutoff.ToString("dd.MM.yyyy", Pl) + ".";
        long bytes = 0;
        foreach (var item in old)
        {
            try { bytes += new FileInfo(item.Path).Length; } catch (Exception) { }
        }
        var lines = old.OrderBy(x => x.Written).Take(12).Select(x => "· " + x.Written.ToString("dd.MM.yyyy", Pl) + "  " + x.Path);
        return "Retencja " + days + " dni — PODGLĄD, nic nie usuwam:" + Environment.NewLine +
            "· starszych niż " + cutoff.ToString("dd.MM.yyyy", Pl) + ": " + old.Count + " plik(ów), razem " + Human(bytes) + Environment.NewLine +
            string.Join(Environment.NewLine, lines) + (old.Count > 12 ? Environment.NewLine + "· …i " + (old.Count - 12) + " więcej" : "") + Environment.NewLine +
            "· Gdybyś chciał to posprzątać, zrób najpierw „kopia danych”, a potem usuwaj ręcznie w Eksploratorze. Sentinel sam nie kasuje historii.";
    }

    public static string DataAge(string directory)
    {
        var entries = Scan(directory).Where(x => x.Files > 0).ToArray();
        if (entries.Length == 0) return "Brak plików danych do oceny wieku w " + directory + ".";
        var lines = entries.Select(x => "· " + x.Name.PadRight(20) + " najnowszy: " + x.Newest.ToLocalTime().ToString("dd.MM.yyyy HH:mm", Pl) + " · najstarszy: " + x.Oldest.ToLocalTime().ToString("dd.MM.yyyy HH:mm", Pl));
        DateTimeOffset newest = entries.Max(x => x.Newest), oldest = entries.Min(x => x.Oldest);
        return "Wiek danych (katalog " + directory + "):" + Environment.NewLine +
            string.Join(Environment.NewLine, lines) + Environment.NewLine +
            "· najstarszy plik: " + oldest.ToLocalTime().ToString("dd.MM.yyyy HH:mm", Pl) + " · najnowszy: " + newest.ToLocalTime().ToString("dd.MM.yyyy HH:mm", Pl) + Environment.NewLine +
            "· Daty z metadanych plików, nie z treści. Nic nie otwieram — patrzę tylko na rozmiar i czas zapisu.";
    }

    public static string Encryption() =>
        "Co jest zaszyfrowane, a co nie — bez upiększania:" + Environment.NewLine +
        "· ZASZYFROWANE: sejf na sekrety — AES-256-GCM, klucz wyprowadzany z hasła przez PBKDF2 (210 000 iteracji). Hasła nie ma w pliku, a podmiana zawartości jest wykrywana („sejf”, „sejf dodaj: nazwa = wartość”)." + Environment.NewLine +
        "· ZASZYFROWANE przez system, jeśli włączysz: cały dysk (BitLocker) albo folder danych (EFS). Sentinel tego nie włącza sam." + Environment.NewLine +
        "· NIE ZASZYFROWANE: rozmowa, wspomnienia, zadania, snapshoty, kopie ZIP, dziennik zdarzeń. To pliki JSON/ZIP w katalogu danych." + Environment.NewLine +
        "· Kopia ZIP ma natomiast manifest SHA-256 — wykryje zmianę pliku, ale jej nie ukryje. Do poufnych rzeczy użyj sejfu, nie backupu." + Environment.NewLine +
        "· Czego nie zrobię: nie wymyślę własnego szyfrowania ani nie udam, że plik jest bezpieczny, jeśli nie jest.";

    public static string Permissions() =>
        "Uprawnienia, których używa Sentinel:" + Environment.NewLine +
        "· pliki: zapis i odczyt własnego katalogu danych oraz operacje, o które poprosisz wprost (tworzenie pliku w wskazanym miejscu, sprzątanie duplikatów, zmiana nazw — zawsze z podglądem i potwierdzeniem)" + Environment.NewLine +
        "· rejestr: wyłącznie klucz autostartu HKCU (Twój profil), tylko gdy włączysz autostart" + Environment.NewLine +
        "· procesy: odczyt listy i zamykanie okna aplikacji (WM_CLOSE — to samo, co kliknięcie „X”), z potwierdzeniem" + Environment.NewLine +
        "· dźwięk, schowek, okna: lokalne API Windows; syntetyczne klawisze mogą zostać odrzucone przez system (UIPI) i wtedy mówię o tym wprost" + Environment.NewLine +
        "· sieć: tylko lokalna Ollama (127.0.0.1:11434), jeśli używasz modelu. Brak telemetrii, brak kont, brak chmury" + Environment.NewLine +
        "· czego NIE robię: nie modyfikuję kodu aplikacji, nie zmieniam ustawień systemowych bez polecenia, nie czytam plików poza tym, o co prosisz.";

    public static string Outgoing() =>
        "Co wychodzi z tego komputera — odpowiedź jest krótka:" + Environment.NewLine +
        "· Nic nie wysyłam na zewnątrz: brak telemetrii, brak kont użytkownika, brak chmury AI, brak aktualizacji w tle." + Environment.NewLine +
        "· Jedyne połączenie, jakie może powstać, to lokalne http://127.0.0.1:11434 — Twoja własna Ollama. Nie ma tam Twoich danych, dopóki sam nie zapytasz modelu." + Environment.NewLine +
        "· Jak to sprawdzić samodzielnie: „netstat -ano | findstr 11434” pokaże lokalne połączenia, a dziennik zdarzeń Sentinela („dziennik json”) — co robiła aplikacja." + Environment.NewLine +
        "· Gdy brakuje modelu albo internetu, narzędzia offline działają dalej — to znaczy, że nic nie wymaga sieci." + Environment.NewLine +
        "· Nie mam dostępu do Twojego konta pocztowego, przeglądarki ani chmury i nie proszę o hasła.";

    public static string ExportPlan() =>
        "Jak wyeksportować swoje dane (wszystko lokalnie):" + Environment.NewLine +
        "· rozmowa: „eksportuj rozmowę markdown” — plik z SHA-256 w nagłówku, żeby dało się sprawdzić, że się nie zmienił" + Environment.NewLine +
        "· diagnostyka: „eksportuj raport” i „eksportuj porównanie” (odczyty stanu)" + Environment.NewLine +
        "· historia akcji: przycisk eksportu JSON/CSV w widoku akcji" + Environment.NewLine +
        "· archiwa rozmów: „archiwizuj rozmowy”, potem „archiwa” — pokażą miesiące i rozmiary" + Environment.NewLine +
        "· wszystko naraz: skopiuj katalog danych wskazany przez „gdzie sa moje dane” (albo „kopia danych” z manifestem)" + Environment.NewLine +
        "· Eksport jest kompletny albo go nie ma: nie udaję, że wyeksportowałem coś, czego nie widzę.";

    public static string Minimization() =>
        "Jak ograniczyć ilość zapisywanych danych:" + Environment.NewLine +
        "· Ustawienia → Pamięć i prywatność: wyłącz zapisywanie wspomnień i rozmów, jeśli nie chcesz historii" + Environment.NewLine +
        "· „nowa rozmowa” czyści bieżący wątek bez ruszania wspomnień; archiwum zostaje tylko, jeśli je zrobisz" + Environment.NewLine +
        "· snapshoty diagnostyki trzymaj krótko — limit i „usuń snapshot N” są wbudowane" + Environment.NewLine +
        "· kopie zapasowe: ustaw mniejszy limit kopii, jeśli nie potrzebujesz historii" + Environment.NewLine +
        "· dziennik zdarzeń: rotacja rozmiarowa pilnuje, żeby nie rósł w nieskończoność" + Environment.NewLine +
        "· Nie czyszczę nic automatycznie poza tymi limitami — decyzja o skasowaniu historii jest Twoja.";
}
