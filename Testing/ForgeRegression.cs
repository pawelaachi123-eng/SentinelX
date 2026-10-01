using System.IO;
namespace SentinelX.Tests;

/// <summary>0.96 · KUŹNIA: the new offline tools. Pure functions — no machine state, no network, no files.</summary>
internal static class ForgeRegression
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    /// <summary>pl-PL groups digits with a non-breaking space; the assertions compare without any spaces.</summary>
    private static string Compact(string value) => value.Replace("\u00a0", "").Replace(" ", "");
    private static string Unix(string value) => value.Replace("\r\n", "\n");

    public static Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);

        // --- variable names ---
        string names = UtilityToolbox.VariableNames("liczba użytkowników aktywnych");
        Check(names.Contains("camelCase: liczbaUzytkownikowAktywnych") && names.Contains("PascalCase: LiczbaUzytkownikowAktywnych"), "camel/Pascal names: " + names);
        Check(names.Contains("snake_case: liczba_uzytkownikow_aktywnych") && names.Contains("kebab-case: liczba-uzytkownikow-aktywnych"), "snake/kebab names: " + names);
        Check(names.Contains("UPPER_SNAKE_CASE: LICZBA_UZYTKOWNIKOW_AKTYWNYCH"), "UPPER_SNAKE name: " + names);
        string split = UtilityToolbox.VariableNames("getHTTPResponse2");
        Check(split.Contains("snake_case: get_http_response_2") && split.Contains("camelCase: getHttpResponse2"), "camelCase input must be split into words: " + split);
        Check(UtilityToolbox.VariableNames("2 koty").Contains("nie może zaczynać się od cyfry"), "A leading digit must be reported.");
        Check(UtilityToolbox.VariableNames("!!!").StartsWith("Nie znalazłem"), "A text without letters must be refused honestly.");

        // --- URL encoding ---
        Check(UtilityToolbox.UrlEncode("ala ma kota & psa") == "Zakodowane (procent-kodowanie UTF-8): ala%20ma%20kota%20%26%20psa", "URL encoding: " + UtilityToolbox.UrlEncode("ala ma kota & psa"));
        Check(UtilityToolbox.UrlEncode("zażółć").Contains("za%C5%BC%C3%B3%C5%82%C4%87"), "Polish letters must be UTF-8 percent-encoded.");
        Check(UtilityToolbox.UrlDecode("ala%20ma%20kota%20%26%20psa") == "Odkodowane: ala ma kota & psa", "URL decoding.");
        Check(UtilityToolbox.UrlDecode(UtilityToolbox.UrlEncode("zażółć gęślą").Replace("Zakodowane (procent-kodowanie UTF-8): ", "")) == "Odkodowane: zażółć gęślą", "URL roundtrip.");

        // --- Unix time (the UTC part is time-zone independent) ---
        Check(UtilityToolbox.UnixToDate("1700000000").Contains("UTC 14.11.2023 22:13:20"), "Unix seconds: " + UtilityToolbox.UnixToDate("1700000000"));
        Check(UtilityToolbox.UnixToDate("0").Contains("UTC 01.01.1970 00:00:00"), "The epoch.");
        string millis = UtilityToolbox.UnixToDate("1700000000000");
        Check(millis.Contains("milisekundy") && millis.Contains("UTC 14.11.2023 22:13:20"), "Milliseconds must be recognised and said so: " + millis);
        Check(UtilityToolbox.UnixToDate("999999999999999").Contains("poza zakresem"), "An absurd value must be refused.");
        Check(UtilityToolbox.DateToUnix("14.11.2023 22:13:20 utc").Contains("Unix: 1700000000"), "Date to Unix (dotted): " + UtilityToolbox.DateToUnix("14.11.2023 22:13:20 utc"));
        Check(UtilityToolbox.DateToUnix("2023-11-14 22:13:20 UTC").Contains("Unix: 1700000000"), "Date to Unix (ISO).");
        Check(UtilityToolbox.DateToUnix("jutro").StartsWith("Nie rozpoznałem daty"), "A non-date must be refused.");

        // --- word frequency ---
        string frequency = UtilityToolbox.WordFrequency("ala ma kota ala ma psa ala");
        Check(frequency.Contains("słów: 7, różnych: 4") && frequency.Contains("1. ala — 3×") && frequency.Contains("2. ma — 2×"), "Word frequency: " + frequency);
        Check(UtilityToolbox.WordFrequency("!!!").StartsWith("Nie znalazłem"), "A text without words must be reported.");

        // --- loan instalment (annuity) ---
        string loan = Compact(UtilityToolbox.LoanPayment("300000", "25", "7,5"));
        Check(loan.Contains("Ratamiesięczna:2216,97zł") && loan.Contains("Dospłatyrazem:665092,06zł") && loan.Contains("odsetki:365092,06zł"), "Loan figures: " + loan);
        Check(Compact(UtilityToolbox.LoanPayment("12000", "1", "0")).Contains("Ratamiesięczna:1000,00zł"), "A zero rate divides evenly.");
        Check(UtilityToolbox.LoanPayment("1000", "60", "5").Contains("od 1 do 50 lat"), "An absurd term must be refused.");
        Check(UtilityToolbox.LoanPayment("0", "5", "5").Contains("dodatnia"), "A zero amount must be refused.");
        Check(UtilityToolbox.Process("rata kredytu: abc", "rata kredytu: abc")!.StartsWith("Format:"), "A malformed loan command must show the format.");

        // --- version comparison ---
        Check(UtilityToolbox.CompareVersions("1.2.10 ||| 1.10.0").StartsWith("1.2.10 < 1.10.0 — nowsza jest druga"), "1.10.0 is newer than 1.2.10: " + UtilityToolbox.CompareVersions("1.2.10 ||| 1.10.0"));
        Check(UtilityToolbox.CompareVersions("1.2 ||| 1.2.0").Contains("ta sama wersja"), "1.2 and 1.2.0 are the same version.");
        Check(UtilityToolbox.CompareVersions("v2.0.1 ||| 2.0.0").StartsWith("v2.0.1 > 2.0.0"), "A leading v is ignored and order is detected.");
        Check(UtilityToolbox.CompareVersions("abc ||| 1.0").Contains("Nie rozpoznałem wersji"), "A non-version must be refused.");
        Check(UtilityToolbox.CompareVersions("1.0").Contains("dwie wersje"), "A single version must show usage.");

        // --- line tools ---
        string numbered = Unix(UtilityToolbox.NumberLines("kot | pies | ryba"));
        Check(numbered == "Ponumerowane wiersze (3):\n1. kot\n2. pies\n3. ryba", "Numbered lines: " + numbered);
        string reversed = UtilityToolbox.ReverseLines("pierwszy | drugi | trzeci");
        Check(reversed.Contains("(3)") && reversed.IndexOf("trzeci", StringComparison.Ordinal) < reversed.IndexOf("pierwszy", StringComparison.Ordinal), "Reversed lines: " + reversed);
        Check(UtilityToolbox.NumberLines("   ").StartsWith("Podaj wiersze"), "Empty input must show usage.");

        // --- spacing ---
        string spacing = UtilityToolbox.FixSpacing("ala   ma    kota");
        Check(spacing.Contains("Poprawione: ala ma kota") && spacing.Contains("Usunięte nadmiarowe odstępy: 5 (z 16 do 11 znaków)"), "Spacing: " + spacing);
        Check(UtilityToolbox.FixSpacing("ala ma kota").Contains("w porządku"), "Clean text must be left alone.");

        // --- the chat entry points reach the same functions ---
        foreach (var (command, expected) in new[]
        {
            ("nazwa zmiennej: moja zmienna", "camelCase: mojaZmienna"), ("url zakoduj: a b", "a%20b"), ("unix: 1700000000", "UTC 14.11.2023"),
            ("czestosc slow: a a b", "1. a — 2×"), ("rata kredytu: 12000 1 0", "Rata miesięczna"), ("porownaj wersje: 1.9 ||| 1.10", "nowsza jest druga"),
            ("numeruj linie: a | b", "Ponumerowane"), ("odwroc linie: a | b", "Odwrócona"), ("popraw odstepy: a    b", "Poprawione: a b")
        })
        {
            string answer = Compact(UtilityToolbox.Process(command, ConversationMemoryService.Normalize(command)) ?? throw new InvalidOperationException("Not handled: " + command));
            string want = Compact(expected);
            Check(answer.Contains(want), "Chat command „" + command + "” answered without „" + want + "”: " + answer);
        }
        return Task.CompletedTask;
    }
}
