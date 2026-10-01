using System.Globalization;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using QRCoder;

namespace SentinelX;

/// <summary>0.95 · WARSZTAT — the second half of the offline toolbox.
/// Everything is deterministic and local: a text diff, a regex tester, file hashes (read-only),
/// data extraction, line ordering, amounts in Polish words, time conversion, password strength
/// and QR codes written into the app's own data folder. Nothing here talks to the network,
/// starts a process or touches a file outside an explicit path given by the user.</summary>
public static partial class UtilityToolbox
{
    private const string DiffSeparatorHint =
        "Podaj dwa teksty oddzielone „|||”, np. „porownaj teksty: ala ma kota ||| ala ma psa”. W dłuższych tekstach możesz też rozdzielić je linią z samymi myślnikami (---).";

    // ------------------------------------------------------------------ diff
    /// <summary>Line comparison of two blocks. Shows only the differences plus a summary —
    /// no fuzzy scoring, no guessing which line "should" be there.</summary>
    public static string DiffText(string text)
    {
        var (left, right, error) = SplitTwo(text);
        if (error != null) return error;
        string[] a = SplitLines(left), b = SplitLines(right);
        var bSet = new HashSet<string>(b, StringComparer.Ordinal);
        var aSet = new HashSet<string>(a, StringComparer.Ordinal);
        var onlyA = a.Where(line => !bSet.Contains(line)).ToList();
        var onlyB = b.Where(line => !aSet.Contains(line)).ToList();
        if (onlyA.Count == 0 && onlyB.Count == 0)
            return "Teksty są identyczne (porównuję dokładnie znaki). Wierszy: " + a.Length + " i " + b.Length + ".";
        var result = new StringBuilder();
        result.AppendLine("Porównanie tekstów (linia po linii, dokładne znaki):");
        int shown = 0;
        foreach (string line in onlyA)
        {
            if (shown++ >= 40) { result.AppendLine("… (lista skrócona)"); break; }
            result.AppendLine("− tylko w pierwszym: " + Shorten(line, 160));
        }
        foreach (string line in onlyB)
        {
            if (shown++ >= 40) { result.AppendLine("… (lista skrócona)"); break; }
            result.AppendLine("+ tylko w drugim: " + Shorten(line, 160));
        }
        result.Append("Razem: wierszy ").Append(a.Length).Append(" i ").Append(b.Length)
            .Append(" · różnice ").Append(onlyA.Count + onlyB.Count)
            .Append(" (pierwszy ").Append(onlyA.Count).Append(", drugi ").Append(onlyB.Count).Append(") · wspólnych ")
            .Append(a.Length - onlyA.Count).Append('.');
        return result.ToString();
    }

