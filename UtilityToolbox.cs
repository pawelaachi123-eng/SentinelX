using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>Offline, deterministic helper tools. Everything here is a pure function: no network,
/// no file access, no process starts — so each one is testable without a machine.
/// 0.95 splits the workshop additions (diff, regex, file hash, QR, amounts in words) into
/// UtilityToolbox.Extras.cs; the routing below stays in one place so every tool is reachable
/// both from chat and from the Tools page.</summary>
public static partial class UtilityToolbox
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    /// <summary>Handles a command if it is one of the tools; returns null otherwise.
    /// <paramref name="text"/> is the normalized form used for matching, <paramref name="command"/> is what the user
    /// actually typed — arguments keep the user's own characters instead of the normalized ones.</summary>
    public static string? Process(string command, string text)
    {
        string raw = command ?? "";
        // Order matters: the specific tools are matched first, the generic calculator last, so that
        // „policz slowa: …” and „ile to procent 30 z 240” are not swallowed by the arithmetic parser.
        var words = Regex.Match(text, @"^(?:ile slow|policz slowa|ile wyrazow)[:\s]+(.+)$");
        if (words.Success) return CountWords(Argument(raw, "ile slow", "policz slowa", "ile wyrazow"));

        // --- percentages and VAT ("ile to procent X z Y" is a share, so it is checked first) ---
        var shareOf = Regex.Match(text, @"^ile to procent[:\s]+(\d+[.,]?\d*)\s+z\s+(\d+[.,]?\d*)$");
        if (shareOf.Success)
        {
            if (!TryNumber(shareOf.Groups[1].Value, out double part) || !TryNumber(shareOf.Groups[2].Value, out double whole)) return NumberError;
            if (whole == 0) return "Nie dzielę przez zero.";
            return part.ToString("0.##", Pl) + " z " + whole.ToString("0.##", Pl) + " = " + (part / whole * 100d).ToString("0.##", Pl) + "%";
        }
        var percentOf = Regex.Match(text, @"^(?:procent|ile procent)[:\s]+(\d+[.,]?\d*)\s*(?:%|procent)?\s*z\s+(\d+[.,]?\d*)$");
        if (percentOf.Success)
        {
            if (!TryNumber(percentOf.Groups[1].Value, out double part) || !TryNumber(percentOf.Groups[2].Value, out double whole)) return NumberError;
            return part.ToString("0.##", Pl) + "% z " + whole.ToString("0.##", Pl) + " = " + (part / 100d * whole).ToString("0.####", Pl);
        }
        var vat = Regex.Match(text, @"^vat[:\s]+(\d+[.,]?\d*)(?:\s*(netto|brutto))?$");
        if (vat.Success)
        {
            if (!TryNumber(vat.Groups[1].Value, out double amount)) return NumberError;
            bool gross = vat.Groups[2].Value == "brutto";
            double net = gross ? amount / 1.23d : amount;
            double total = gross ? amount : amount * 1.23d;
            return "VAT 23% · netto " + net.ToString("0.00", Pl) + " zł · VAT " + (total - net).ToString("0.00", Pl) + " zł · brutto " + total.ToString("0.00", Pl) + " zł\n" +
                "Stawka 23% jest wpisana na stałe. Innych stawek (8%, 5%, 0%) jeszcze nie liczę — to znane ograniczenie.";
        }

        // --- unit conversion ---
        var convert = Regex.Match(text, @"^(?:przelicz|konwertuj|ile to)[:\s]+(-?\d+[.,]?\d*)\s*([a-z°/]+)\s*(?:na|w|to|→|->)\s*([a-z°/]+)$");
        if (convert.Success)
        {
            if (!TryNumber(convert.Groups[1].Value, out double value)) return NumberError;
            return ConvertUnit(value, convert.Groups[2].Value.Trim(), convert.Groups[3].Value.Trim());
        }

        // --- dates and time ---
        var daysTo = Regex.Match(text, @"^ile dni (do|od)[:\s]+(\d{1,2}[.\-/]\d{1,2}(?:[.\-/]\d{2,4})?)$");
        if (daysTo.Success) return DaysBetween(daysTo.Groups[1].Value, daysTo.Groups[2].Value);
        var weekday = Regex.Match(text, @"^jaki dzien tygodnia[:\s]+(\d{1,2}[.\-/]\d{1,2}(?:[.\-/]\d{2,4})?)$");
        if (weekday.Success) return WeekdayOf(weekday.Groups[1].Value);
        var untilHour = Regex.Match(text, @"^ile (zostalo|zostało) do[:\s]+(\d{1,2}):(\d{2})$");
        if (untilHour.Success) return TimeUntil(int.Parse(untilHour.Groups[2].Value), int.Parse(untilHour.Groups[3].Value));

        // --- text tools ---
        var b64 = Regex.Match(text, @"^base64[:\s]+(.+)$");
        if (b64.Success) return ToBase64(Argument(raw, "base64"));
        var b64back = Regex.Match(text, @"^dekoduj base64[:\s]+(.+)$");
        if (b64back.Success) return FromBase64(Argument(raw, "dekoduj base64"));
        var hash = Regex.Match(text, @"^(?:hash tekstu|sha256)[:\s]+(.+)$");
        if (hash.Success) return HashText(Argument(raw, "hash tekstu", "sha256"));
        var json = Regex.Match(text, @"^(?:json|sprawdz json)[:\s]+(.+)$", RegexOptions.Singleline);
        if (json.Success) return FormatJson(Argument(raw, "json", "sprawdz json"));
        var slug = Regex.Match(text, @"^slug[:\s]+(.+)$");
        if (slug.Success) return Slugify(Argument(raw, "slug"));
        var translit = Regex.Match(text, @"^transliteruj[:\s]+(.+)$");
        if (translit.Success) return Transliterate(Argument(raw, "transliteruj"));
        var upper = Regex.Match(text, @"^wielkie litery[:\s]+(.+)$");
        if (upper.Success) return Argument(raw, "wielkie litery").ToUpper(Pl);
        var lower = Regex.Match(text, @"^male litery[:\s]+(.+)$");
        if (lower.Success) return Argument(raw, "male litery").ToLower(Pl);
        var reverse = Regex.Match(text, @"^odwroc tekst[:\s]+(.+)$");
        if (reverse.Success) return new string(Argument(raw, "odwroc tekst").Reverse().ToArray());

        // --- randomness (cryptographic, never used for secrets that need auditing) ---
        var pick = Regex.Match(text, @"^losuj[:\s]+(\d{1,9})(?:\s*(?:-|do|,)\s*(\d{1,9}))?$");
        if (pick.Success) return Roll(pick.Groups[1].Value, pick.Groups[2].Value);
        var dice = Regex.Match(text, @"^rzuc kostk(?:a|ami|e)(?:[:\s]+(\d{1,2}))?$");
        if (dice.Success) return Dice(dice.Groups[1].Success ? int.Parse(dice.Groups[1].Value) : 1);
        var choose = Regex.Match(text, @"^wybierz losowo[:\s]+(.+)$");
        if (choose.Success) return Choose(Argument(raw, "wybierz losowo"));

        // --- identifiers and codes ---
        if (text is "haslo" or "haslo 16" || text.StartsWith("haslo ", StringComparison.Ordinal) || text.StartsWith("generuj haslo", StringComparison.Ordinal))
            return Password(text);
        if (text is "uuid" or "guid" or "generuj uuid" or "generuj guid")
            return "UUID: " + Guid.NewGuid().ToString();
        var roman = Regex.Match(text, @"^rzymskie[:\s]+(\d{1,4})$");
        if (roman.Success) return ToRoman(int.Parse(roman.Groups[1].Value));
        var romanBack = Regex.Match(text, @"^z rzymskich[:\s]+([mdclxviMDCLXVI]{1,10})$");
        if (romanBack.Success) return FromRoman(romanBack.Groups[1].Value);
        var color = Regex.Match(text, @"^kolor[:\s]+#?([0-9a-fA-F]{6})$");
        if (color.Success) return DescribeColor(color.Groups[1].Value);
        var bmi = Regex.Match(text, @"^bmi[:\s]+(\d{2,3}(?:[.,]\d)?)\s+(\d{2,3})$");
        if (bmi.Success) return Bmi(bmi.Groups[1].Value, bmi.Groups[2].Value);

        // --- 0.91 · CENTRUM: exact system facts and calendar helpers (before the generic patterns) ---
        if (text is "tydzien roku" or "numer tygodnia") return WeekOfYear();
        if (text is "dzien roku" or "ktory dzien roku") return DayOfYear();
        if (text is "ile dni do konca roku" or "ile dni zostalo do konca roku") return DaysToYearEnd();
        if (text is "rzut moneta" or "moneta" or "orzel czy reszka") return CoinFlip();
        if (text is "lotto" or "lotek" or "duzy lotek") return Lotto();
        if (text is "moje ip" or "moj adres ip" or "lokalne ip") return LocalIp();
        if (text is "nazwa komputera" or "jak sie nazywa komputer") return "Komputer: " + Environment.MachineName;
        if (text is "ile rdzeni" or "ile watkow") return "Rdzenie logiczne: " + Environment.ProcessorCount;
        if (text is "architektura" or "jaka architektura") return ArchitectureInfo();
        var pin = Regex.Match(text, @"^pin[:\s]+(\d{1,2})$");
        if (pin.Success) return Pin(int.Parse(pin.Groups[1].Value));

        // --- 0.91 · CENTRUM: math beyond the calculator ---
        var sqrt = Regex.Match(text, @"^pierwiastek(?:\s+z)?[:\s]+(-?\d+[.,]?\d*)$");
        if (sqrt.Success) return Sqrt(sqrt.Groups[1].Value);
        var fact = Regex.Match(text, @"^silnia[:\s]+(\d{1,3})$");
        if (fact.Success) return Factorial(int.Parse(fact.Groups[1].Value));
        var gcd = Regex.Match(text, @"^nwd[:\s]+(\d{1,12})\s+(\d{1,12})$");
        if (gcd.Success) return GcdLcm(long.Parse(gcd.Groups[1].Value), long.Parse(gcd.Groups[2].Value), lcm: false);
        var lcm = Regex.Match(text, @"^nww[:\s]+(\d{1,12})\s+(\d{1,12})$");
        if (lcm.Success) return GcdLcm(long.Parse(lcm.Groups[1].Value), long.Parse(lcm.Groups[2].Value), lcm: true);
        var prime = Regex.Match(text, @"^czy pierwsza[:\s]+(\d{1,12})$");
        if (prime.Success) return IsPrime(long.Parse(prime.Groups[1].Value));
        var divisors = Regex.Match(text, @"^dzielniki[:\s]+(\d{1,6})$");
        if (divisors.Success) return Divisors(int.Parse(divisors.Groups[1].Value));
        var fib = Regex.Match(text, @"^fibonacci[:\s]+(\d{1,3})$");
        if (fib.Success) return Fibonacci(int.Parse(fib.Groups[1].Value));
        var stats = Regex.Match(text, @"^(srednia|mediana|suma|min|max)[:\s]+(.+)$");
        if (stats.Success) return Stats(stats.Groups[1].Value, Argument(raw, stats.Groups[1].Value));
        var round = Regex.Match(text, @"^zaokraglij[:\s]+(-?\d+[.,]?\d*)\s+do\s+(\d)(?:\s+miejsc(?:a|ow)?(?:\s+po\s+przecinku)?)?$");
        if (round.Success) return RoundTo(round.Groups[1].Value, int.Parse(round.Groups[2].Value));
        var change = Regex.Match(text, @"^zmiana(?:\s+procentowa)?\s+z\s+(-?\d+[.,]?\d*)\s+do\s+(-?\d+[.,]?\d*)$");
        if (change.Success) return PercentChange(change.Groups[1].Value, change.Groups[2].Value);

        // --- 0.91 · CENTRUM: text analysis ---
        var chars = Regex.Match(text, @"^ile znakow[:\s]+(.+)$");
        if (chars.Success) return CountChars(Argument(raw, "ile znakow"));
        var sentences = Regex.Match(text, @"^ile zdan[:\s]+(.+)$");
        if (sentences.Success) return CountSentences(Argument(raw, "ile zdan"));
        var palindrome = Regex.Match(text, @"^palindrom[:\s]+(.+)$");
        if (palindrome.Success) return Palindrome(Argument(raw, "palindrom"));
        var anagram = Regex.Match(text, @"^anagram[:\s]+(.+)$");
        if (anagram.Success) return Anagram(Argument(raw, "anagram"));
        var rot13 = Regex.Match(text, @"^rot13[:\s]+(.+)$");
        if (rot13.Success) return "ROT13: " + Rot13(Argument(raw, "rot13"));
        var title = Regex.Match(text, @"^tytul[:\s]+(.+)$");
        if (title.Success) return "Tytuł: " + TitleCase(Argument(raw, "tytul"));

        // --- 0.91 · CENTRUM: encodings ---
        var morse = Regex.Match(text, @"^morse[:\s]+(.+)$");
        if (morse.Success) return ToMorse(Argument(raw, "morse"));
        var morseBack = Regex.Match(text, @"^dekoduj morse[:\s]+(.+)$");
        if (morseBack.Success) return FromMorse(Argument(raw, "dekoduj morse"));
        var binary = Regex.Match(text, @"^binarnie[:\s]+(.+)$");
        if (binary.Success) return ToBinary(Argument(raw, "binarnie"));
        var binaryBack = Regex.Match(text, @"^dekoduj binarnie[:\s]+(.+)$");
        if (binaryBack.Success) return FromBinary(Argument(raw, "dekoduj binarnie"));
        var hex = Regex.Match(text, @"^hex[:\s]+(.+)$");
        if (hex.Success) return ToHex(Argument(raw, "hex"));
        var hexBack = Regex.Match(text, @"^dekoduj hex[:\s]+(.+)$");
        if (hexBack.Success) return FromHex(Argument(raw, "dekoduj hex"));

        // --- 0.91 · CENTRUM: Polish identifiers and colour ---
        var pesel = Regex.Match(text, @"^pesel[:\s]+(\d{11})$");
        if (pesel.Success) return Pesel(pesel.Groups[1].Value);
        if (text.StartsWith("pesel", StringComparison.Ordinal)) return "PESEL musi mieć dokładnie 11 cyfr, np. „pesel: 90010112349”. Nie wysyłam go nigdzie — walidacja jest lokalna.";
        var nip = Regex.Match(text, @"^nip[:\s]+(\d{10})$");
        if (nip.Success) return Nip(nip.Groups[1].Value);
        if (text.StartsWith("nip", StringComparison.Ordinal)) return "NIP musi mieć dokładnie 10 cyfr, np. „nip: 1234567802”. Walidacja lokalna, bez wysyłania.";
        var iban = Regex.Match(text, @"^iban[:\s]+([a-z0-9 ]{8,42})$");
        if (iban.Success) return Iban(Argument(raw, "iban"));
        var rgbCmd = Regex.Match(text, @"^rgb[:\s]+(\d{1,3})[ ,]+(\d{1,3})[ ,]+(\d{1,3})$");
        if (rgbCmd.Success) return FromRgb(rgbCmd.Groups[1].Value, rgbCmd.Groups[2].Value, rgbCmd.Groups[3].Value);

        // --- 0.91 · CENTRUM: dates, age, workdays, Easter, world clocks ---
        var age = Regex.Match(text, @"^wiek[:\s]+(.+)$");
        if (age.Success) return Age(Argument(raw, "wiek"));
        var workdays = Regex.Match(text, @"^dni robocze[:\s]+(\d{1,2}[.\-/]\d{1,2}(?:[.\-/]\d{2,4})?)\s*(?:do|-|–)\s*(\d{1,2}[.\-/]\d{1,2}(?:[.\-/]\d{2,4})?)$");
        if (workdays.Success) return Workdays(workdays.Groups[1].Value, workdays.Groups[2].Value);
        var easter = Regex.Match(text, @"^wielkanoc[:\s]+(\d{4})$");
        if (easter.Success) return Easter(int.Parse(easter.Groups[1].Value));
        var clock = Regex.Match(text, @"^czas w[:\s]+(.+)$");
        if (clock.Success) return WorldClock(text[^clock.Groups[1].Length..].Trim());

        // --- 0.95 · WARSZTAT: diff, regex, hash pliku, wyciąganie danych, porządki w tekście ---
        var diff = Regex.Match(text, @"^(?:porownaj teksty|diff)[:\s]+(.+)$", RegexOptions.Singleline);
        if (diff.Success) return DiffText(Argument(raw, "porownaj teksty", "diff"));
        var regexTest = Regex.Match(text, @"^(?:regex|sprawdz wzor)[:\s]+(.+)$", RegexOptions.Singleline);
        if (regexTest.Success) return RegexTest(Argument(raw, "regex", "sprawdz wzor"));
        if (Regex.IsMatch(text, @"^(?:sha256 pliku|md5 pliku|hash pliku)[:\s]+.+$", RegexOptions.Singleline)) return FileHash(raw.Trim());
        var extract = Regex.Match(text, @"^(?:wyciagnij z tekstu|wyciagnij)[:\s]+(.+)$", RegexOptions.Singleline);
        if (extract.Success) return Extract(Argument(raw, "wyciagnij z tekstu", "wyciagnij"));
        var sortLines = Regex.Match(text, @"^(?:posortuj linie|posortuj wiersze)[:\s]+(.+)$", RegexOptions.Singleline);
        if (sortLines.Success) return SortLines(Argument(raw, "posortuj wiersze", "posortuj linie"), unique: false);
        var uniqueLines = Regex.Match(text, @"^(?:unikalne linie|tylko unikalne linie)[:\s]+(.+)$", RegexOptions.Singleline);
        if (uniqueLines.Success) return SortLines(Argument(raw, "tylko unikalne linie", "unikalne linie"), unique: true);
        var amountCommand = Regex.Match(text, @"^(?:kwota slownie|slownie)[:\s]+(.+)$");
        if (amountCommand.Success) return AmountInWords(Argument(raw, "kwota slownie", "slownie"));
        var toSeconds = Regex.Match(text, @"^(?:na sekundy|ile to sekund)[:\s]+(.+)$");
        if (toSeconds.Success) return SecondsText(Argument(raw, "na sekundy", "ile to sekund"), toSeconds: true);
        var seconds = Regex.Match(text, @"^sekundy[:\s]+(.+)$");
        if (seconds.Success) return SecondsText(Argument(raw, "sekundy"), toSeconds: false);
        var strength = Regex.Match(text, @"^(?:moc hasla|sila hasla)[:\s]+(.+)$", RegexOptions.Singleline);
        if (strength.Success) return PasswordStrength(Argument(raw, "moc hasla", "sila hasla"));
        var qr = Regex.Match(text, @"^qr(?: wifi)?[:\s]+.+$", RegexOptions.Singleline);
        if (qr.Success) return QrCode(raw.Trim());

        // --- arithmetic (last: it is the most generic pattern) ---
        var calc = Regex.Match(text, @"^(?:policz|kalkulator|ile to|oblicz)[:\s]+(.+)$");
        if (calc.Success) return Calculate(calc.Groups[1].Value.Trim());
        return null;
    }

    private const string NumberError = "Nie rozpoznałem liczb. Użyj cyfr, np. „policz 12,5 * 4” albo „przelicz 5 km na mile”.";

    /// <summary>Cuts the argument out of the user's original text, so casing and Polish characters survive.
    /// Falls back to the normalized match when the prefix cannot be located.</summary>
    private static string Argument(string raw, params string[] prefixes)
    {
        string normalized = ConversationMemoryService.Normalize(raw);
        foreach (string prefix in prefixes)
        {
            if (!normalized.StartsWith(prefix, StringComparison.Ordinal) || raw.Length <= prefix.Length) continue;
            return raw[prefix.Length..].Trim().TrimStart(':').Trim();
        }
        return raw;
    }

    private static bool TryNumber(string text, out double value)
    {
        string normalized = text.Replace(',', '.').Trim();
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    private static string Format(double value) => value.ToString("0.####", Pl);

    // ------------------------------ arithmetic ------------------------------
    public static string Calculate(string expression)
    {
        if (expression.Length > 200) return "Wyrażenie jest za długie (limit 200 znaków).";
        try
        {
            double result = new ExpressionParser(expression).Parse();
            if (!double.IsFinite(result)) return "Wynik nie jest skończoną liczbą.";
            return expression + " = " + Format(result);
        }
        catch (InvalidOperationException ex) { return "Nie policzę tego: " + ex.Message; }
    }

    /// <summary>Recursive-descent parser for + - * / % ^ and parentheses. Never evaluates code.</summary>
    private sealed class ExpressionParser
    {
        private readonly string text;
        private int position;
        public ExpressionParser(string text) => this.text = text.Replace(',', '.').Replace(" ", "");
        public double Parse()
        {
            double value = ParseAddSub();
            if (position < text.Length) throw new InvalidOperationException("nieoczekiwany znak „" + text[position] + "”");
            return value;
        }
        private double ParseAddSub()
        {
            double value = ParseMulDiv();
            while (position < text.Length && (text[position] == '+' || text[position] == '-'))
            {
                char op = text[position++];
                double right = ParseMulDiv();
                value = op == '+' ? value + right : value - right;
            }
            return value;
        }
        private double ParseMulDiv()
        {
            double value = ParsePower();
            while (position < text.Length && (text[position] is '*' or '/' or '%'))
            {
                char op = text[position++];
                double right = ParsePower();
                if (op == '/' && right == 0) throw new InvalidOperationException("dzielenie przez zero");
                value = op switch { '*' => value * right, '/' => value / right, _ => value % right };
            }
            return value;
        }
        private double ParsePower()
        {
            double value = ParseUnary();
            if (position < text.Length && text[position] == '^') { position++; value = Math.Pow(value, ParsePower()); }
            return value;
        }
        private double ParseUnary()
        {
            if (position < text.Length && text[position] == '-') { position++; return -ParseUnary(); }
            if (position < text.Length && text[position] == '+') { position++; return ParseUnary(); }
            return ParsePrimary();
        }
        private double ParsePrimary()
        {
            if (position >= text.Length) throw new InvalidOperationException("urwane wyrażenie");
            if (text[position] == '(')
            {
                position++;
                double parenthesized = ParseAddSub();
                if (position >= text.Length || text[position] != ')') throw new InvalidOperationException("brak nawiasu )");
                position++;
                return parenthesized;
            }
            int start = position;
            while (position < text.Length && (char.IsDigit(text[position]) || text[position] == '.')) position++;
            if (start == position) throw new InvalidOperationException("oczekiwałem liczby przy „" + text[position] + "”");
            string number = text[start..position];
            if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
                throw new InvalidOperationException("nie rozpoznano liczby „" + number + "”");
            return value;
        }
    }

    // ------------------------------ units ------------------------------
    private static readonly Dictionary<string, double> Factors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mm"] = 0.001, ["cm"] = 0.01, ["m"] = 1, ["km"] = 1000, ["in"] = 0.0254, ["ft"] = 0.3048, ["yd"] = 0.9144, ["mi"] = 1609.344,
        ["g"] = 0.001, ["kg"] = 1, ["t"] = 1000, ["oz"] = 0.0283495, ["lb"] = 0.453592,
        ["ml"] = 0.001, ["l"] = 1, ["gal"] = 3.78541, ["pt"] = 0.473176,
        ["b"] = 1d / 8, ["kb"] = 1024d / 8, ["mb"] = 1048576d / 8, ["gb"] = 1073741824d / 8, ["tb"] = 1099511627776d / 8,
        ["ms"] = 1, ["kmh"] = 1d / 3.6, ["km/h"] = 1d / 3.6, ["mph"] = 0.44704, ["kn"] = 0.514444
    };

    /// <summary>Polish and spelled-out unit names mapped onto the factor table.</summary>
    private static readonly Dictionary<string, string> UnitAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["milimetr"] = "mm", ["milimetry"] = "mm", ["centymetr"] = "cm", ["centymetry"] = "cm",
        ["metr"] = "m", ["metry"] = "m", ["kilometr"] = "km", ["kilometry"] = "km",
        ["cal"] = "in", ["cale"] = "in", ["stopa"] = "ft", ["stopy"] = "ft", ["jard"] = "yd", ["jardy"] = "yd",
        ["mila"] = "mi", ["mile"] = "mi", ["mil"] = "mi",
        ["gram"] = "g", ["gramy"] = "g", ["kilogram"] = "kg", ["kilogramy"] = "kg", ["kilo"] = "kg",
        ["tona"] = "t", ["tony"] = "t", ["funt"] = "lb", ["funty"] = "lb", ["uncja"] = "oz", ["uncje"] = "oz",
        ["litr"] = "l", ["litry"] = "l", ["mililitr"] = "ml", ["mililitry"] = "ml", ["galon"] = "gal", ["galony"] = "gal",
        ["pinta"] = "pt", ["pinty"] = "pt",
        ["bajt"] = "b", ["bajty"] = "b", ["kilobajt"] = "kb", ["megabajt"] = "mb", ["gigabajt"] = "gb", ["terabajt"] = "tb",
        ["kbps"] = "kb", ["mbps"] = "mb", ["gbps"] = "gb",
        ["kmh"] = "kmh", ["km/h"] = "kmh", ["kilometrow na godzine"] = "kmh", ["ms"] = "ms", ["m/s"] = "ms",
        ["mph"] = "mph", ["mil na godzine"] = "mph", ["wezel"] = "kn", ["wezly"] = "kn", ["wezlów"] = "kn", ["knot"] = "kn",
        ["c"] = "c", ["celsjusz"] = "c", ["celsius"] = "c", ["f"] = "f", ["fahrenheit"] = "f", ["fahrenheita"] = "f",
        ["k"] = "k", ["kelwin"] = "k", ["kelvina"] = "k", ["kelvin"] = "k"
    };

    private static string Canonical(string unit)
    {
        string key = unit.Replace("°", "").Replace("/", "").Trim().ToLowerInvariant();
        return UnitAliases.TryGetValue(key, out string? canonical) ? canonical : key;
    }

    public static string ConvertUnit(double value, string from, string to)
    {
        string fromKey = Canonical(from), toKey = Canonical(to);
        if (fromKey == "c") return toKey switch
        {
            "f" => Format(value * 9d / 5d + 32d) + " °F",
            "k" => Format(value + 273.15d) + " K",
            _ => UnknownUnit(to)
        };
        if (fromKey == "f") return toKey switch
        {
            "c" => Format((value - 32d) * 5d / 9d) + " °C",
            "k" => Format((value - 32d) * 5d / 9d + 273.15d) + " K",
            _ => UnknownUnit(to)
        };
        if (fromKey == "k") return toKey switch
        {
            "c" => Format(value - 273.15d) + " °C",
            "f" => Format((value - 273.15d) * 9d / 5d + 32d) + " °F",
            _ => UnknownUnit(to)
        };
        if (!Factors.TryGetValue(fromKey, out double fromFactor)) return UnknownUnit(from);
        if (!Factors.TryGetValue(toKey, out double toFactor)) return UnknownUnit(to);
        return Format(value * fromFactor / toFactor) + " " + to;
    }

    private static string UnknownUnit(string unit) =>
        "Nie znam jednostki „" + unit + "”. Obsługuję: mm cm m km in ft yd mi · g kg t oz lb · ml l gal pt · b kb mb gb tb · ms km/h mph kn · C F K.";

    // ------------------------------ dates ------------------------------
    private static bool TryDate(string text, out DateTime date)
    {
        date = default;
        string[] parts = text.Replace('-', '.').Replace('/', '.').Split('.');
        if (parts.Length is < 2 or > 3) return false;
        if (!int.TryParse(parts[0], out int day) || !int.TryParse(parts[1], out int month)) return false;
        int year = DateTime.Now.Year;
        if (parts.Length == 3)
        {
            if (!int.TryParse(parts[2], out year)) return false;
            if (year < 100) year += year < 70 ? 2000 : 1900;
        }
        else if (new DateTime(year, month, day) < DateTime.Today) year++;
        try { date = new DateTime(year, month, day); return true; }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    public static string DaysBetween(string direction, string text)
    {
        if (!TryDate(text, out DateTime date)) return "Nie rozpoznałem daty. Przykład: „ile dni do 24.12” albo „ile dni od 1.1.2020”.";
        int days = (int)Math.Round((date.Date - DateTime.Today).TotalDays);
        return direction == "do"
            ? "Do " + date.ToString("dd.MM.yyyy") + " zostało " + Math.Abs(days) + " dni."
            : "Od " + date.ToString("dd.MM.yyyy") + " minęło " + Math.Abs(days) + " dni.";
    }

    public static string WeekdayOf(string text) =>
        TryDate(text, out DateTime date)
            ? date.ToString("dd.MM.yyyy") + " to " + date.ToString("dddd", Pl) + "."
            : "Nie rozpoznałem daty. Przykład: „jaki dzien tygodnia 1.1.2030”.";

    public static string TimeUntil(int hour, int minute)
    {
        if (hour is < 0 or > 23 || minute is < 0 or > 59) return "Godzina musi być w zakresie 0:00–23:59.";
        var target = DateTime.Today.AddHours(hour).AddMinutes(minute);
        if (target <= DateTime.Now) target = target.AddDays(1);
        TimeSpan span = target - DateTime.Now;
        return "Do " + target.ToString("HH:mm") + " zostało " + (int)span.TotalHours + " h " + span.Minutes + " min (" + target.ToString("dd.MM HH:mm") + ").";
    }

    // ------------------------------ text ------------------------------
    public static string CountWords(string text)
    {
        string[] words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return "Słowa: " + words.Length + " · znaki: " + text.Length + " · bez spacji: " + text.Replace(" ", "").Length +
            " · linie: " + text.Split('\n').Length + " · zdania (kropka/?/!): " + Regex.Matches(text, @"[.?!]+").Count;
    }

    public static string ToBase64(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > 100_000) return "Tekst jest za długi (limit 100 kB).";
        return "Base64: " + Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
    }

    public static string FromBase64(string text)
    {
        try
        {
            byte[] bytes = Convert.FromBase64String(text.Replace(" ", ""));
            if (bytes.Length > 100_000) return "Dane są za duże (limit 100 kB).";
            return "Zdekodowane: " + Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException) { return "To nie jest poprawny Base64."; }
    }

    public static string HashText(string text) =>
        "SHA-256 (UTF-8, " + Encoding.UTF8.GetByteCount(text) + " B): " + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    public static string FormatJson(string text)
    {
        if (text.Length > 200_000) return "JSON jest za duży (limit 200 kB).";
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            string pretty = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
            if (pretty.Length > 8000) pretty = pretty[..8000] + "\n…(przycięte do 8000 znaków)";
            return "JSON poprawny.\n" + pretty;
        }
        catch (JsonException ex) { return "JSON jest niepoprawny: " + ex.Message; }
    }

    public static string Slugify(string text)
    {
        string ascii = Transliterate(text).ToLowerInvariant();
        string slug = Regex.Replace(ascii, @"[^a-z0-9]+", "-").Trim('-');
        return slug.Length == 0 ? "Z tego tekstu nie da się zbudować sluga (brak liter ani cyfr)." : "Slug: " + slug;
    }

    public static string Transliterate(string text) =>
        new(text.Select(c => c switch
        {
            'ą' => 'a', 'ć' => 'c', 'ę' => 'e', 'ł' => 'l', 'ń' => 'n', 'ó' => 'o', 'ś' => 's', 'ź' => 'z', 'ż' => 'z',
            'Ą' => 'A', 'Ć' => 'C', 'Ę' => 'E', 'Ł' => 'L', 'Ń' => 'N', 'Ó' => 'O', 'Ś' => 'S', 'Ź' => 'Z', 'Ż' => 'Z',
            _ => c
        }).ToArray());

    // ------------------------------ randomness ------------------------------
    public static string Roll(string from, string to)
    {
        if (!int.TryParse(from, out int low)) return NumberError;
        int high = to.Length == 0 ? low : int.TryParse(to, out int parsed) ? parsed : low;
        if (low > high) (low, high) = (high, low);
        if (high - low > 1_000_000_000) return "Zakres jest za duży.";
        return "Wylosowano " + RandomNumberGenerator.GetInt32(low, high + 1) + " (zakres " + low + "–" + high + ").";
    }

    public static string Dice(int count)
    {
        if (count is < 1 or > 20) return "Można rzucić od 1 do 20 kostek.";
        var rolls = new int[count];
        for (int i = 0; i < count; i++) rolls[i] = RandomNumberGenerator.GetInt32(1, 7);
        return "Kostki: " + string.Join(", ", rolls) + " · suma: " + rolls.Sum();
    }

    public static string Choose(string list)
    {
        string[] options = list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (options.Length < 2) return "Podaj co najmniej dwie opcje po przecinku, np. „wybierz losowo: pizza, sushi”.";
        return "Wybrano: " + options[RandomNumberGenerator.GetInt32(options.Length)] + " (z " + options.Length + " opcji).";
    }

    public static string Password(string command)
    {
        var match = Regex.Match(command, @"(\d{1,3})");
        int length = match.Success ? int.Parse(match.Groups[1].Value) : 16;
        if (length is < 8 or > 128) return "Długość hasła musi wynosić 8–128 znaków.";
        const string lower = "abcdefghijkmnopqrstuvwxyz", upper = "ABCDEFGHJKLMNPQRSTUVWXYZ", digits = "23456789", symbols = "!@#$%^&*-_=+?";
        string all = lower + upper + digits + symbols;
        var characters = new char[length];
        // Guarantee one character from each class, then fill randomly.
        characters[0] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
        characters[1] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
        characters[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        characters[3] = symbols[RandomNumberGenerator.GetInt32(symbols.Length)];
        for (int i = 4; i < length; i++) characters[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
        // Fisher-Yates with a cryptographic source so the fixed positions are not predictable.
        for (int i = length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (characters[i], characters[j]) = (characters[j], characters[i]);
        }
        double entropy = length * Math.Log2(all.Length);
        return "Hasło (" + length + " znaków, ~" + entropy.ToString("0") + " bitów entropii):\n" + new string(characters) +
            "\nWygenerowane lokalnie, nigdzie nie zapisane i nie wysłane.";
    }

    // ------------------------------ codes ------------------------------
    public static string ToRoman(int value)
    {
        if (value is < 1 or > 3999) return "Zamieniam liczby 1–3999.";
        int[] values = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
        string[] symbols = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];
        var builder = new StringBuilder();
        for (int i = 0; i < values.Length && value > 0; i++)
            while (value >= values[i]) { builder.Append(symbols[i]); value -= values[i]; }
        return builder.ToString();
    }

    public static string FromRoman(string text)
    {
        string upper = text.ToUpperInvariant();
        var weights = new Dictionary<char, int> { ['I'] = 1, ['V'] = 5, ['X'] = 10, ['L'] = 50, ['C'] = 100, ['D'] = 500, ['M'] = 1000 };
        int total = 0, previous = 0;
        foreach (char c in upper.Reverse())
        {
            if (!weights.TryGetValue(c, out int weight)) return "To nie jest liczba rzymska („" + c + "”).";
            total += weight < previous ? -weight : weight;
            previous = Math.Max(previous, weight);
        }
        if (total < 1 || ToRoman(total) != upper) return "To nie jest poprawna liczba rzymska.";
        return upper + " = " + total;
    }

    public static string DescribeColor(string hex)
    {
        int r = int.Parse(hex[..2], NumberStyles.HexNumber), g = int.Parse(hex[2..4], NumberStyles.HexNumber), b = int.Parse(hex[4..6], NumberStyles.HexNumber);
        double rn = r / 255d, gn = g / 255d, bn = b / 255d;
        double max = Math.Max(rn, Math.Max(gn, bn)), min = Math.Min(rn, Math.Min(gn, bn)), delta = max - min;
        double hue = 0;
        if (delta > 0)
            hue = max == rn ? 60 * (((gn - bn) / delta) % 6) : max == gn ? 60 * ((bn - rn) / delta + 2) : 60 * ((rn - gn) / delta + 4);
        if (hue < 0) hue += 360;
        double lightness = (max + min) / 2d;
        double saturation = delta == 0 ? 0 : delta / (1 - Math.Abs(2 * lightness - 1));
        double luminance = 0.2126 * Linear(rn) + 0.7152 * Linear(gn) + 0.0722 * Linear(bn);
        return "#" + hex.ToLowerInvariant() + " · RGB(" + r + ", " + g + ", " + b + ") · HSL(" + hue.ToString("0") + "°, " +
            (saturation * 100).ToString("0", Pl) + "%, " + (lightness * 100).ToString("0", Pl) + "%) · luminancja " + luminance.ToString("0.000", Pl) +
            " · kontrast z bielą " + Contrast(luminance, 1).ToString("0.0", Pl) + ":1, z czernią " + Contrast(luminance, 0).ToString("0.0", Pl) + ":1";
    }

    private static double Linear(double channel) => channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    private static double Contrast(double a, double b) => (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);

    public static string Bmi(string weightText, string heightText)
    {
        if (!TryNumber(weightText, out double kg) || !TryNumber(heightText, out double cm)) return NumberError;
        if (kg is < 20 or > 400 || cm is < 100 or > 250) return "Podaj wagę 20–400 kg i wzrost 100–250 cm.";
        double meters = cm / 100d;
        double bmi = kg / (meters * meters);
        string range = bmi switch
        {
            < 18.5 => "niedowaga",
            < 25 => "wartość w zakresie referencyjnym",
            < 30 => "nadwaga",
            < 35 => "otyłość I stopnia",
            < 40 => "otyłość II stopnia",
            _ => "otyłość III stopnia"
        };
        return "BMI " + bmi.ToString("0.0", Pl) + " (" + range + ") dla " + kg.ToString("0.#", Pl) + " kg i " + cm.ToString("0", Pl) +
            " cm.\nBMI jest wskaźnikiem orientacyjnym — nie mierzy składu ciała ani stanu zdrowia.";
    }

    // ------------------------------ 0.91 · CENTRUM: calendar facts ------------------------------
    public static string WeekOfYear()
    {
        var now = DateTime.Now;
        return "Tydzień: " + ISOWeek.GetWeekOfYear(now) + " (ISO 8601) · dzień roku: " + now.DayOfYear + " · " + now.ToString("dddd, dd.MM.yyyy", Pl);
    }

    public static string DayOfYear()
    {
        var now = DateTime.Now;
        int daysInYear = DateTime.IsLeapYear(now.Year) ? 366 : 365;
        return "Dzień roku: " + now.DayOfYear + " z " + daysInYear + " (" + (daysInYear - now.DayOfYear) + " dni do końca roku).";
    }

    public static string DaysToYearEnd()
    {
        var now = DateTime.Now;
        var end = new DateTime(now.Year, 12, 31);
        return "Do końca roku: " + (end - now.Date).Days + " dni.";
    }

    /// <summary>Anonymous Gregorian algorithm (deterministic for years 1900–2200).</summary>
    public static string Easter(int year)
    {
        if (year is < 1900 or > 2200) return "Podaj rok z zakresu 1900–2200.";
        int a = year % 19, b = year / 100, c = year % 100;
        int d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int month = (h + l - 7 * m + 114) / 31, day = (h + l - 7 * m + 114) % 31 + 1;
        return "Wielkanoc " + year + ": " + new DateTime(year, month, day).ToString("dd.MM.yyyy", Pl) + " (algorytm Gaussa, kalendarz gregoriański).";
    }

    /// <summary>Age in full years for a birth date. The date must be in the past.</summary>
    public static string Age(string text)
    {
        if (!TryDate(text, out DateTime birth) || birth > DateTime.Now) return "Podaj datę urodzenia w przeszłości, np. „wiek: 01.01.1990”.";
        var today = DateTime.Now;
        int years = today.Year - birth.Year;
        if (birth.Date > today.AddYears(-years)) years--;
        var next = new DateTime(today.Year, birth.Month, birth.Day);
        if (next <= today) next = next.AddYears(1);
        return "Wiek: " + years + " lat (ur. " + birth.ToString("dd.MM.yyyy", Pl) + "). Następne urodziny za " + (next - today.Date).Days + " dni.";
    }

    /// <summary>Weekdays (Mon–Fri) between two dates, both ends inclusive. Polish public holidays are NOT subtracted — honestly.</summary>
    public static string Workdays(string fromText, string toText)
    {
        if (!TryDate(fromText, out DateTime from) || !TryDate(toText, out DateTime to)) return "Nie rozpoznałem dat. Użyj formatu DD.MM.RRRR, np. „dni robocze 1.1.2024 do 31.1.2024”.";
        if (to < from) (from, to) = (to, from);
        if ((to - from).Days > 1100) return "Zakres jest za duży — maksymalnie około 3 lata.";
        int total = 0, work = 0;
        for (DateTime d = from; d <= to; d = d.AddDays(1))
        {
            total++;
            if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday) work++;
        }
        return "Dni robocze: " + work + " (dni razem: " + total + ", weekendy: " + (total - work) + ").\nŚwięta nie są odejmowane — liczę tylko poniedziałki–piątki.";
    }

    /// <summary>Local time in a fixed set of world cities. Offline: uses the operating system's timezone data.</summary>
    public static string WorldClock(string city)
    {
        Dictionary<string, (string Zone, string Name)> zones = new(StringComparer.OrdinalIgnoreCase)
        {
            ["tokio"] = ("Tokyo Standard Time", "Tokio"),
            ["londyn"] = ("GMT Standard Time", "Londyn"),
            ["berlin"] = ("W. Europe Standard Time", "Berlin"),
            ["paryz"] = ("Romance Standard Time", "Paryż"),
            ["nowy jork"] = ("Eastern Standard Time", "Nowy Jork"),
            ["chicago"] = ("Central Standard Time", "Chicago"),
            ["los angeles"] = ("Pacific Standard Time", "Los Angeles"),
            ["seoul"] = ("Korea Standard Time", "Seul"),
        };
        string key = (city ?? "").Trim();
        if (!zones.TryGetValue(key, out var zone))
            return "Nie znam tego miasta. Znam: " + string.Join(", ", zones.Values.Select(x => x.Name.ToLower(Pl))) + ".\nCzas lokalny: " + DateTime.Now.ToString("HH:mm", Pl) + ".";
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(zone.Zone);
            var there = TimeZoneInfo.ConvertTime(DateTime.Now, tz);
            var offset = tz.GetUtcOffset(there);
            string sign = offset < TimeSpan.Zero ? "-" : "+";
            string hours = Math.Abs(offset.Hours).ToString(Pl);
            string minutes = offset.Minutes == 0 ? "" : ":" + Math.Abs(offset.Minutes).ToString("00", Pl);
            return "W " + zone.Name + " jest teraz " + there.ToString("HH:mm", Pl) + ", " + there.ToString("dddd", Pl) + " (UTC" + sign + hours + minutes + ").";
        }
        catch (TimeZoneNotFoundException)
        {
            return "System nie ma danych strefy „" + zone.Name + "” — nie wymyślam czasu.";
        }
    }

    // ------------------------------ 0.91 · CENTRUM: math ------------------------------
    public static string Sqrt(string text)
    {
        if (!TryNumber(text, out double value)) return NumberError;
        if (value < 0) return "Nie liczę pierwiastka z liczby ujemnej — to poza zakresem tego narzędzia.";
        return "√" + Format(value) + " = " + Format(Math.Round(Math.Sqrt(value), 10, MidpointRounding.AwayFromZero));
    }

    public static string Factorial(int n)
    {
        if (n > 500) return "Silnię liczę do 500 — wyżej wynik miałby ponad tysiąc cyfr.";
        BigInteger result = 1;
        for (int i = 2; i <= n; i++) result *= i;
        return n + "! = " + result.ToString(Pl);
    }

    private static long Gcd(long a, long b) { while (b != 0) (a, b) = (b, a % b); return Math.Abs(a); }

    public static string GcdLcm(long a, long b, bool lcm)
    {
        if (a == 0 && b == 0) return "Podaj liczby różne od zera.";
        long gcd = Gcd(a, b);
        if (!lcm) return "NWD(" + a + ", " + b + ") = " + gcd;
        try { checked { return "NWW(" + a + ", " + b + ") = " + Math.Abs(a / gcd * b); } }
        catch (OverflowException) { return "Wynik przekracza zakres — podaj mniejsze liczby."; }
    }

    public static string IsPrime(long n)
    {
        if (n < 2) return n + " nie jest liczbą pierwszą (liczby pierwsze zaczynają się od 2).";
        if (n > 1_000_000_000_000) return "Sprawdzam liczby do biliona — podaj mniejszą.";
        if (n % 2 == 0) return n == 2 ? "2 jest liczbą pierwszą." : n + " nie jest liczbą pierwszą (dzielnik: 2).";
        for (long i = 3; i * i <= n; i += 2)
            if (n % i == 0) return n + " nie jest liczbą pierwszą (dzielnik: " + i + ").";
        return n + " jest liczbą pierwszą.";
    }

    public static string Divisors(int n)
    {
        if (n < 1) return "Podaj liczbę całkowitą większą od zera.";
        var divisors = new SortedSet<int>();
        for (int i = 1; (long)i * i <= n; i++)
            if (n % i == 0) { divisors.Add(i); divisors.Add(n / i); }
        var list = divisors.ToList();
        string shown = list.Count > 300 ? string.Join(", ", list.Take(300)) + ", … (razem " + list.Count + ")" : string.Join(", ", list);
        return "Dzielniki " + n + ": " + shown + " (razem " + list.Count + ").";
    }

    public static string Fibonacci(int n)
    {
        if (n > 200) return "Liczbę Fibonacciego liczę do F(200).";
        BigInteger a = 0, b = 1;
        for (int i = 0; i < n; i++) (a, b) = (b, a + b);
        return "Fibonacci(" + n + ") = " + a.ToString(Pl);
    }

    private static bool TryNumbers(string list, out double[] values)
    {
        values = Regex.Split(list, @"[,;\s]+").Where(x => x.Length > 0)
            .Select(x => TryNumber(x, out double v) ? v : double.NaN).ToArray();
        return values.Length > 0 && !values.Any(double.IsNaN);
    }

    public static string Stats(string kind, string list)
    {
        if (!TryNumbers(list, out double[] values)) return "Podaj liczby po przecinku, np. „" + kind + ": 2, 4, 6”.";
        if (values.Length > 1000) return "Maksymalnie 1000 liczb.";
        switch (kind)
        {
            case "suma": return "Suma: " + Format(values.Sum());
            case "min": return "Minimum: " + Format(values.Min());
            case "max": return "Maksimum: " + Format(values.Max());
            case "srednia": return "Średnia: " + Format(values.Average()) + " (liczby: " + values.Length + ", suma: " + Format(values.Sum()) + ")";
            case "mediana":
                var sorted = values.OrderBy(x => x).ToArray();
                double median = sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2d;
                return "Mediana: " + Format(median);
            default: return "Nie znam operacji „" + kind + "”.";
        }
    }

    public static string RoundTo(string text, int places)
    {
        if (!TryNumber(text, out double value)) return NumberError;
        if (places is < 0 or > 8) return "Podaj od 0 do 8 miejsc po przecinku.";
        return "Zaokrąglone: " + value.ToString("F" + places, Pl);
    }

    public static string PercentChange(string fromText, string toText)
    {
        if (!TryNumber(fromText, out double from) || !TryNumber(toText, out double to)) return NumberError;
        if (from == 0) return "Nie liczę zmiany procentowej od zera.";
        double change = (to - from) / Math.Abs(from) * 100d;
        return "Zmiana z " + Format(from) + " do " + Format(to) + " = " + (change >= 0 ? "+" : "") + change.ToString("0.####", Pl) + "%";
    }

    // ------------------------------ 0.91 · CENTRUM: text analysis ------------------------------
    public static string CountChars(string text)
    {
        if (text.Length > 100_000) return "Za długi tekst (limit 100 000 znaków).";
        return "Znaki: " + text.Length + " (bez spacji: " + text.Count(c => !char.IsWhiteSpace(c)) + ").";
    }

    public static string CountSentences(string text)
    {
        int count = Regex.Matches(text, @"[.!?…]+(?=\s|$)").Count;
        if (count == 0 && text.Trim().Length > 0) count = 1;
        return "Zdania: " + count;
    }

    public static string Palindrome(string text)
    {
        var letters = text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray();
        if (letters.Length == 0) return "Podaj tekst z literami lub cyframi.";
        bool isPalindrome = letters.SequenceEqual(letters.Reverse());
        return "„" + text.Trim() + "” " + (isPalindrome ? "jest palindromem." : "nie jest palindromem.") + " (sprawdzam litery i cyfry, bez spacji i znaków)";
    }

    public static string Anagram(string text)
    {
        string[] parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            return "Podaj dokładnie dwa słowa po przecinku, np. „anagram: kot, tok”.";
        char[] Sort(string s) => s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).OrderBy(x => x).ToArray();
        bool isAnagram = Sort(parts[0]).SequenceEqual(Sort(parts[1]));
        return "„" + parts[0] + "” i „" + parts[1] + "” " + (isAnagram ? "są anagramami." : "nie są anagramami.");
    }

    public static string Rot13(string text) => new(text.Select(c => c switch
    {
        >= 'a' and <= 'z' => (char)('a' + (c - 'a' + 13) % 26),
        >= 'A' and <= 'Z' => (char)('A' + (c - 'A' + 13) % 26),
        _ => c
    }).ToArray());

    public static string TitleCase(string text)
    {
        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(w => char.ToUpper(w[0], Pl) + (w.Length > 1 ? w[1..].ToLower(Pl) : "")));
    }

    // ------------------------------ 0.91 · CENTRUM: encodings ------------------------------
    private static readonly Dictionary<char, string> MorseMap = new()
    {
        ['A'] = ".-", ['B'] = "-...", ['C'] = "-.-.", ['D'] = "-..", ['E'] = ".", ['F'] = "..-.",
        ['G'] = "--.", ['H'] = "....", ['I'] = "..", ['J'] = ".---", ['K'] = "-.-", ['L'] = ".-..",
        ['M'] = "--", ['N'] = "-.", ['O'] = "---", ['P'] = ".--.", ['Q'] = "--.-", ['R'] = ".-.",
        ['S'] = "...", ['T'] = "-", ['U'] = "..-", ['V'] = "...-", ['W'] = ".--", ['X'] = "-..-",
        ['Y'] = "-.--", ['Z'] = "--..", ['0'] = "-----", ['1'] = ".----", ['2'] = "..---",
        ['3'] = "...--", ['4'] = "....-", ['5'] = ".....", ['6'] = "-....", ['7'] = "--...",
        ['8'] = "---..", ['9'] = "----."
    };

    public static string ToMorse(string text)
    {
        string upper = text.Trim().ToUpperInvariant();
        if (upper.Length == 0) return "Podaj tekst — zamienię litery A–Z i cyfry 0–9.";
        var codes = new List<string>();
        foreach (char c in upper)
        {
            if (c == ' ') { codes.Add("/"); continue; }
            if (!MorseMap.TryGetValue(c, out string? code))
                return "Znak „" + c + "” nie ma kodu Morse'a — obsługuję litery A–Z, cyfry 0–9 i spacje.";
            codes.Add(code);
        }
        return "Morse: " + string.Join(' ', codes).Replace(" / ", "  /  ");
    }

    public static string FromMorse(string text)
    {
        var reverse = MorseMap.ToDictionary(x => x.Value, x => x.Key);
        var words = text.Trim().Split('/', StringSplitOptions.TrimEntries);
        var builder = new StringBuilder();
        foreach (string word in words)
        {
            if (builder.Length > 0) builder.Append(' ');
            foreach (string code in word.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!reverse.TryGetValue(code, out char letter)) return "Nie znam kodu „" + code + "”. Używaj kropek i kresek, np. „dekoduj morse: ... --- ...”.";
                builder.Append(letter);
            }
        }
        return "Zdekodowane: " + builder;
    }

    public static string ToBinary(string text)
    {
        if (text.Length > 200) return "Za długi tekst (limit 200 znaków).";
        return "Binarnie: " + string.Join(" ", Encoding.UTF8.GetBytes(text).Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));
    }

    public static string FromBinary(string text)
    {
        string[] bytes = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (bytes.Length == 0) return "Podaj bajty jako ósemki bitów, np. „dekoduj binarnie: 01000001”.";
        var result = new byte[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i].Length != 8 || bytes[i].Any(c => c is not ('0' or '1')))
                return "„" + bytes[i] + "” nie jest bajtem — oczekuję dokładnie 8 znaków 0/1.";
            result[i] = Convert.ToByte(bytes[i], 2);
        }
        return "Zdekodowane: " + Encoding.UTF8.GetString(result);
    }

    public static string ToHex(string text)
    {
        if (text.Length > 200) return "Za długi tekst (limit 200 znaków).";
        return "Hex: " + string.Join(" ", Encoding.UTF8.GetBytes(text).Select(b => b.ToString("X2")));
    }

    public static string FromHex(string text)
    {
        string clean = text.Replace(" ", "").Trim();
        if (clean.Length == 0 || clean.Length % 2 != 0) return "Podaj parzystą liczbę znaków hex, np. „dekoduj hex: 41 6C 61”.";
        var bytes = new byte[clean.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            if (!byte.TryParse(clean.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
                return "„" + clean.Substring(i * 2, 2) + "” nie jest parą hex.";
        }
        return "Zdekodowane: " + Encoding.UTF8.GetString(bytes);
    }

    // ------------------------------ 0.91 · CENTRUM: Polish identifiers ------------------------------
    /// <summary>Validates the checksum, reads the birth date and sex digit. Everything stays local.</summary>
    public static string Pesel(string digits)
    {
        if (digits.Length != 11 || !digits.All(char.IsDigit)) return "PESEL musi mieć dokładnie 11 cyfr.";
        int[] weights = [1, 3, 7, 9, 1, 3, 7, 9, 1, 3];
        int sum = 0;
        for (int i = 0; i < 10; i++) sum += (digits[i] - '0') * weights[i];
        if ((10 - sum % 10) % 10 != digits[10] - '0') return "PESEL niepoprawny: suma kontrolna się nie zgadza.";
        int yy = int.Parse(digits[..2]);
        int mm = int.Parse(digits.Substring(2, 2));
        int dd = int.Parse(digits.Substring(4, 2));
        int century;
        if (mm is >= 1 and <= 12) century = 1900;
        else if (mm is >= 21 and <= 32) { century = 2000; mm -= 20; }
        else if (mm is >= 41 and <= 52) { century = 2100; mm -= 40; }
        else if (mm is >= 61 and <= 72) { century = 2200; mm -= 60; }
        else if (mm is >= 81 and <= 92) { century = 1800; mm -= 80; }
        else return "PESEL niepoprawny: miesiąc urodzenia poza zakresem.";
        try
        {
            var birth = new DateTime(century + yy, mm, dd);
            string sex = (digits[9] - '0') % 2 == 0 ? "kobieta" : "mężczyzna";
            return "PESEL poprawny · data urodzenia: " + birth.ToString("dd.MM.yyyy", Pl) + " · płeć: " + sex + ".\nWalidacja lokalna — numer nigdzie nie został wysłany.";
        }
        catch (ArgumentOutOfRangeException) { return "PESEL niepoprawny: nieistniejąca data urodzenia."; }
    }

    public static string Nip(string digits)
    {
        if (digits.Length != 10 || !digits.All(char.IsDigit)) return "NIP musi mieć dokładnie 10 cyfr.";
        int[] weights = [6, 5, 7, 2, 3, 4, 5, 6, 7];
        int sum = 0;
        for (int i = 0; i < 9; i++) sum += (digits[i] - '0') * weights[i];
        int check = sum % 11;
        if (check == 10 || check != digits[9] - '0') return "NIP niepoprawny: suma kontrolna się nie zgadza.";
        return "NIP poprawny (" + digits + "). Walidacja lokalna — numer nigdzie nie został wysłany.";
    }

    /// <summary>ISO 13616 mod-97 validation. For PL IBANs also checks the national length (28).</summary>
    public static string Iban(string text)
    {
        string iban = text.Replace(" ", "").ToUpperInvariant();
        if (iban.Length < 8 || iban.Length > 34 || !Regex.IsMatch(iban, @"^[A-Z]{2}\d{2}[A-Z0-9]+$"))
            return "IBAN ma format: 2 litery kraju, 2 cyfry kontrolne i numer, np. „iban: PL61 1090 1014 0000 0712 1981 2874”.";
        string rearranged = iban[4..] + iban[..4];
        int remainder = 0;
        foreach (char c in rearranged)
        {
            string part = char.IsDigit(c) ? c.ToString() : ((int)c - 55).ToString(Pl);
            remainder = int.Parse(remainder.ToString(Pl) + part) % 97;
        }
        if (remainder != 1) return "IBAN niepoprawny: suma kontrolna się nie zgadza.";
        string country = iban[..2];
        if (country == "PL" && iban.Length != 28) return "IBAN ma poprawną sumę kontrolną, ale polski IBAN powinien mieć 28 znaków (ma " + iban.Length + ").";
        return "IBAN poprawny (" + country + ", długość " + iban.Length + "). Walidacja lokalna — numer nigdzie nie został wysłany.";
    }

    public static string FromRgb(string r, string g, string b)
    {
        if (!int.TryParse(r, out int red) || !int.TryParse(g, out int green) || !int.TryParse(b, out int blue)
            || red is < 0 or > 255 || green is < 0 or > 255 || blue is < 0 or > 255)
            return "Podaj trzy wartości 0–255, np. „rgb 31 162 195”.";
        string hex = "#" + red.ToString("X2") + green.ToString("X2") + blue.ToString("X2");
        return "RGB(" + red + ", " + green + ", " + blue + ") = " + hex;
    }

    // ------------------------------ 0.91 · CENTRUM: system facts (offline, read-only) ------------------------------
    public static string LocalIp()
    {
        try
        {
            var addresses = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a.Address))
                .Select(a => a.Address.ToString())
                .Distinct()
                .ToList();
            return addresses.Count == 0
                ? "Nie wykryto lokalnego adresu IPv4 (poza 127.0.0.1). Adres publiczny wymaga połączenia z siecią — nie robię tego bez polecenia."
                : "Lokalne adresy IPv4:\n" + string.Join("\n", addresses.Select(a => "· " + a)) + "\nTo adresy w sieci lokalnej — publicznego adresu nie sprawdzam bez Twojego polecenia.";
        }
        catch (Exception) { return "Nie udało się odczytać interfejsów sieciowych."; }
    }

    public static string ArchitectureInfo() =>
        "Architektura systemu: " + RuntimeInformation.OSArchitecture + " · proces aplikacji: " + RuntimeInformation.ProcessArchitecture +
        " · system: " + RuntimeInformation.OSDescription;

    // ------------------------------ 0.91 · CENTRUM: randomness ------------------------------
    public static string CoinFlip() => "Moneta: " + (RandomNumberGenerator.GetInt32(2) == 0 ? "orzeł" : "reszka");

    public static string Lotto()
    {
        var numbers = new SortedSet<int>();
        while (numbers.Count < 6) numbers.Add(RandomNumberGenerator.GetInt32(1, 50));
        return "Lotto (6 z 49): " + string.Join(", ", numbers) + " · losowanie kryptograficzne, szansa na szóstkę ok. 1 do 13 983 816.";
    }

    public static string Pin(int digits)
    {
        if (digits is < 4 or > 12) return "PIN może mieć od 4 do 12 cyfr.";
        var builder = new StringBuilder();
        for (int i = 0; i < digits; i++) builder.Append(RandomNumberGenerator.GetInt32(0, 10));
        return "PIN (" + digits + " cyfr): " + builder + " · wygenerowany lokalnie, nigdzie nie zapisany.";
    }
}
