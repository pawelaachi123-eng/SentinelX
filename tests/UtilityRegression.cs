using System.Linq;

namespace SentinelX.Tests;

/// <summary>Deterministic offline tools: arithmetic, units, dates, text, codes, randomness.
/// Every assertion below is a pure function result — no machine state, no network.</summary>
internal static class UtilityRegression
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string Require(string? value, string label) => value ?? throw new InvalidOperationException(label + " was not handled as a tool");

    public static Task RunAsync(string directory)
    {
        // --- safe arithmetic (never evaluates code) ---
        Check(UtilityToolbox.Calculate("2+2*3") == "2+2*3 = 8", "operator precedence must hold: " + UtilityToolbox.Calculate("2+2*3"));
        Check(UtilityToolbox.Calculate("(2+2)*3") == "(2+2)*3 = 12", "parentheses must hold");
        Check(UtilityToolbox.Calculate("2^10") == "2^10 = 1024", "power must work");
        Check(UtilityToolbox.Calculate("12,5*4") == "12,5*4 = 50", "a Polish decimal comma must be accepted");
        Check(UtilityToolbox.Calculate("-5+2") == "-5+2 = -3", "unary minus must work");
        Check(UtilityToolbox.Calculate("10/0").Contains("dzielenie przez zero"), "division by zero must be refused politely");
        Check(UtilityToolbox.Calculate("abc").StartsWith("Nie policzę tego"), "junk must be rejected: " + UtilityToolbox.Calculate("abc"));
        Check(UtilityToolbox.Calculate("2+").Contains("urwane"), "an unfinished expression must be reported");
        Check(UtilityToolbox.Calculate("Process.Start(\"calc\")").StartsWith("Nie policzę tego"), "no code may be interpreted as math");

        // --- units ---
        Check(UtilityToolbox.ConvertUnit(5, "km", "mile") == "3,1069 mile", "km to miles: " + UtilityToolbox.ConvertUnit(5, "km", "mile"));
        Check(UtilityToolbox.ConvertUnit(21, "C", "F") == "69,8 °F", "celsius to fahrenheit");
        Check(UtilityToolbox.ConvertUnit(1, "gb", "mb") == "1024 mb", "gigabytes to megabytes");
        Check(UtilityToolbox.ConvertUnit(100, "kmh", "ms") == "27,7778 ms", "km/h to m/s");
        Check(UtilityToolbox.ConvertUnit(1, "kg", "lb").StartsWith("2,2046"), "kilograms to pounds");
        Check(UtilityToolbox.ConvertUnit(1, "kilogram", "lb").StartsWith("2,2046"), "spelled-out Polish unit names must resolve");
        Check(UtilityToolbox.ConvertUnit(21, "celsjusz", "fahrenheit") == "69,8 °F", "spelled-out temperature names must resolve");
        Check(UtilityToolbox.ConvertUnit(100, "km/h", "m/s") == "27,7778 m/s", "slash units must resolve and echo the requested name");
        Check(UtilityToolbox.ConvertUnit(1, "parsec", "km").StartsWith("Nie znam jednostki"), "an unknown unit must say so");

        // --- dates ---
        Check(UtilityToolbox.WeekdayOf("1.1.2030").Contains("wtorek"), "1 Jan 2030 is a Tuesday: " + UtilityToolbox.WeekdayOf("1.1.2030"));
        Check(UtilityToolbox.WeekdayOf("32.13.2030").StartsWith("Nie rozpoznałem"), "an impossible date must be refused");
        Check(UtilityToolbox.DaysBetween("od", "1.1.2020").Contains("minęło"), "days since a date must be reported");
        Check(UtilityToolbox.TimeUntil(23, 59).Contains("Do 23:59"), "time until a clock time must be reported");
        Check(UtilityToolbox.TimeUntil(25, 0).StartsWith("Godzina musi być"), "an impossible hour must be refused");

        // --- text ---
        Check(UtilityToolbox.CountWords("ala ma kota").Contains("Słowa: 3"), "word counting");
        Check(UtilityToolbox.CountWords("ala ma kota").Contains("znaki: 11"), "character counting");
        Check(UtilityToolbox.ToBase64("ala").Contains("YWxh"), "base64 encoding");
        Check(UtilityToolbox.FromBase64("YWxh") == "Zdekodowane: ala", "base64 decoding");
        Check(UtilityToolbox.FromBase64("nie-base64!!").Contains("nie jest poprawny"), "bad base64 must be refused");
        Check(UtilityToolbox.HashText("abc") == "SHA-256 (UTF-8, 3 B): ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            "the SHA-256 of \"abc\" is a published constant: " + UtilityToolbox.HashText("abc"));
        Check(UtilityToolbox.Transliterate("ZaŻółć Gęślą Jaźń") == "ZaZolc Gesla Jazn", "diacritics transliteration");
        Check(UtilityToolbox.Slugify("ZaŻółć Gęślą Jaźń!") == "Slug: zazolc-gesla-jazn", "slug building");
        Check(UtilityToolbox.Slugify("!!!").StartsWith("Z tego tekstu"), "a slugless text must be reported honestly");
        Check(UtilityToolbox.FormatJson("{\"a\":1}").StartsWith("JSON poprawny"), "valid JSON must be confirmed");
        Check(UtilityToolbox.FormatJson("{a:1").StartsWith("JSON jest niepoprawny"), "invalid JSON must be reported");
        Check(UtilityToolbox.FormatJson("{\"a\":1}").Contains("\"a\": 1"), "valid JSON must be pretty-printed");

        // --- codes ---
        Check(UtilityToolbox.ToRoman(2026) == "MMXXVI", "roman numerals");
        Check(UtilityToolbox.ToRoman(4000).StartsWith("Zamieniam"), "out of range must be refused");
        Check(UtilityToolbox.FromRoman("MMXXVI") == "MMXXVI = 2026", "roman parsing");
        Check(UtilityToolbox.FromRoman("IIII").StartsWith("To nie jest poprawna"), "a non-canonical numeral must be refused");
        Check(UtilityToolbox.DescribeColor("1fa2c3").Contains("RGB(31, 162, 195)"), "hex colour conversion: " + UtilityToolbox.DescribeColor("1fa2c3"));
        Check(UtilityToolbox.DescribeColor("ffffff").Contains("z czernią 21,0:1"), "white against black must be 21:1, got " + UtilityToolbox.DescribeColor("ffffff"));
        Check(UtilityToolbox.Bmi("80", "180").StartsWith("BMI 24,7"), "BMI calculation: " + UtilityToolbox.Bmi("80", "180"));
        Check(UtilityToolbox.Bmi("80", "180").Contains("orientacyjnym"), "BMI must carry its honest disclaimer");
        Check(UtilityToolbox.Bmi("5", "180").StartsWith("Podaj wagę"), "implausible values must be refused");

        // --- randomness ---
        for (int i = 0; i < 25; i++)
        {
            string roll = UtilityToolbox.Roll("1", "6");
            int value = int.Parse(new string(roll.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray()));
            Check(value is >= 1 and <= 6, "a dice roll must stay in range: " + roll);
        }
        Check(UtilityToolbox.Dice(3).Contains("suma:"), "dice must report a total");
        Check(UtilityToolbox.Dice(0).StartsWith("Można rzucić"), "zero dice must be refused");
        Check(UtilityToolbox.Choose("pizza, sushi").StartsWith("Wybrano:"), "a pick must return one option");
        Check(UtilityToolbox.Choose("pizza").StartsWith("Podaj co najmniej dwie"), "a single option must be refused");

        // --- passwords: local only, mixed classes, never stored ---
        string passwordLine = UtilityToolbox.Password("haslo 24").Split('\n')[1];
        Check(passwordLine.Length == 24, "the generated password must be exactly the requested length, got " + passwordLine.Length);
        Check(passwordLine.Any(char.IsLower) && passwordLine.Any(char.IsUpper) && passwordLine.Any(char.IsDigit), "the password must mix character classes");
        Check(UtilityToolbox.Password("haslo 4").StartsWith("Długość"), "a too-short password must be refused");
        Check(UtilityToolbox.Password("haslo 16") != UtilityToolbox.Password("haslo 16"), "two generations must differ (cryptographic source)");

        // --- the router surface: handled tools versus everything else ---
        Check(Require(UtilityToolbox.Process("policz 12+8", "policz 12+8"), "policz").Contains("= 20"), "the chat form must reach the calculator");
        Check(Require(UtilityToolbox.Process("przelicz 100 km na mile", "przelicz 100 km na mile"), "przelicz").StartsWith("62,1371"), "the chat form must reach the converter");
        Check(Require(UtilityToolbox.Process("procent 15 z 240", "procent 15 z 240"), "procent").StartsWith("15% z 240 = 36"), "percent of a value");
        Check(Require(UtilityToolbox.Process("ile to procent 30 z 240", "ile to procent 30 z 240"), "share").StartsWith("30 z 240 = 12,5%"), "value as a percent");
        Check(Require(UtilityToolbox.Process("vat 100", "vat 100"), "vat").Contains("brutto 123,00"), "VAT gross from net");
        Check(Require(UtilityToolbox.Process("uuid", "uuid"), "uuid").StartsWith("UUID: ") && Require(UtilityToolbox.Process("uuid", "uuid"), "uuid").Length == 42, "a UUID must be returned");
        Check(Require(UtilityToolbox.Process("wielkie litery: kot", "wielkie litery: kot"), "case") == "KOT", "case conversion");
        Check(Require(UtilityToolbox.Process("odwroc tekst: kot", "odwroc tekst: kot"), "reverse") == "tok", "text reversal");
        Check(UtilityToolbox.Process("napisz wiersz", "napisz wiersz") == null, "free conversation must not be caught by a tool");
        Check(UtilityToolbox.Process("ile mam ramu", "ile mam ramu") == null, "measurements stay with the read-only catalogue");
        Check(UtilityToolbox.Process("zamknij notatnik", "zamknij notatnik") == null, "tools must never swallow an action command");
        return Task.CompletedTask;
    }
}
