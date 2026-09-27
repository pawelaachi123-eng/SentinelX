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

        // ————— 0.97 · DOŁĄCZONE: §7 język, §11 reguły automatyzacji, §13 sesje/spotkania/czas pracy —————
        string en = ExtraFlat(text);
        if (ExtraIs(en, "jezyk")) return LanguageDetect(Payload(raw, "jezyk"));
        if (ExtraIs(en, "plan tygodnia")) return WeekPlan(Payload(raw, "plan tygodnia"));
        if (ExtraIs(en, "i18n")) return I18nGaps(Payload(raw, "i18n"));
        if (ExtraIs(en, "webhook szablon")) return WebhookTemplate(Payload(raw, "webhook szablon"));
        if (ExtraIs(en, "token bucket")) return TokenBucket(Payload(raw, "token bucket"));
        if (ExtraIs(en, "retry plan")) return RetryPlan(Payload(raw, "retry plan"));
        if (ExtraIs(en, "sesje")) return Sessions(Payload(raw, "sesje"));
        if (ExtraIs(en, "koszt spotkania")) return MeetingCost(Payload(raw, "koszt spotkania"));
        if (ExtraIs(en, "godziny pracy")) return WorkHours(Payload(raw, "godziny pracy"));

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

    public static string Depreciation(string value, string years, string? rate = null)
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

    public static string Savings(string monthly, string rate, string years, string? initial = null)
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

    // ————— 0.97 · SEKCJE 7, 11 i 13 (dołączone): język, reguły automatyzacji, sesje i spotkania —————

    private static string LanguageDetect(string input)
    {
        var languages = new (string Name, string[] Words)[]
        {
            ("polski", new[] { "jest", "sie", "ktory", "ktora", "jak", "ze", "byc", "nie", "cie", "mnie", "tym", "tego", "wiele", "bardzo", "moze", "dobra", "ale", "przez", "kiedy", "wszystko" }),
            ("angielski", new[] { "the", "and", "is", "you", "that", "with", "have", "this", "from", "they", "will", "would", "there", "their", "about", "which", "been" }),
            ("niemiecki", new[] { "der", "die", "das", "und", "ist", "nicht", "ein", "eine", "mit", "auf", "fur", "sich", "auch", "aber", "über", "durch" }),
            ("hiszpański", new[] { "el", "los", "las", "una", "pero", "por", "para", "con", "como", "esta", "son", "muy", "tambien", "porque" }),
            ("francuski", new[] { "les", "des", "une", "est", "pour", "dans", "avec", "sur", "etre", "cette", "plus", "tout", "mais", "nous" }),
            ("włoski", new[] { "che", "non", "con", "per", "una", "sono", "come", "questo", "quella", "anche", "piu", "della" }),
            ("ukraiński", new[] { "що", "це", "для", "або", "його", "її", "тому", "дуже", "тільки", "коли", "також" }),
        };
        var tokens = Regex.Matches((input ?? "").ToLowerInvariant(), "[a-ząćęłńóśźżіїё]{2,}").Select(m => m.Value).ToList();
        if (tokens.Count < 3)
            return "Użycie: „jezyk: <fragment tekstu>”. Rozpoznaję po słowach funkcyjnych: polski, angielski, niemiecki, hiszpański, francuski, włoski, ukraiński.";
        var scores = languages.ToDictionary(x => x.Name, x => tokens.Count(t => x.Words.Contains(t)), StringComparer.Ordinal);
        var best = scores.OrderByDescending(x => x.Value).First();
        if (best.Value == 0)
            return "Nie rozpoznaję języka (za mało słów funkcyjnych znanych mi rodzin: polski, angielski, niemiecki, hiszpański, francuski, włoski, ukraiński). Nie zgaduję.";
        return "JEZYK: " + best.Key + " (trafienia słów funkcyjnych: " + best.Value + " z " + tokens.Count + " słów)" + Environment.NewLine +
            "· pozostałe: " + string.Join(", ", scores.Where(x => x.Key != best.Key).OrderByDescending(x => x.Value).Take(3).Select(x => x.Key + " " + x.Value)) + Environment.NewLine +
            "· rozpoznanie po stopwordach — krótkie fragmenty bez słów funkcyjnych mogą być nierozpoznawalne";
    }

    private static string I18nGaps(string input)
    {
        string[] parts = (input ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return "Użycie: „i18n: pl: ok=ok; save=zapisz | en: ok=ok”. Pokażę brakujące klucze między językami.";
        var locales = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (string part in parts)
        {
            int colon = part.IndexOf(':');
            if (colon <= 0) continue;
            string code = part[..colon].Trim();
            if (!Regex.IsMatch(code, "^[a-z]{2}(-[A-Z]{2})?$")) continue;
            var keys = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string entry in part[(colon + 1)..].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int eq = entry.IndexOf('=');
                if (eq > 0) keys[entry[..eq].Trim()] = entry[(eq + 1)..].Trim();
            }
            locales[code] = keys;
        }
        if (locales.Count < 2) return "Nie odczytałem dwóch wersji językowych — sprawdź format: „i18n: pl: ok=ok; save=zapisz | en: ok=ok”.";
        var allKeys = locales.Values.SelectMany(x => x.Keys).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var sb = new StringBuilder("I18N — KLUCZE (").Append(allKeys.Count).Append("):").AppendLine();
        foreach (var locale in locales)
        {
            var missing = allKeys.Where(k => !locale.Value.ContainsKey(k)).ToList();
            sb.Append("· ").Append(locale.Key).Append(": ").Append(missing.Count == 0 ? "komplet" : "brakuje " + string.Join(", ", missing)).AppendLine();
        }
        sb.Append("· klucze nazwane po znaczeniu („error.file.missing”), nie po treści — tłumaczenie nie zepsuje nazw");
        return sb.ToString();
    }

    private static string WebhookTemplate(string input)
    {
        string what = ExtraFlat(input ?? "").Trim();
        if (what.Length < 3) return "Użycie: „webhook szablon: zamowienie”. Zwrócę szkielet ładunku JSON z podpisem HMAC — do uzupełnienia u siebie.";
        return "WEBHOOK — ŁADUNEK (zdarzenie: " + what + "):" + Environment.NewLine +
            "{" + Environment.NewLine +
            "  \"event\": \"" + what + "\"," + Environment.NewLine +
            "  \"id\": \"evt_01J... unikalne, do deduplikacji po stronie odbiorcy\"," + Environment.NewLine +
            "  \"occurred_at\": \"2026-09-27T12:00:00Z\"," + Environment.NewLine +
            "  \"data\": { \"...\": \"konkretne pola zdarzenia\" }" + Environment.NewLine +
            "}" + Environment.NewLine +
            "· nagłówki: X-Signature = HMAC-SHA256(treść, sekret) — odbiorca weryfikuje PRZED parsowaniem" + Environment.NewLine +
            "· odbiór: odpowiedz 200 szybko, przetwarzaj asynchronicznie; przewiduj ponowienia (idempotencja po event.id)";
    }

    private static string TokenBucket(string input)
    {
        double[] nums = Numbers(input, 2);
        if (nums.Length < 2 || nums[0] < 1 || nums[1] <= 0)
            return "Użycie: „token bucket: 100 10” (pojemność wiadra, dopływ tokenów na sekundę). Wyliczę zachowanie limitu.";
        double capacity = nums[0], refill = nums[1];
        double burstSeconds = capacity / refill;
        return "TOKEN BUCKET: pojemność " + N(capacity) + ", dopływ " + N(refill) + "/s" + Environment.NewLine +
            "· start pełny → przepuści serię " + N(capacity) + " żądań od razu (burst), potem utrzyma średnio " + N(refill) + "/s" + Environment.NewLine +
            "· pełne wiadro po przerwie odbuduje się w " + N(burstSeconds) + " s" + Environment.NewLine +
            "· poza 429 wysyłaj Retry-After = 1/dopływ (" + N(1 / refill) + " s) — klient, który szanuje limit, sam się wyhamuje";
    }

    private static string RetryPlan(string input)
    {
        double[] nums = Numbers(input, 2);
        if (nums.Length < 2 || nums[0] < 1 || nums[0] > 10 || nums[1] <= 0)
            return "Użycie: „retry plan: 3 30” (liczba ponowień, pierwsza pauza w sekundach). Rozpiszę harmonogram z backoffem.";
        int attempts = (int)nums[0];
        double pause = nums[1];
        var gaps = new List<string>();
        double total = pause;
        for (int i = 0; i < attempts; i++)
        {
            gaps.Add(N(pause * Math.Pow(2, i), 0) + " s");
            if (i > 0) total += pause * Math.Pow(2, i);
        }
        return "RETRY PLAN: próba początkowa + " + attempts + " ponowienia po " + string.Join(", ", gaps) + " (razem ~" + N(total, 0) + " s oczekiwania)" + Environment.NewLine +
            "· podwój pauzę przy każdym podejściu (backoff wykładniczy) + jitter ±20%, by klienci nie atakowali falą" + Environment.NewLine +
            "· ponawiaj tylko błędy przejściowe (sieć, 503, 429); 4xx to błąd żądania — powtórka niczego nie naprawi";
    }

    private static string Sessions(string input)
    {
        double[] nums = Numbers(input, 1);
        if (nums.Length < 1 || nums[0] < 1 || nums[0] > 10)
            return "Użycie: „sesje: 4 25 5” (liczba sesji, minuty sesji, minuty przerwy). Rozpiszę plan pracy z długą przerwą na końcu.";
        int count = (int)nums[0];
        double work = nums.Length > 1 ? nums[1] : 25;
        double brk = nums.Length > 2 ? nums[2] : 5;
        double totalWork = count * work, totalBreaks = (count - 1) * brk + 15;
        var sb = new StringBuilder("PLAN SESJI (").Append(count).Append(" × ").Append(N(work, 0)).Append(" min):").AppendLine();
        for (int i = 1; i <= count; i++)
        {
            sb.Append("· sesja ").Append(i).Append(": ").Append(N(work, 0)).Append(" min");
            sb.Append(i < count ? " → przerwa " + N(brk, 0) + " min" : " → DŁUGA przerwa 15 min");
            sb.AppendLine();
        }
        sb.Append("· praca: ").Append(N(totalWork, 0)).Append(" min, przerwy: ").Append(N(totalBreaks, 0)).Append(" min, razem: ").Append(N(totalWork + totalBreaks, 0)).Append(" min");
        return sb.ToString();
    }

    private static string MeetingCost(string input)
    {
        double[] nums = Numbers(input, 3);
        if (nums.Length < 3 || nums[0] < 1 || nums[1] <= 0 || nums[2] < 0)
            return "Użycie: „koszt spotkania: 6 60 120” (osoby, minuty, zł/h). Policzę koszt w godzinach ludzkiej pracy.";
        double cost = nums[0] * (nums[1] / 60.0) * nums[2];
        return "KOSZT SPOTKANIA: " + N(nums[0], 0) + " osób × " + N(nums[1], 0) + " min × " + N(nums[2], 0) + " zł/h = " + N(cost, 0) + " zł" + Environment.NewLine +
            "· to koszt realny: ludzie przestają pracować na rzecz spotkania na tę godzinę" + Environment.NewLine +
            "· przed wysłaniem zaproszenia: agendę w treści, podsumowanie po — spotkanie bez notatki prawie się nie zdarzyło";
    }

    private static string WorkHours(string input)
    {
        var m = Regex.Match((input ?? "").Trim(), @"(\d{1,2}):(\d{2})\s*-\s*(\d{1,2}):(\d{2})(?:\s+(\d{1,3}))?$");
        if (!m.Success) return "Użycie: „godziny pracy: 8:00-16:30 45” (przyjście-wyjście, przerwa w minutach). Policzę czas netto.";
        int start = int.Parse(m.Groups[1].Value) * 60 + int.Parse(m.Groups[2].Value);
        int end = int.Parse(m.Groups[3].Value) * 60 + int.Parse(m.Groups[4].Value);
        int brk = m.Groups[5].Success ? int.Parse(m.Groups[5].Value) : 0;
        int net = end - start - brk;
        if (net <= 0) return "Przerwa zjada cały czas albo godziny są odwrócone — sprawdź zapis (np. „godziny pracy: 8:00-16:30 45”).";
        return "CZAS PRACY: " + m.Groups[1].Value + ":" + m.Groups[2].Value + " – " + m.Groups[3].Value + ":" + m.Groups[4].Value +
            " z przerwą " + N(brk, 0) + " min = " + (net / 60) + " h " + (net % 60).ToString("00", Pl) + " netto (" + N(net, 0) + " min)";
    }

    private static string ExtraFlat(string input)
    {
        string s = (input ?? "").ToLowerInvariant();
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            sb.Append(c switch { 'ą' => 'a', 'ć' => 'c', 'ę' => 'e', 'ł' => 'l', 'ń' => 'n', 'ó' => 'o', 'ś' => 's', 'ź' => 'z', 'ż' => 'z', _ => c });
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    private static bool ExtraIs(string norm, string trigger) => norm == trigger || norm.StartsWith(trigger + ":");

    private static double[] Numbers(string input, int min)
    {
        var result = new List<double>();
        string normalized = Regex.Replace(input ?? "", @"(?<=\d),(?=\d)", ".");
        foreach (string token in Regex.Split(normalized, "[\\s;+]+"))
        {
            double v = ExtraNum(token);
            if (double.IsFinite(v)) result.Add(v);
            if (result.Count >= 8) break;
        }
        return result.Count >= min ? result.ToArray() : [];
    }

    private static string N(double v, int? digits = null) => digits == 0 ? v.ToString("0", Pl) : v.ToString("0.##", Pl);

    private static double ExtraNum(string s)
    {
        s = (s ?? "").Trim().Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;
    }
}
