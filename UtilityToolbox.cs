using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>Offline, deterministic helper tools. Everything here is a pure function: no network,
/// no file access, no process starts — so each one is testable without a machine.</summary>
public static class UtilityToolbox
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

    public static string ConvertUnit(double value, string from, string to)
    {
        from = from.Replace("°", "").Trim().ToLowerInvariant();
        to = to.Replace("°", "").Trim().ToLowerInvariant();
        if (from is "c" or "celsius") return to switch
        {
            "f" or "fahrenheit" => Format(value * 9d / 5d + 32d) + " °F",
            "k" or "kelvin" => Format(value + 273.15d) + " K",
            _ => UnknownUnit(to)
        };
        if (from is "f" or "fahrenheit") return to switch
        {
            "c" or "celsius" => Format((value - 32d) * 5d / 9d) + " °C",
            "k" or "kelvin" => Format((value - 32d) * 5d / 9d + 273.15d) + " K",
            _ => UnknownUnit(to)
        };
        if (from is "k" or "kelvin") return to switch
        {
            "c" or "celsius" => Format(value - 273.15d) + " °C",
            "f" or "fahrenheit" => Format((value - 273.15d) * 9d / 5d + 32d) + " °F",
            _ => UnknownUnit(to)
        };
        if (!Factors.TryGetValue(from, out double fromFactor)) return UnknownUnit(from);
        if (!Factors.TryGetValue(to, out double toFactor)) return UnknownUnit(to);
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
            (saturation * 100).ToString("0") + "%, " + (lightness * 100).ToString("0") + "%) · luminancja " + luminance.ToString("0.000") +
            " · kontrast z bielą " + Contrast(luminance, 1).ToString("0.0") + ":1, z czernią " + Contrast(luminance, 0).ToString("0.0") + ":1";
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
}
