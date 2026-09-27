using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>
/// SEKCJA 13 i 16 — narzędzia liczbowe, finansowe i tekstowe, które nie potrzebują sieci ani modelu:
/// kwota słownie po polsku, lorem, statystyki tekstu, ROI, próg rentowności, amortyzacja, inflacja,
/// plan oszczędzania, budżet 50/30/20, czas do celu, macierz Eisenhowera i generator slajdów.
/// <para>Wszystko są to czyste funkcje: liczby są liczone z podanych wartości, a nie „na oko”.
/// Tam, gdzie wynik zależy od założeń (np. inflacja), założenie jest wypisane w odpowiedzi.</para>
/// </summary>
public static class ProductivityToolbox
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public static string? TryHandle(string command, string text)
    {
        string raw = command ?? "";

        var words = Regex.Match(text, @"^(?:liczba slownie|slownie|kwota slownie)[:\s]+(-?\d+[ .,]?\d*(?:[.,]\d{1,2})?)$");
        if (words.Success) return NumberToPolishWords(Payload(raw, "liczba slownie", "kwota slownie", "slownie"));

        var lorem = Regex.Match(text, @"^(?:lorem|tekst zastepczy|placeholder)[:\s]*(\d{1,4})?$");
        if (lorem.Success)
        {
            int count = lorem.Groups[1].Success && int.TryParse(lorem.Groups[1].Value, out int parsed) ? parsed : 40;
            return Lorem(count);
        }

        var stats = Regex.Match(text, @"^(?:statystyki tekstu|analiza tekstu|statystyki)[:\s]+(.+)$", RegexOptions.Singleline);
        if (stats.Success) return TextStatistics(Payload(raw, "statystyki tekstu", "analiza tekstu", "statystyki"));

        var roi = Regex.Match(text, @"^roi[:\s]+(-?\d+[.,]?\d*)\s+(-?\d+[.,]?\d*)$");
        if (roi.Success) return Roi(roi.Groups[1].Value, roi.Groups[2].Value);

        var breakEven = Regex.Match(text, @"^(?:break even|prog rentownosci)[:\s]+(\d+[.,]?\d*)\s+(\d+[.,]?\d*)\s+(\d+[.,]?\d*)$");
        if (breakEven.Success) return BreakEven(breakEven.Groups[1].Value, breakEven.Groups[2].Value, breakEven.Groups[3].Value);

        var depreciation = Regex.Match(text, @"^amortyzacja[:\s]+(\d+[.,]?\d*)\s+(\d{1,3})(?:\s+(\d+[.,]?\d*))?$");
        if (depreciation.Success) return Depreciation(depreciation.Groups[1].Value, depreciation.Groups[2].Value, depreciation.Groups[3].Success ? depreciation.Groups[3].Value : null);

        var inflation = Regex.Match(text, @"^inflacja[:\s]+(\d+[.,]?\d*)\s+(\d+[.,]?\d*)\s+(\d{1,3})$");
        if (inflation.Success) return Inflation(inflation.Groups[1].Value, inflation.Groups[2].Value, inflation.Groups[3].Value);

        var savings = Regex.Match(text, @"^(?:oszczednosci|plan oszczedzania|oszczedzanie)[:\s]+(\d+[.,]?\d*)\s+(\d+[.,]?\d*)\s+(\d{1,3})(?:\s+(\d+[.,]?\d*))?$");
        if (savings.Success) return Savings(savings.Groups[1].Value, savings.Groups[2].Value, savings.Groups[3].Value, savings.Groups[4].Success ? savings.Groups[4].Value : null);

        var goal = Regex.Match(text, @"^cel[:\s]+(\d+[.,]?\d*)\s+(\d+[.,]?\d*)$");
        if (goal.Success) return Goal(goal.Groups[1].Value, goal.Groups[2].Value);

        var budget = Regex.Match(text, @"^(?:budzet 50 30 20|budzet|podziel budzet)[:\s]+(\d+[.,]?\d*)$");
        if (budget.Success) return Budget5030_20(budget.Groups[1].Value);

        var matrix = Regex.Match(text, @"^macierz[:\s]+(.+)$");
        if (matrix.Success) return Eisenhower(Payload(raw, "macierz"));

        var slides = Regex.Match(text, @"^(?:slajdy|prezentacja|slajdy markdown)[:\s]+(.+)$", RegexOptions.Singleline);
        if (slides.Success) return Slides(Payload(raw, "slajdy markdown", "prezentacja", "slajdy"));

        return null;
    }

    private static string Payload(string raw, params string[] prefixes)
    {
        string text = (raw ?? "").Trim();
        foreach (string prefix in prefixes)
        {
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            string rest = text[prefix.Length..].TrimStart();
            if (rest.StartsWith(':')) rest = rest[1..];
            return rest.Trim();
        }
        return text;
    }

    private static bool TryNumber(string text, out double value)
    {
        value = 0;
        string clean = (text ?? "").Replace(" ", "").Replace("_", "");
        if (clean.Count(x => x is ',' or '.') > 1)
        {
            // separator tysięcy: 1.234.567 → usuń kropki
            clean = clean.Replace(".", "").Replace(",", ".");
        }
        else clean = clean.Replace(',', '.');
        return double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string Money(double value) => value.ToString("N2", Pl) + " zł";

    // ============================================================
    // Kwota słownie
    // ============================================================

    private static readonly string[] Units = ["", "jeden", "dwa", "trzy", "cztery", "pięć", "sześć", "siedem", "osiem", "dziewięć"];
    private static readonly string[] Teens = ["dziesięć", "jedenaście", "dwanaście", "trzynaście", "czternaście", "piętnaście", "szesnaście", "siedemnaście", "osiemnaście", "dziewiętnaście"];
    private static readonly string[] Tens = ["", "dziesięć", "dwadzieścia", "trzydzieści", "czterdzieści", "pięćdziesiąt", "sześćdziesiąt", "siedemdziesiąt", "osiemdziesiąt", "dziewięćdziesiąt"];
    private static readonly string[] Hundreds = ["", "sto", "dwieście", "trzysta", "czterysta", "pięćset", "sześćset", "siedemset", "osiemset", "dziewięćset"];
    private static readonly (string Singular, string Few, string Many)[] Scales =
    [
        ("", "", ""),
        ("tysiąc", "tysiące", "tysięcy"),
        ("milion", "miliony", "milionów"),
        ("miliard", "miliardy", "miliardów"),
        ("bilion", "biliony", "bilionów"),
        ("biliard", "biliardy", "biliardów")
    ];

    /// <summary>Zapis słowny liczby po polsku (z poprawną odmianą tysięcy, milionów itd.).</summary>
    public static string NumberToPolishWords(string text)
    {
        if (!TryNumber(text, out double value)) return "Podaj liczbę, np. „liczba slownie: 1234,56”.";
        if (Math.Abs(value) >= 1e18) return "Liczba jest za duża — obsługuję wartości do biliarda (10^15).";
        bool negative = value < 0;
        value = Math.Abs(value);
        long integer = (long)Math.Floor(value);
        int decimals = (int)Math.Round((value - integer) * 100, MidpointRounding.AwayFromZero);
        if (decimals == 100) { integer++; decimals = 0; }

        string words = integer == 0 ? "zero" : IntegerWords(integer);
        if (negative) words = "minus " + words;
        string result = words;
        if (decimals > 0)
            result += " i " + decimals + "/100 (" + IntegerWords(decimals) + " setnych)";
        return "Słownie: " + result + Environment.NewLine +
            "· zapis: " + value.ToString("N2", Pl) + (negative ? " (wartość ujemna)" : "") +
            Environment.NewLine + "· groszy: " + decimals;
    }

    private static string IntegerWords(long value)
    {
        if (value == 0) return "zero";
        var groups = new List<int>();
        while (value > 0)
        {
            groups.Add((int)(value % 1000));
            value /= 1000;
        }
        var parts = new List<string>();
        for (int index = groups.Count - 1; index >= 0; index--)
        {
            int group = groups[index];
            if (group == 0) continue;
            string scale = scalesFor(index, group);
            string body = GroupWords(group);
            if (index == 1 && group == 1) parts.Add("tysiąc");
            else if (index > 0 && group == 1) parts.Add(Scales[index].Singular);
            else parts.Add((body + " " + scale).Trim());
        }
        return string.Join(" ", parts.Where(x => x.Length > 0));
    }

    private static string scalesFor(int index, int group)
    {
        if (index == 0 || index >= Scales.Length) return "";
        bool few = (group % 10 is >= 2 and <= 4) && (group % 100 is < 12 or > 14);
        if (group == 1) return Scales[index].Singular;
        return few ? Scales[index].Few : Scales[index].Many;
    }

    private static string GroupWords(int group)
    {
        var parts = new List<string>();
        int hundreds = group / 100;
        int rest = group % 100;
        if (hundreds > 0) parts.Add(Hundreds[hundreds]);
        if (rest is >= 10 and < 20) parts.Add(Teens[rest - 10]);
        else
        {
            int tens = rest / 10;
            int units = rest % 10;
            if (tens > 0) parts.Add(Tens[tens]);
            if (units > 0) parts.Add(Units[units]);
        }
        return string.Join(" ", parts);
    }

    // ============================================================
    // Tekst
    // ============================================================

    private static readonly string[] LoremWords =
    [
        "lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing", "elit", "sed", "do",
        "eiusmod", "tempor", "incididunt", "ut", "labore", "et", "dolore", "magna", "aliqua", "enim",
        "ad", "minim", "veniam", "quis", "nostrud", "exercitation", "ullamco", "laboris", "nisi", "aliquip"
    ];

    public static string Lorem(int words)
    {
        int count = Math.Clamp(words, 1, 1000);
        var random = new Random(20260927);
        var builder = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            if (i > 0) builder.Append(' ');
            builder.Append(LoremWords[random.Next(LoremWords.Length)]);
            if (i == 0) builder[0] = char.ToUpperInvariant(builder[0]);
            if ((i + 1) % 12 == 0 && i + 1 < count) builder.Append(". ");
        }
        return builder.ToString().TrimEnd() + "." + Environment.NewLine +
            "(" + count + " słów; tekst zastępczy wygenerowany lokalnie, deterministycznie — te same słowa przy tym samym żądaniu)";
    }

    public static string TextStatistics(string text)
    {
        string content = text ?? "";
        if (content.Trim().Length == 0) return "Podaj tekst po dwukropku.";
        int characters = content.Length;
        int charactersNoSpaces = content.Count(x => !char.IsWhiteSpace(x));
        var wordMatches = Regex.Matches(content.ToLowerInvariant(), @"[\p{L}\p{Nd}][\p{L}\p{Nd}'-]*").Cast<System.Text.RegularExpressions.Match>().Select(x => x.Value).ToArray();
        int sentences = Regex.Matches(content, @"[.!?]+(?:\s|$)").Count;
        int paragraphs = content.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Count(x => x.Trim().Length > 0);
        var unique = wordMatches.Distinct(StringComparer.Ordinal).Count();
        double averageWordLength = wordMatches.Length == 0 ? 0 : wordMatches.Average(x => x.Length);
        string longest = wordMatches.OrderByDescending(x => x.Length).FirstOrDefault() ?? "—";
        var top = wordMatches.GroupBy(x => x, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Take(5).Select(g => g.Key + " (" + g.Count() + "×)");
        double readingMinutes = wordMatches.Length / 200.0;
        double speakingMinutes = wordMatches.Length / 140.0;

        return "Statystyki tekstu:" + Environment.NewLine +
            "· znaków: " + characters.ToString("N0", Pl) + " (bez spacji: " + charactersNoSpaces.ToString("N0", Pl) + ")" + Environment.NewLine +
            "· słów: " + wordMatches.Length.ToString("N0", Pl) + " · unikalnych: " + unique.ToString("N0", Pl) + Environment.NewLine +
            "· zdań: " + sentences + " · akapitów: " + paragraphs + Environment.NewLine +
            "· średnia długość słowa: " + averageWordLength.ToString("0.#", Pl) + " znaku" + Environment.NewLine +
            "· najdłuższe słowo: „" + longest + "”" + Environment.NewLine +
            "· najczęstsze: " + (top.Any() ? string.Join(", ", top) : "brak") + Environment.NewLine +
            "· czytanie ~" + readingMinutes.ToString("0.#", Pl) + " min (200 słów/min) · mówienie ~" + speakingMinutes.ToString("0.#", Pl) + " min (140 słów/min)";
    }

    public static string Slides(string markdown)
    {
        var lines = (markdown ?? "").Replace("\r\n", "\n").Split('\n');
        var slides = new List<List<string>> { new List<string>() };
        bool inCode = false;
        foreach (string line in lines)
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) inCode = !inCode;
            bool isHeading = !inCode && Regex.IsMatch(line, @"^#{1,3}\s");
            bool isSeparator = !inCode && line.Trim() is "---" or "***";
            if ((isHeading || isSeparator) && slides.Count > 0 && slides[^1].Count > 0) slides.Add(new List<string>());
            if (line.Trim().Length > 0 && !isSeparator) slides[^1].Add(line);
        }
        slides = slides.Where(x => x.Count > 0).ToList();
        if (slides.Count == 0) return "Nie znalazłem treści. Podaj Markdown z nagłówkami, np. „slajdy: # Tytuł\\n## Punkt”.";
        var output = new StringBuilder();
        for (int i = 0; i < slides.Count; i++)
        {
            if (i > 0) output.AppendLine().AppendLine("---").AppendLine();
            foreach (string line in slides[i]) output.AppendLine(line);
        }
        return "Szkielet prezentacji (Markdown zgodny z Marp/Reveal.js — separatory „---”):" + Environment.NewLine +
            "· slajdów: " + slides.Count + Environment.NewLine + Environment.NewLine + output.ToString().TrimEnd();
    }

    // ============================================================
    // Finanse
    // ============================================================

    public static string Roi(string profit, string cost)
    {
        if (!TryNumber(profit, out double gain) || !TryNumber(cost, out double investment)) return "Podaj dwie liczby: zysk i koszt, np. „roi: 2000 15000”.";
        if (investment == 0) return "Koszt nie może być zerem — ROI to stosunek zysku do nakładu.";
        double roi = gain / investment * 100;
        string verdict = roi switch
        {
            > 100 => "bardzo wysoki zwrot (ponad dwukrotność nakładu)",
            > 20 => "dobry zwrot",
            > 0 => "zwrot dodatni, ale skromny",
            _ => "strata — nakład nie został odzyskany"
        };
        return "ROI: " + roi.ToString("0.##", Pl) + "% · " + verdict + Environment.NewLine +
            "· zysk: " + Money(gain) + " · koszt: " + Money(investment) + Environment.NewLine +
            "· ROI liczone jako zysk/koszt × 100% (bez dyskontowania w czasie).";
    }

    public static string BreakEven(string price, string variableCost, string fixedCosts)
    {
        if (!TryNumber(price, out double unitPrice) || !TryNumber(variableCost, out double unitVariable) || !TryNumber(fixedCosts, out double fixedTotal))
            return "Użyj: „break even: cena koszt_zmienny koszty_stałe”, np. „break even: 100 40 12000”.";
        double margin = unitPrice - unitVariable;
        if (margin <= 0) return "Marża na sztuce wynosi " + Money(margin) + " — przy takiej cenie próg rentowności nie istnieje, każda sprzedaż powiększa stratę.";
        double units = fixedTotal / margin;
        return "Próg rentowności: " + Math.Ceiling(units).ToString("N0", Pl) + " sztuk" + Environment.NewLine +
            "· marża na sztuce: " + Money(margin) + " · koszty stałe: " + Money(fixedTotal) + Environment.NewLine +
            "· przychód w punkcie progu: " + Money(Math.Ceiling(units) * unitPrice) + Environment.NewLine +
            "· dokładna liczba sztuk: " + units.ToString("0.##", Pl) + " (zaokrąglam w górę — nie sprzedasz 0,4 sztuki)";
    }

    public static string Depreciation(string value, string years, string? rate)
    {
        if (!TryNumber(value, out double initial)) return "Podaj wartość i liczbę lat, np. „amortyzacja: 12000 4”.";
        if (!int.TryParse(years, out int life) || life is < 1 or > 50) return "Liczba lat musi być z zakresu 1–50.";
        double salvage = 0;
        if (rate is not null && TryNumber(rate, out double percent)) salvage = initial * Math.Clamp(percent, 0, 100) / 100;
        double annual = (initial - salvage) / life;
        var lines = new List<string> { "Amortyzacja liniowa przez " + life + " lat:" };
        double remaining = initial;
        for (int year = 1; year <= Math.Min(life, 10); year++)
        {
            remaining -= annual;
            lines.Add("· rok " + year + ": odpisu " + Money(annual) + " · wartość netto " + Money(Math.Max(remaining, salvage)));
        }
        if (life > 10) lines.Add("… (kolejne lata podobnie, roczna stawka bez zmian)");
        lines.Add("· odpis roczny: " + Money(annual) + " · miesięczny: " + Money(annual / 12) + (salvage > 0 ? " · wartość końcowa: " + Money(salvage) : ""));
        return string.Join(Environment.NewLine, lines);
    }

    public static string Inflation(string amount, string rate, string years)
    {
        if (!TryNumber(amount, out double value) || !TryNumber(rate, out double percent)) return "Użyj: „inflacja: kwota procent lat”, np. „inflacja: 1000 5 10”.";
        if (!int.TryParse(years, out int span) || span is < 1 or > 80) return "Liczba lat musi być z zakresu 1–80.";
        double real = value / Math.Pow(1 + percent / 100, span);
        double needed = value * Math.Pow(1 + percent / 100, span);
        return "Założenie: stała inflacja " + percent.ToString("0.##", Pl) + "% rocznie (to model, nie prognoza):" + Environment.NewLine +
            "· siła nabywcza " + Money(value) + " po " + span + " latach: " + Money(real) + Environment.NewLine +
            "· żeby zachować tę siłę nabywczą, potrzebujesz: " + Money(needed) + Environment.NewLine +
            "· różnica: " + Money(needed - value) + " (utrata wartości pieniądza: " + (100 - real / value * 100).ToString("0.#", Pl) + "%)";
    }

    public static string Savings(string monthly, string rate, string years, string? initial)
    {
        if (!TryNumber(monthly, out double contribution) || !TryNumber(rate, out double annualPercent)) return "Użyj: „oszczednosci: miesięcznie procent lat [start]”, np. „oszczednosci: 500 6 10 1000”.";
        if (!int.TryParse(years, out int span) || span is < 1 or > 60) return "Liczba lat musi być z zakresu 1–60.";
        double start = 0;
        if (initial is not null && TryNumber(initial, out double parsedStart)) start = parsedStart;
        double monthlyRate = annualPercent / 100 / 12;
        double balance = start;
        double contributed = start;
        for (int month = 0; month < span * 12; month++)
        {
            balance = balance * (1 + monthlyRate) + contribution;
            contributed += contribution;
        }
        double interest = balance - contributed;
        return "Plan oszczędzania (oprocentowanie " + annualPercent.ToString("0.##", Pl) + "% rocznie ze składaniem miesięcznym):" + Environment.NewLine +
            "· wpłacone razem: " + Money(contributed) + " (z kwotą startową " + Money(start) + ")" + Environment.NewLine +
            "· odsetki: " + Money(interest) + Environment.NewLine +
            "· saldo po " + span + " latach: " + Money(balance) + Environment.NewLine +
            "· To model matematyczny: nie uwzględnia podatku od zysków, inflacji ani zmian oprocentowania.";
    }

    public static string Goal(string target, string monthly)
    {
        if (!TryNumber(target, out double goal) || !TryNumber(monthly, out double contribution)) return "Użyj: „cel: kwota miesięcznie”, np. „cel: 20000 800”.";
        if (goal <= 0) return "Cel musi być większy od zera.";
        if (contribution <= 0) return "Kwota miesięczna musi być większa od zera — inaczej cel nigdy nie zostanie osiągnięty.";
        double months = goal / contribution;
        int wholeMonths = (int)Math.Ceiling(months);
        var date = DateTime.Today.AddMonths(wholeMonths);
        return "Cel " + Money(goal) + " przy odkładaniu " + Money(contribution) + " miesięcznie:" + Environment.NewLine +
            "· potrzebne miesiące: " + wholeMonths + " (" + (wholeMonths / 12) + " lat i " + (wholeMonths % 12) + " miesięcy)" + Environment.NewLine +
            "· przy tej samej wpłacie ostatnia rata: " + Money(goal - (wholeMonths - 1) * contribution) + Environment.NewLine +
            "· bez odsetek i bez inflacji — to plan wpłat, nie prognoza zysku." + Environment.NewLine +
            "· orientacyjny termin: " + date.ToString("MMMM yyyy", Pl);
    }

    public static string Budget5030_20(string amount)
    {
        if (!TryNumber(amount, out double income)) return "Podaj kwotę miesięczną, np. „budzet: 6000”.";
        if (income <= 0) return "Kwota musi być większa od zera.";
        double needs = income * 0.5, wants = income * 0.3, savings = income * 0.2;
        return "Budżet 50/30/20 dla " + Money(income) + ":" + Environment.NewLine +
            "· potrzeby (50%): " + Money(needs) + " — mieszkanie, jedzenie, rachunki, transport" + Environment.NewLine +
            "· zachcianki (30%): " + Money(wants) + " — rozrywka, restauracje, hobby" + Environment.NewLine +
            "· oszczędności i długi (20%): " + Money(savings) + Environment.NewLine +
            "· to ogólna reguła, nie Twoja sytuacja: jeśli potrzeby przekraczają 50%, najpierw tam szukaj różnicy.";
    }

    public static string Eisenhower(string payload)
    {
        string text = (payload ?? "").Trim();
        if (text.Length == 0) return "Użyj: „macierz: wazne pilne zadanie”, np. „macierz: wazne pilne oddac raport”.";
        var words = Regex.Split(text.ToLowerInvariant(), @"\s+").Where(x => x.Length > 0).ToArray();
        bool important = words.Contains("wazne") || words.Contains("ważne") || words.Contains("istotne");
        bool urgent = words.Contains("pilne");
        string task = string.Join(" ", words.Where(x => x is not ("wazne" or "ważne" or "istotne" or "pilne" or "i" or "oraz")));
        if (task.Length == 0) task = "(bez treści)";
        string quadrant = (important, urgent) switch
        {
            (true, true) => "1 — zrób teraz (ważne i pilne)",
            (true, false) => "2 — zaplanuj termin (ważne, niepilne)",
            (false, true) => "3 — deleguj albo skróć (pilne, nieważne)",
            _ => "4 — odłóż albo usuń (nieważne i niepilne)"
        };
        var notes = new List<string>();
        if (!important && !urgent) notes.Add("Nie napisałeś „ważne” ani „pilne” — traktuję zadanie jako ćwiartkę 4. Dopisz słowo, żeby przesunąć je wyżej.");
        return "Zadanie: " + task + Environment.NewLine + "· ćwiartka: " + quadrant +
            (notes.Count > 0 ? Environment.NewLine + "· " + string.Join(Environment.NewLine + "· ", notes) : "") +
            Environment.NewLine + "· Klasyfikacja wynika wyłącznie z Twoich słów — niczego nie zgaduję z treści zadania.";
    }
}
