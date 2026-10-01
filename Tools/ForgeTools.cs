using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>0.96 · KUŹNIA — the third part of the offline toolbox. Same rules as before: pure functions,
/// no network, no file access, no process starts. Each tool is reachable from chat and from the Tools page
/// (<c>Core/ToolCatalog.cs</c>, category „Kuźnia 0.96”); the routing hook is <see cref="ProcessForge"/>.</summary>
public static partial class UtilityToolbox
{
    private static string? ProcessForge(string raw, string text)
    {
        var names = Regex.Match(text, @"^nazwa zmiennej[:\s]+(.+)$");
        if (names.Success) return VariableNames(Argument(raw, "nazwa zmiennej"));
        var urlEncode = Regex.Match(text, @"^url zakoduj[:\s]+(.+)$", RegexOptions.Singleline);
        if (urlEncode.Success) return UrlEncode(Argument(raw, "url zakoduj"));
        var urlDecode = Regex.Match(text, @"^url odkoduj[:\s]+(.+)$", RegexOptions.Singleline);
        if (urlDecode.Success) return UrlDecode(Argument(raw, "url odkoduj"));
        var unixToDate = Regex.Match(text, @"^unix[:\s]+(-?\d{1,15})$");
        if (unixToDate.Success) return UnixToDate(unixToDate.Groups[1].Value);
        var dateToUnix = Regex.Match(text, @"^na unix[:\s]+(.+)$");
        if (dateToUnix.Success) return DateToUnix(Argument(raw, "na unix"));
        var frequency = Regex.Match(text, @"^czestosc slow[:\s]+(.+)$", RegexOptions.Singleline);
        if (frequency.Success) return WordFrequency(Argument(raw, "czestosc slow"));
        var loan = Regex.Match(text, @"^rata kredytu[:\s]+(\d+(?:[.,]\d+)?)\s+(\d{1,2})\s+(\d+(?:[.,]\d+)?)\s*%?$");
        if (loan.Success) return LoanPayment(loan.Groups[1].Value, loan.Groups[2].Value, loan.Groups[3].Value);
        if (text.StartsWith("rata kredytu", StringComparison.Ordinal))
            return "Format: „rata kredytu: kwota lata oprocentowanie”, np. „rata kredytu: 300000 25 7,5” (kwota w zł, okres w latach, oprocentowanie roczne w %). Liczę lokalnie, nic nie wysyłam.";
        var versions = Regex.Match(text, @"^porownaj wersje[:\s]+(.+)$", RegexOptions.Singleline);
        if (versions.Success) return CompareVersions(Argument(raw, "porownaj wersje"));
        var numbered = Regex.Match(text, @"^numeruj linie[:\s]+(.+)$", RegexOptions.Singleline);
        if (numbered.Success) return NumberLines(Argument(raw, "numeruj linie"));
        var reversedLines = Regex.Match(text, @"^odwroc linie[:\s]+(.+)$", RegexOptions.Singleline);
        if (reversedLines.Success) return ReverseLines(Argument(raw, "odwroc linie"));
        var spaces = Regex.Match(text, @"^popraw odstepy[:\s]+(.+)$", RegexOptions.Singleline);
        if (spaces.Success) return FixSpacing(Argument(raw, "popraw odstepy"));
        return null;
    }

    // ------------------------------------------------------- identifier names
    /// <summary>One phrase, five programmer conventions. Polish letters are transliterated, because
    /// identifiers should stay ASCII; camelCase input is split into words first.</summary>
    public static string VariableNames(string input)
    {
        input = (input ?? "").Trim();
        if (input.Length == 0) return "Podaj nazwę lub kilka słów, np. „nazwa zmiennej: liczba użytkowników aktywnych”.";
        if (input.Length > 200) return "Tekst jest za długi na nazwę (limit 200 znaków).";
        var words = Regex.Matches(input, @"\p{Lu}+(?!\p{Ll})|\p{Lu}?\p{Ll}+|\p{N}+")
            .Select(match => Transliterate(match.Value).ToLowerInvariant())
            .Where(word => word.Length > 0)
            .Take(30)
            .ToArray();
        if (words.Length == 0) return "Nie znalazłem w tym tekście liter ani cyfr, z których dałoby się zbudować nazwę.";
        string Capital(string word) => char.ToUpperInvariant(word[0]) + word[1..];
        string pascal = string.Concat(words.Select(Capital));
        string camel = words[0] + string.Concat(words.Skip(1).Select(Capital));
        var result = new StringBuilder();
        result.Append("Nazwy dla „").Append(Shorten(input, 80)).AppendLine("”:");
        result.Append("camelCase: ").AppendLine(camel);
        result.Append("PascalCase: ").AppendLine(pascal);
        result.Append("snake_case: ").AppendLine(string.Join('_', words));
        result.Append("kebab-case: ").AppendLine(string.Join('-', words));
        result.Append("UPPER_SNAKE_CASE: ").Append(string.Join('_', words).ToUpperInvariant());
        if (char.IsDigit(words[0][0])) result.AppendLine().Append("Uwaga: w większości języków identyfikator nie może zaczynać się od cyfry.");
        return result.ToString();
    }