    // ----------------------------------------------------------------- regex
    /// <summary>Tests a regular expression against a text locally. A 500 ms timeout guards the
    /// machine against catastrophic backtracking; a longer text is cut with an honest note.</summary>
    public static string RegexTest(string text)
    {
        var (pattern, subject, error) = SplitTwo(text);
        if (error != null) return "Podaj wzorzec i tekst oddzielone „|||”, np. „regex: \\d+ ||| mam 12 kotów i 3 psy”.";
        pattern = pattern.Trim();
        if (pattern.Length == 0) return "Wzorzec jest pusty.";
        if (pattern.Length > 500) return "Wzorzec jest za długi (limit 500 znaków).";
        bool cut = subject.Length > 20000;
        if (cut) subject = subject[..20000];
        Regex regex;
        try { regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(500)); }
        catch (ArgumentException ex) { return "Wzorzec nie jest poprawny: " + ex.Message + "\nNic nie zostało uruchomione — popraw wzorzec i spróbuj ponownie."; }
        try
        {
            var matches = regex.Matches(subject);
            if (matches.Count == 0) return "Brak dopasowań wzorca „" + pattern + "” w tekście (" + subject.Length + " znaków)." + (cut ? " Sprawdziłem pierwsze 20 000 znaków." : "");
            var result = new StringBuilder();
            result.Append("Wzorzec „").Append(pattern).Append("” — dopasowania: ").Append(matches.Count);
            if (cut) result.Append(" (sprawdziłem pierwsze 20 000 znaków)");
            result.AppendLine().AppendLine();
            int limit = Math.Min(matches.Count, 20);
            for (int i = 0; i < limit; i++)
            {
                Match match = matches[i];
                result.Append(i + 1).Append(". „").Append(Shorten(match.Value.Replace("\n", "\\n").Replace("\r", ""), 120)).Append('”');
                if (match.Groups.Count > 1)
                {
                    var groups = new List<string>();
                    for (int g = 1; g < match.Groups.Count; g++) groups.Add(g + "=" + Shorten(match.Groups[g].Value, 40));
                    result.Append(" (grupy: ").Append(string.Join(", ", groups)).Append(')');
                }
                result.AppendLine();
            }
            if (matches.Count > limit) result.AppendLine("… pokazuję pierwsze " + limit + " dopasowań.");
            return result.ToString().TrimEnd();
        }
        catch (RegexMatchTimeoutException) { return "Wzorzec przekroczył limit 500 ms — prawdopodobnie jest zbyt złożony (katastrofalny backtracking). Zmień wzorzec, np. dodaj ograniczenie znaków."; }
        catch (Exception ex) { return "Nie udało się sprawdzić wzorca: " + ex.Message; }
    }

    // -------------------------------------------------------------- file hash
    /// <summary>Read-only hashes of a file the user explicitly points at. Nothing is written,
    /// nothing is uploaded. Files above 2 GB are refused instead of freezing the machine.</summary>
    public static string FileHash(string command)
    {
        string normalized = ConversationMemoryService.Normalize(command);
        bool wantMd5 = normalized.StartsWith("md5 pliku", StringComparison.Ordinal);
        bool wantSha = normalized.StartsWith("sha256 pliku", StringComparison.Ordinal);
        string argument = Argument(command, "sha256 pliku", "md5 pliku", "hash pliku");
        if (argument.Length == 0) return "Podaj ścieżkę pliku, np. „sha256 pliku: C:\\Users\\Ty\\Dokumenty\\raport.pdf” albo „sha256 pliku: pobrane\\setup.exe”.";
        string? path = ResolvePath(argument);
        if (path == null) return "Nie rozpoznałem ścieżki: " + Shorten(argument, 120);
        if (Directory.Exists(path)) return "To folder, nie plik: " + path + "\nWskaż konkretny plik.";
        if (!File.Exists(path)) return "Nie widzę pliku: " + path + "\nSprawdź literę dysku i nazwę. Nie szukam po całym dysku bez Twojego polecenia.";
        try
        {
            var info = new FileInfo(path);
            if (info.Length > 2L * 1024 * 1024 * 1024) return "Plik ma " + HumanBytes(info.Length) + " — liczenie skrótu trwałoby zbyt długo i zamroziłoby komputer. Podaj mniejszy plik albo podziel go na części.";
            var result = new StringBuilder();
            result.Append("Plik: ").AppendLine(info.Name);
            result.Append("Ścieżka: ").AppendLine(info.FullName);
            result.Append("Rozmiar: ").Append(HumanBytes(info.Length)).Append(" (").Append(info.Length.ToString("N0", Pl)).AppendLine(" B)");
            result.Append("Zmieniony: ").AppendLine(info.LastWriteTime.ToString("dd.MM.yyyy HH:mm:ss", Pl));
            using (var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20))
            {
                if (wantMd5 || !wantSha) result.Append("MD5:      ").AppendLine(Convert.ToHexString(MD5.HashData(stream)).ToLowerInvariant());
                stream.Position = 0;
                if (wantSha || !wantMd5) result.Append("SHA-256:  ").Append(Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
            }
            result.AppendLine().Append("Skrót policzony lokalnie, tylko do odczytu");
            return result.ToString().TrimEnd();
        }
        catch (UnauthorizedAccessException) { return "Brak dostępu do pliku: " + path; }
        catch (IOException ex) { return "Nie udało się odczytać pliku (" + ex.GetType().Name + "): " + path; }
    }

    // --------------------------------------------------------------- extract
    /// <summary>Pulls e-mail addresses, links, IPv4 addresses and numbers out of a text.
    /// Only finds — never opens anything.</summary>
    public static string Extract(string text)
    {
        if (text.Trim().Length == 0) return "Podaj tekst, z którego mam coś wyciągnąć.";
        var emails = Distinct(Regex.Matches(text, @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}").Select(m => m.Value));
        var links = Distinct(Regex.Matches(text, @"https?://[^\s<>()""]+").Select(m => m.Value.TrimEnd('.', ',', ';')));
        var ips = Distinct(Regex.Matches(text, @"\b(?:\d{1,3}\.){3}\d{1,3}\b").Select(m => m.Value).Where(IsIpv4));
        var numbers = Distinct(Regex.Matches(text, @"(?<![\w.])\d+(?:[.,]\d+)?").Select(m => m.Value));
        if (emails.Count + links.Count + ips.Count + numbers.Count == 0)
            return "Nie znalazłem w tym tekście e-maili, linków, adresów IP ani liczb (" + text.Length + " znaków sprawdzonych lokalnie).";
        var result = new StringBuilder("Wyciągnięte z tekstu (" + text.Length + " znaków, bez wysyłania gdziekolwiek):").AppendLine();
        AppendSection(result, "E-maile", emails, 10);
        AppendSection(result, "Linki", links, 10);
        AppendSection(result, "Adresy IPv4", ips, 10);
        AppendSection(result, "Liczby", numbers, 20);
        return result.ToString().TrimEnd();
    }

    // ------------------------------------------------------------ sort lines
    /// <summary>Sorts lines (or removes duplicates keeping the first occurrence).
    /// Comparison is case-insensitive and culture-free, so the result is always the same.</summary>
    public static string SortLines(string text, bool unique)
    {
        if (text.Trim().Length == 0) return "Podaj kolejne wiersze — osobno albo rozdzielone „ | ”, np. „posortuj linie: zebra | kot | Ala”.";
        string[] lines = Lines(text);
        if (lines.Length == 0) return "Nie znalazłem żadnego wiersza z treścią.";
        if (lines.Length > 2000) return "Za dużo wierszy (" + lines.Length + ") — limit to 2000. Podziel tekst na części.";
        if (unique)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var kept = new List<string>();
            foreach (string line in lines) if (seen.Add(line.Trim())) kept.Add(line.Trim());
            int removed = lines.Length - kept.Count;
            var output = new StringBuilder("Usunąłem duplikaty wierszy (bez rozróżniania wielkości liter): zostało " + kept.Count + " z " + lines.Length + " (usunięte: " + removed + ").");
            output.AppendLine().AppendLine();
            output.Append(string.Join("\n", kept.Take(100)));
            if (kept.Count > 100) output.AppendLine().Append("… pokazuję pierwsze 100 wierszy.");
            return output.ToString().TrimEnd();
        }
        var sorted = lines.Select(line => line.Trim()).OrderBy(line => line, StringComparer.OrdinalIgnoreCase).ToArray();
        var sortedOutput = new StringBuilder("Posortowane wiersze (" + sorted.Length + "):");
        sortedOutput.AppendLine().AppendLine();
        sortedOutput.Append(string.Join("\n", sorted.Take(100)));
        if (sorted.Length > 100) sortedOutput.AppendLine().Append("… pokazuję pierwsze 100 wierszy.");
        return sortedOutput.ToString().TrimEnd();
    }

    // -------------------------------------------------------- amount in words
    private static readonly string[] PolesUnits =
    [
        "zero", "jeden", "dwa", "trzy", "cztery", "pięć", "sześć", "siedem", "osiem", "dziewięć",
        "dziesięć", "jedenaście", "dwanaście", "trzynaście", "czternaście", "piętnaście", "szesnaście",
        "siedemnaście", "osiemnaście", "dziewiętnaście"
    ];
    private static readonly string[] PolesTens = ["", "", "dwadzieścia", "trzydzieści", "czterdzieści", "pięćdziesiąt", "sześćdziesiąt", "siedemdziesiąt", "osiemdziesiąt", "dziewięćdziesiąt"];
    private static readonly string[] PolesHundreds = ["", "sto", "dwieście", "trzysta", "czterysta", "pięćset", "sześćset", "siedemset", "osiemset", "dziewięćset"];

    /// <summary>Writes a PLN amount in Polish words (0 – 999 999 999,99). Deterministic, no culture surprises.</summary>
    public static string AmountInWords(string text)
    {
        string cleaned = text.Trim().TrimEnd('.');
        while (cleaned.EndsWith("zl", StringComparison.OrdinalIgnoreCase) || cleaned.EndsWith("zł", StringComparison.OrdinalIgnoreCase) || cleaned.EndsWith("pln", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[..^2].Trim();
        cleaned = new string(cleaned.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (cleaned.Length == 0) return "Podaj kwotę, np. „kwota slownie: 1234,56”.";
        if (!TryNumber(cleaned, out double value)) return "Nie rozpoznałem kwoty: „" + Shorten(text, 60) + "”. Przykład: „kwota slownie: 1234,56”.";
        if (value < 0) return "Kwoty ujemne to nie moja bajka — podaj wartość od 0 w górę.";
        if (value > 999_999_999.99) return "Kwota jest zbyt duża. Obsługuję do 999 999 999,99 zł.";
        long zloty = (long)Math.Floor(value + 1e-9);
        long groszy = (long)Math.Round((value - zloty) * 100, MidpointRounding.AwayFromZero);
        if (groszy == 100) { groszy = 0; zloty++; }
        string words = zloty == 0 ? "zero złotych" : NumberInPolish(zloty) + " " + PlForm(zloty, "złoty", "złote", "złotych");
        return value.ToString("N2", Pl) + " zł słownie: " + words + " " + groszy + " " + PlForm(groszy, "grosz", "grosze", "groszy") + ".";
    }

    private static string NumberInPolish(long value)
    {
        if (value == 0) return "zero";
        var groups = new List<string>();
        long millions = value / 1_000_000;
        long thousands = value / 1000 % 1000;
        long rest = value % 1000;
        if (millions > 0) groups.Add(GroupInPolish(millions, "milion", "miliony", "milionów"));
        if (thousands > 0) groups.Add(GroupInPolish(thousands, "tysiąc", "tysiące", "tysięcy"));
        if (rest > 0 || groups.Count == 0) groups.Add(Under1000InPolish((int)rest));
        return string.Join(" ", groups);
    }

    private static string GroupInPolish(long value, string one, string few, string many)
    {
        if (value == 1) return one;
        return Under1000InPolish((int)(value % 1000)) + " " + PlForm(value % 1000, one, few, many);
    }

    private static string Under1000InPolish(int value)
    {
        var parts = new List<string>();
        if (value / 100 > 0) parts.Add(PolesHundreds[value / 100]);
        int rest = value % 100;
        if (rest is > 0 and < 20) parts.Add(PolesUnits[rest]);
        else if (rest >= 20)
        {
            parts.Add(PolesTens[rest / 10]);
            if (rest % 10 > 0) parts.Add(PolesUnits[rest % 10]);
        }
        return string.Join(" ", parts);
    }

    private static string PlForm(long value, string one, string few, string many)
    {
        long lastTwo = value % 100, last = value % 10;
        if (value == 1) return one;
        if (lastTwo is >= 12 and <= 14) return many;
        if (last is >= 2 and <= 4) return few;
        return many;
    }

    // ------------------------------------------------------------------ time
    /// <summary>Two directions: seconds → days/hours/minutes, and “2h 15m 10s” → seconds.</summary>
    public static string SecondsText(string text, bool toSeconds)
    {
        if (!toSeconds)
        {
            string digits = new string(text.TakeWhile(c => char.IsDigit(c) || c is ' ' or '.' or ',').ToArray()).Replace(" ", "").Replace(".", "").Replace(",", ".");
            if (digits.Length == 0 || !double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) || seconds < 0)
                return "Podaj liczbę sekund, np. „sekundy: 3661”.";
            if (seconds > 3.15e9) return "To ponad 100 lat — podaj mniejszą liczbę sekund.";
            long total = (long)Math.Round(seconds);
            return total.ToString("N0", Pl) + " s = " + HumanDuration(total) + " (łącznie " + (total / 60.0).ToString("N1", Pl) + " min).";
        }
        var parts = Regex.Matches(text, @"(\d+(?:[.,]\d+)?)\s*(dni|d|godzin|godz|g|h|minut|min|m|sekund|sek|s)\b", RegexOptions.IgnoreCase);
        if (parts.Count == 0)
        {
            if (TryNumber(text.Trim(), out double plain) && plain >= 0) return text.Trim() + " s = " + plain.ToString("N0", Pl) + " s.";
            return "Nie rozpoznałem czasu. Przykłady: „na sekundy: 2h 15m 10s”, „na sekundy: 90min”.";
        }
        double sum = 0; var echo = new List<string>();
        foreach (Match part in parts)
        {
            if (!TryNumber(part.Groups[1].Value, out double number)) continue;
            string unit = part.Groups[2].Value.ToLowerInvariant();
            double factor = unit switch
            {
                "dni" or "d" => 86400,
                "godzin" or "godz" or "g" or "h" => 3600,
                "minut" or "min" or "m" => 60,
                _ => 1
            };
            sum += number * factor;
            echo.Add(number.ToString("0.##", Pl) + " " + UnitName(unit));
        }
        return string.Join(" + ", echo) + " = " + sum.ToString("N0", Pl) + " s (" + HumanDuration((long)Math.Round(sum)) + ").";
    }

    private static string UnitName(string unit) => unit switch
    {
        "dni" or "d" => "dni",
        "godzin" or "godz" or "g" or "h" => "godz.",
        "minut" or "min" or "m" => "min",
        _ => "s"
    };

    private static string HumanDuration(long seconds)
    {
        if (seconds < 60) return seconds + " s";
        var parts = new List<string>();
        long days = seconds / 86400, hours = seconds % 86400 / 3600, minutes = seconds % 3600 / 60, secs = seconds % 60;
        if (days > 0) parts.Add(days + " d");
        if (hours > 0) parts.Add(hours + " h");
        if (minutes > 0) parts.Add(minutes + " min");
        if (secs > 0 && days == 0) parts.Add(secs + " s");
        return string.Join(" ", parts);
    }

    // ------------------------------------------------------- password strength
    /// <summary>Estimates password strength locally. The password itself is never echoed back,
    /// never written anywhere and never sent — only its length and character classes are used.</summary>
    public static string PasswordStrength(string password)
    {
        if (password.Length == 0) return "Podaj hasło do sprawdzenia: „moc hasla: …”. Nie zapisuję go i nigdzie nie wysyłam.";
        int pool = 0;
        bool lower = password.Any(char.IsLower), upper = password.Any(char.IsUpper), digits = password.Any(char.IsDigit), symbols = password.Any(c => !char.IsLetterOrDigit(c));
        if (lower) pool += 26;
        if (upper) pool += 26;
        if (digits) pool += 10;
        if (symbols) pool += 33;
        if (pool == 0) pool = 26;
        double entropy = password.Length * Math.Log2(pool);
        string verdict = entropy switch
        {
            < 28 => "bardzo słabe",
            < 40 => "słabe",
            < 60 => "średnie",
            < 80 => "silne",
            _ => "bardzo silne"
        };
        var warnings = new List<string>();
        string lowered = password.ToLowerInvariant();
        foreach (string pattern in new[] { "123", "1234", "qwerty", "haslo", "hasło", "password", "admin", "111", "abc", "0000", "test" })
            if (lowered.Contains(pattern, StringComparison.Ordinal)) { warnings.Add("zawiera typowy fragment „" + pattern + "”"); break; }
        int distinct = password.Distinct().Count();
        if (password.Length < 12) warnings.Add("krótsze niż 12 znaków");
        if (distinct <= password.Length / 2) warnings.Add("dużo powtórzonych znaków (" + distinct + " różnych na " + password.Length + ")");
        if (!upper) warnings.Add("brak wielkich liter");
        if (!digits) warnings.Add("brak cyfr");
        if (!symbols) warnings.Add("brak znaków specjalnych");
        double seconds = Math.Pow(2, entropy) / 1e10; // 10 mld prób na sekundę — rząd wielkości lamacza GPU
        var result = new StringBuilder();
        result.Append("Ocena hasła (nie pokazuję treści, nie zapisuję jej i nigdzie nie wysyłam):").AppendLine();
        result.Append("· długość: ").Append(password.Length).Append(" znaków, różnych znaków: ").Append(distinct).AppendLine();
        result.Append("· zestaw znaków: ").Append(pool).Append(" możliwych · entropia ok. ").Append(entropy.ToString("N1", Pl)).Append(" bitów").AppendLine();
        result.Append("· ocena: ").Append(verdict).Append(" · złamanie siłowe przy 10 mld prób/s: ok. ").Append(HumanCrackTime(seconds)).AppendLine();
        result.Append(warnings.Count == 0 ? "· nie widzę typowych słabości." : "· uwagi: " + string.Join(", ", warnings) + ".");
        result.AppendLine().Append("To szacunek oparty na entropii i słowniku typowych fragmentów, a nie gwarancja — unikalne hasło z menedżera haseł zawsze wygrywa.");
        return result.ToString();
    }

    private static string HumanCrackTime(double seconds)
    {
        if (seconds < 1) return "mniej niż sekundę";
        if (seconds < 60) return seconds.ToString("N0", Pl) + " s";
        if (seconds < 3600) return (seconds / 60).ToString("N0", Pl) + " min";
        if (seconds < 86400) return (seconds / 3600).ToString("N0", Pl) + " godz.";
        if (seconds < 31_536_000) return (seconds / 86400).ToString("N0", Pl) + " dni";
        double years = seconds / 31_536_000;
        if (years < 1e6) return years.ToString("N0", Pl) + " lat";
        return "ponad milion lat";
    }

    // -------------------------------------------------------------- QR codes
    /// <summary>Writes a QR code PNG into the app's data folder (or a directory given by the caller).
    /// Pure local rendering — QRCoder needs no drawing subsystem for the PNG encoder.</summary>
    public static string QrCode(string command, string? directory = null)
    {
        string normalized = ConversationMemoryService.Normalize(command);
        bool wifi = normalized.StartsWith("qr wifi", StringComparison.Ordinal);
        string payload = Argument(command, "qr wifi", "qr");
        if (payload.Length == 0) return "Podaj treść kodu, np. „qr: https://example.com” albo „qr wifi: MojaSiec|tajnehaslo”. W kodzie QR nie umieszczam żadnych danych poza tym, co podasz.";
        if (payload.Length > 1200) return "Treść jest za długa na czytelny kod QR (limit 1200 znaków).";
        if (wifi)
        {
            string[] halves = payload.Split('|', 2);
            if (halves.Length < 2) return "Format Wi-Fi: „qr wifi: nazwa_sieci|hasło”. Hasło zostanie zapisane tylko w kodzie na Twoim dysku.";
            payload = "WIFI:T:WPA;S:" + EscapeWifi(halves[0]) + ";P:" + EscapeWifi(halves[1]) + ";;";
        }
        try
        {
            string target = directory ?? Path.Combine(AppPaths.Root, "Qr");
            Directory.CreateDirectory(target);
            string name = "qr-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png";
            string path = Path.Combine(target, name);
            using (var generator = new QRCodeGenerator())
            using (QRCodeData data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M))
            {
                byte[] png = new PngByteQRCode(data).GetGraphic(8);
                File.WriteAllBytes(path, png);
                var result = new StringBuilder();
                result.Append("Kod QR zapisany lokalnie:").AppendLine();
                result.Append(path).AppendLine();
                result.Append("Rozmiar pliku: ").Append(HumanBytes(png.Length)).Append(" · treść: ").Append(Shorten(wifi ? "dane sieci Wi-Fi (nazwa i hasło)" : payload, 100)).AppendLine();
                result.Append("Zeskanuj telefonem. Plik nie jest nigdzie wysyłany — zostaje na tym komputerze.");
                return result.ToString();
            }
        }
        catch (Exception ex) { return "Nie udało się zapisać kodu QR (" + ex.GetType().Name + "): " + ex.Message; }
    }

    private static string EscapeWifi(string value) => value.Trim().Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace(":", "\\:");

    // ------------------------------------------------------------- helpers
    private static (string Left, string Right, string? Error) SplitTwo(string text)
    {
        string[] halves = text.Split("|||", 2, StringSplitOptions.None);
        if (halves.Length == 2) return (halves[0].Trim(), halves[1].Trim(), null);
        string[] dashed = Regex.Split(text, @"^\s*-{3,}\s*$", RegexOptions.Multiline);
        if (dashed.Length == 2) return (dashed[0].Trim(), dashed[1].Trim(), null);
        return (string.Empty, string.Empty, DiffSeparatorHint);
    }

    /// <summary>Lines of a tool argument. The chat pipeline flattens newlines into spaces
    /// (verified by CI), so „ | ” works as an explicit separator as well.</summary>
    private static string[] Lines(string text)
    {
        string[] byNewline = SplitLines(text).Where(line => line.Trim().Length > 0).ToArray();
        if (byNewline.Length > 1) return byNewline;
        return text.Split(" | ", StringSplitOptions.None).Where(line => line.Trim().Length > 0).ToArray();
    }

    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    private static string HumanBytes(long bytes) => bytes switch
    {
        < 1024 => bytes + " B",
        < 1024 * 1024 => (bytes / 1024d).ToString("0.0", Pl) + " KB",
        < 1024L * 1024 * 1024 => (bytes / (1024d * 1024)).ToString("0.0", Pl) + " MB",
        _ => (bytes / (1024d * 1024 * 1024)).ToString("0.00", Pl) + " GB"
    };

    /// <summary>Turns “pobrane\plik.zip”, “~/raport.pdf” or an environment variable into a full path.
    /// Returns null for empty or impossible input — the caller answers honestly.</summary>
    private static string? ResolvePath(string raw)
    {
        string value = raw.Trim().Trim('"').Trim();
        if (value.Length == 0) return null;
        try
        {
            value = Environment.ExpandEnvironmentVariables(value);
            if (value.StartsWith("~/", StringComparison.Ordinal) || value.StartsWith("~\\", StringComparison.Ordinal))
                value = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), value[2..]);
            string normalized = ConversationMemoryService.Normalize(value);
            (string Prefix, Environment.SpecialFolder Folder)[] shortcuts =
            [
                ("pulpit", Environment.SpecialFolder.Desktop),
                ("desktop", Environment.SpecialFolder.Desktop),
                ("pobrane", Environment.SpecialFolder.UserProfile),
                ("dokumenty", Environment.SpecialFolder.MyDocuments),
                ("obrazki", Environment.SpecialFolder.MyPictures),
                ("muzyka", Environment.SpecialFolder.MyMusic)
            ];
            foreach (var (prefix, folder) in shortcuts)
            {
                if (!normalized.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string root = folder == Environment.SpecialFolder.UserProfile
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
                    : Environment.GetFolderPath(folder);
                string rest = value[prefix.Length..].TrimStart('\\', '/', ' ', ':');
                value = rest.Length == 0 ? root : Path.Combine(root, rest);
                break;
            }
            return Path.GetFullPath(value);
        }
        catch (Exception) { return null; }
    }

    private static bool IsIpv4(string text) =>
        text.Split('.').Length == 4 && text.Split('.').All(part => byte.TryParse(part, out _));

    private static List<string> Distinct(IEnumerable<string> values)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (string value in values) if (value.Length > 0 && seen.Add(value)) list.Add(value);
        return list;
    }

    private static void AppendSection(StringBuilder builder, string label, IReadOnlyList<string> values, int limit)
    {
        if (values.Count == 0) return;
        builder.Append("· ").Append(label).Append(" (").Append(values.Count).Append("): ");
        builder.Append(string.Join(", ", values.Take(limit).Select(x => Shorten(x, 80))));
        if (values.Count > limit) builder.Append(" … i ").Append(values.Count - limit).Append(" więcej");
        builder.AppendLine();
    }
}