    // ------------------------------------------------------------------- URL
    public static string UrlEncode(string input)
    {
        input = input ?? "";
        if (input.Trim().Length == 0) return "Podaj tekst do zakodowania, np. „url zakoduj: ala ma kota & psa”.";
        if (input.Length > 2000) return "Tekst jest za długi (limit 2000 znaków).";
        return "Zakodowane (procent-kodowanie UTF-8): " + Uri.EscapeDataString(input.Trim());
    }

    public static string UrlDecode(string input)
    {
        input = input ?? "";
        if (input.Trim().Length == 0) return "Podaj zakodowany tekst, np. „url odkoduj: ala%20ma%20kota”.";
        if (input.Length > 4000) return "Tekst jest za długi (limit 4000 znaków).";
        return "Odkodowane: " + Uri.UnescapeDataString(input.Trim());
    }

    // ------------------------------------------------------------- unix time
    /// <summary>Unix seconds → date. A value above 99 999 999 999 is read as milliseconds (and says so).</summary>
    public static string UnixToDate(string digits)
    {
        if (!long.TryParse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value))
            return "Nie rozpoznałem liczby sekund. Podaj liczbę całkowitą, np. „unix: 1700000000”.";
        bool milliseconds = Math.Abs(value) > 99_999_999_999L;
        try
        {
            DateTimeOffset moment = milliseconds ? DateTimeOffset.FromUnixTimeMilliseconds(value) : DateTimeOffset.FromUnixTimeSeconds(value);
            DateTimeOffset local = moment.ToLocalTime();
            return "Unix " + value.ToString(CultureInfo.InvariantCulture) + (milliseconds ? " (milisekundy)" : " (sekundy)") +
                " → UTC " + moment.UtcDateTime.ToString("dd.MM.yyyy HH:mm:ss", Pl) +
                " · czas lokalny " + local.ToString("dd.MM.yyyy HH:mm:ss", Pl) + " (strefa tego komputera)";
        }
        catch (ArgumentOutOfRangeException) { return "Ta liczba jest poza zakresem dat (lata 1–9999)."; }
    }

    private static readonly string[] UnixDateFormats =
    [
        "d.M.yyyy H:mm:ss", "d.M.yyyy H:mm", "d.M.yyyy", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd"
    ];

    /// <summary>Date → Unix seconds. Without „utc” at the end the date is read in this computer's time zone.</summary>
    public static string DateToUnix(string input)
    {
        string value = (input ?? "").Trim();
        bool utc = value.EndsWith("utc", StringComparison.OrdinalIgnoreCase);
        if (utc) value = value[..^3].Trim();
        if (!DateTime.TryParseExact(value, UnixDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
            return "Nie rozpoznałem daty. Napisz np. „na unix: 14.11.2023 22:13:20 utc” albo „na unix: 2023-11-14 22:13”. Bez słowa „utc” biorę czas tego komputera.";
        try
        {
            var moment = utc
                ? new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Utc))
                : new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Local));
            return "Unix: " + moment.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) + " (sekundy) · " +
                parsed.ToString("dd.MM.yyyy HH:mm:ss", Pl) + (utc ? " UTC" : " czasu lokalnego");
        }
        catch (ArgumentOutOfRangeException) { return "Ta data jest poza zakresem."; }
    }

    // --------------------------------------------------------- word frequency
    public static string WordFrequency(string input)
    {
        input = input ?? "";
        if (input.Trim().Length == 0) return "Podaj tekst, np. „czestosc slow: ala ma kota ala ma psa”.";
        if (input.Length > 50000) return "Tekst jest za długi (limit 50 000 znaków).";
        string[] words = Regex.Matches(input.ToLower(Pl), @"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*")
            .Select(match => match.Value).ToArray();
        if (words.Length == 0) return "Nie znalazłem w tekście żadnych słów.";
        var top = words.GroupBy(word => word, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal)
            .Take(10).ToArray();
        var result = new StringBuilder();
        int distinct = words.Distinct(StringComparer.Ordinal).Count();
        result.Append("Najczęstsze słowa (słów: ").Append(words.Length).Append(", różnych: ").Append(distinct).AppendLine("):");
        for (int i = 0; i < top.Length; i++)
            result.Append(i + 1).Append(". ").Append(top[i].Key).Append(" — ").Append(top[i].Count()).Append('×').AppendLine();
        return result.ToString().TrimEnd();
    }

    // ------------------------------------------------------------------ loan
    /// <summary>Equal instalments (annuity). Informational only: fees, insurance and a variable rate are not included.</summary>
    public static string LoanPayment(string amountText, string yearsText, string rateText)
    {
        if (!TryNumber(amountText, out double principal) || !TryNumber(rateText, out double rate) || !int.TryParse(yearsText, NumberStyles.None, CultureInfo.InvariantCulture, out int years))
            return NumberError;
        if (principal <= 0 || principal > 1_000_000_000d) return "Kwota musi być dodatnia i nie większa niż miliard złotych.";
        if (years is < 1 or > 50) return "Okres musi mieć od 1 do 50 lat.";
        if (rate < 0 || rate > 100) return "Oprocentowanie roczne musi mieścić się w przedziale 0–100%.";
        int months = years * 12;
        double monthlyRate = rate / 100d / 12d;
        double payment = monthlyRate == 0 ? principal / months : principal * monthlyRate / (1d - Math.Pow(1d + monthlyRate, -months));
        double total = payment * months;
        return "Kredyt " + principal.ToString("N2", Pl) + " zł na " + years + " lat, oprocentowanie " + rate.ToString("0.##", Pl) + "% w skali roku:\n" +
            "Rata miesięczna: " + payment.ToString("N2", Pl) + " zł (rat: " + months + ")\n" +
            "Do spłaty razem: " + total.ToString("N2", Pl) + " zł · odsetki: " + (total - principal).ToString("N2", Pl) + " zł\n" +
            "To rachunek orientacyjny: raty równe, stałe oprocentowanie, bez prowizji, ubezpieczeń i zmian stóp. Liczę lokalnie.";
    }

    // -------------------------------------------------------------- versions
    public static string CompareVersions(string input)
    {
        string[] halves = (input ?? "").Split("|||", 2, StringSplitOptions.None);
        if (halves.Length != 2) return "Podaj dwie wersje oddzielone „|||”, np. „porownaj wersje: 1.2.10 ||| 1.10.0”.";
        string left = halves[0].Trim(), right = halves[1].Trim();
        if (!TryVersion(left, out Version? a) || !TryVersion(right, out Version? b) || a == null || b == null)
            return "Nie rozpoznałem wersji. Użyj liczb rozdzielonych kropkami (do czterech), np. 1.2.10 albo v2.0.1.";
        int order = a.CompareTo(b);
        string verdict = order == 0 ? "to ta sama wersja."
            : order < 0 ? "nowsza jest druga (" + right + ")."
            : "nowsza jest pierwsza (" + left + ").";
        string sign = order == 0 ? "=" : order < 0 ? "<" : ">";
        return left + " " + sign + " " + right + " — " + verdict + " Porównuję liczby składowe, nie tekst (1.10 jest nowsze niż 1.9).";
    }

    private static bool TryVersion(string text, out Version? version)
    {
        version = null;
        string value = text.Trim().TrimStart('v', 'V');
        if (value.Length == 0 || value.Length > 40 || !Regex.IsMatch(value, @"^\d+(\.\d+){0,3}$")) return false;
        if (!value.Contains('.')) value += ".0";
        if (!Version.TryParse(value, out Version? parsed)) return false;
        // Version treats a missing component as −1, which would make 1.2 older than 1.2.0.
        version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build), Math.Max(0, parsed.Revision));
        return true;
    }

    // ------------------------------------------------------------ line tools
    public static string NumberLines(string input)
    {
        string[] lines = Lines(input ?? "");
        if (lines.Length == 0) return "Podaj wiersze — osobno albo rozdzielone „ | ”, np. „numeruj linie: kot | pies | ryba”.";
        if (lines.Length > 2000) return "Za dużo wierszy (" + lines.Length + ") — limit to 2000. Podziel tekst na części.";
        var result = new StringBuilder("Ponumerowane wiersze (" + lines.Length + "):").AppendLine();
        for (int i = 0; i < lines.Length; i++) result.Append(i + 1).Append(". ").AppendLine(lines[i].Trim());
        return result.ToString().TrimEnd();
    }

    public static string ReverseLines(string input)
    {
        string[] lines = Lines(input ?? "");
        if (lines.Length == 0) return "Podaj wiersze — osobno albo rozdzielone „ | ”, np. „odwroc linie: pierwszy | drugi | trzeci”.";
        if (lines.Length > 2000) return "Za dużo wierszy (" + lines.Length + ") — limit to 2000. Podziel tekst na części.";
        var result = new StringBuilder("Odwrócona kolejność wierszy (" + lines.Length + "):").AppendLine();
        for (int i = lines.Length - 1; i >= 0; i--) result.AppendLine(lines[i].Trim());
        return result.ToString().TrimEnd();
    }

    /// <summary>Collapses runs of spaces and tabs into one and trims the ends; says how much was removed.</summary>
    public static string FixSpacing(string input)
    {
        string text = (input ?? "").Trim();
        if (text.Length == 0) return "Podaj tekst, np. „popraw odstepy: ala   ma    kota”.";
        if (text.Length > 20000) return "Tekst jest za długi (limit 20 000 znaków).";
        string fixedText = Regex.Replace(text, @"[ \t\u00a0]{2,}", " ");
        int removed = text.Length - fixedText.Length;
        return removed == 0
            ? "Odstępy są w porządku — nic nie trzeba poprawiać (znaków: " + text.Length + ")."
            : "Poprawione: " + fixedText + "\nUsunięte nadmiarowe odstępy: " + removed + " (z " + text.Length + " do " + fixedText.Length + " znaków).";
    }
}
