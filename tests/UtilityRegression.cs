using System.IO;
using System.Linq;

namespace SentinelX.Tests;

/// <summary>Deterministic offline tools: arithmetic, units, dates, text, codes, randomness.
/// Every assertion below is a pure function result — no machine state, no network.</summary>
internal static class UtilityRegression
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string Require(string? value, string label) => value ?? throw new InvalidOperationException(label + " was not handled as a tool");
    /// <summary>Normalises the non-breaking space used by pl-PL group formatting so assertions stay readable.</summary>
    private static string Flat(string value) => value.Replace('\u00a0', ' ').Replace("  ", " ");

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

        // --- 0.91 · CENTRUM: math beyond the calculator ---
        Check(Require(UtilityToolbox.Process("pierwiastek 144", "pierwiastek 144"), "sqrt").Contains("= 12"), "square root");
        Check(UtilityToolbox.Process("pierwiastek -4", "pierwiastek -4")!.StartsWith("Nie liczę pierwiastka"), "negative sqrt must be refused");
        Check(Require(UtilityToolbox.Process("silnia 10", "silnia 10"), "factorial").Contains("3628800"), "10! is a published constant");
        Check(Require(UtilityToolbox.Process("nwd 12 8", "nwd 12 8"), "gcd").Contains("= 4"), "greatest common divisor");
        Check(Require(UtilityToolbox.Process("nww 4 6", "nww 4 6"), "lcm").Contains("= 12"), "least common multiple");
        Check(UtilityToolbox.Process("czy pierwsza 97", "czy pierwsza 97")!.Contains("jest liczbą pierwszą"), "97 is prime");
        Check(UtilityToolbox.Process("czy pierwsza 98", "czy pierwsza 98")!.Contains("nie jest liczbą pierwszą"), "98 is not prime");
        Check(UtilityToolbox.Process("dzielniki 12", "dzielniki 12")!.Contains("1, 2, 3, 4, 6, 12"), "divisors of 12");
        Check(UtilityToolbox.Process("fibonacci 10", "fibonacci 10")!.Contains("= 55"), "F(10)=55");
        Check(UtilityToolbox.Process("srednia: 2, 4, 6", "srednia: 2, 4, 6")!.Contains("Średnia: 4"), "mean of 2,4,6");
        Check(UtilityToolbox.Process("mediana: 3, 1, 2", "mediana: 3, 1, 2")!.Contains("Mediana: 2"), "median of 3,1,2");
        Check(UtilityToolbox.Process("suma: 1, 2, 3", "suma: 1, 2, 3")!.Contains("Suma: 6"), "sum of 1,2,3");
        Check(UtilityToolbox.Process("min: 5, 2, 9", "min: 5, 2, 9")!.Contains("Minimum: 2"), "minimum");
        Check(UtilityToolbox.Process("max: 5, 2, 9", "max: 5, 2, 9")!.Contains("Maksimum: 9"), "maximum");
        Check(UtilityToolbox.Process("zaokraglij 3,14159 do 2", "zaokraglij 3,14159 do 2")!.Contains("3,14"), "rounding to 2 places");
        Check(UtilityToolbox.Process("zmiana z 50 do 80", "zmiana z 50 do 80")!.Contains("+60%"), "percent change up");
        Check(UtilityToolbox.Process("zmiana z 80 do 50", "zmiana z 80 do 50")!.Contains("-37,5%"), "percent change down");

        // --- 0.91 · CENTRUM: text analysis ---
        Check(UtilityToolbox.Process("ile znakow: ala", "ile znakow: ala")!.Contains("Znaki: 3"), "character counting");
        Check(UtilityToolbox.Process("ile zdan: Ala ma kota. Kot śpi.", "ile zdan: Ala ma kota. Kot śpi.")!.Contains("Zdania: 2"), "sentence counting");
        Check(UtilityToolbox.Process("palindrom: kajak", "palindrom: kajak")!.Contains("jest palindromem"), "kajak is a palindrome");
        Check(UtilityToolbox.Process("palindrom: Sentinel", "palindrom: Sentinel")!.Contains("nie jest palindromem"), "Sentinel is not a palindrome");
        Check(UtilityToolbox.Process("anagram: kot, tok", "anagram: kot, tok")!.Contains("są anagramami"), "kot/tok are anagrams");
        Check(UtilityToolbox.Process("rot13: ala", "rot13: ala")!.Contains("ROT13: nyn"), "rot13 of ala");
        Check(UtilityToolbox.Process("tytul: ala ma kota", "tytul: ala ma kota")!.Contains("Tytuł: Ala Ma Kota"), "title case");

        // --- 0.91 · CENTRUM: encodings ---
        Check(UtilityToolbox.Process("morse: sos", "morse: sos")!.Contains("... --- ..."), "SOS in Morse");
        Check(UtilityToolbox.Process("dekoduj morse: ... --- ...", "dekoduj morse: ... --- ...")!.Contains("Zdekodowane: SOS"), "Morse decoding");
        Check(UtilityToolbox.Process("binarnie: A", "binarnie: A")!.Contains("01000001"), "binary encoding of A");
        Check(UtilityToolbox.Process("dekoduj binarnie: 01000001", "dekoduj binarnie: 01000001")!.Contains("Zdekodowane: A"), "binary decoding");
        Check(UtilityToolbox.Process("hex: Ala", "hex: Ala")!.Contains("41 6C 61"), "hex encoding");
        Check(UtilityToolbox.Process("dekoduj hex: 416C61", "dekoduj hex: 416C61")!.Contains("Zdekodowane: Ala"), "hex decoding");

        // --- 0.91 · CENTRUM: Polish identifiers (checksums are public constants) ---
        Check(UtilityToolbox.Pesel("90010112349").Contains("PESEL poprawny"), "a checksum-valid PESEL must pass: " + UtilityToolbox.Pesel("90010112349"));
        Check(UtilityToolbox.Pesel("90010112349").Contains("01.01.1990"), "the PESEL encodes the birth date");
        Check(UtilityToolbox.Pesel("90010112349").Contains("kobieta"), "an even 10th digit means female");
        Check(UtilityToolbox.Pesel("90010112345").StartsWith("PESEL niepoprawny"), "a broken checksum must be rejected");
        Check(UtilityToolbox.Nip("1234567802").Contains("NIP poprawny"), "a checksum-valid NIP must pass");
        Check(UtilityToolbox.Nip("1234567890").StartsWith("NIP niepoprawny"), "a broken NIP checksum must be rejected");
        Check(UtilityToolbox.Iban("PL61 1090 1014 0000 0712 1981 2874").Contains("IBAN poprawny"), "the published IBAN example must validate");
        Check(UtilityToolbox.Iban("PL61109010140000071219812875").StartsWith("IBAN niepoprawny"), "a flipped check digit must fail");
        Check(UtilityToolbox.Process("rgb 31 162 195", "rgb 31 162 195")!.Contains("#1FA2C3"), "RGB to hex");

        // --- 0.91 · CENTRUM: calendar ---
        Check(Require(UtilityToolbox.Process("tydzien roku", "tydzien roku"), "week").Contains("Tydzień:"), "ISO week number");
        Check(Require(UtilityToolbox.Process("dzien roku", "dzien roku"), "day").Contains("Dzień roku:"), "day of year");
        Check(Require(UtilityToolbox.Process("ile dni do konca roku", "ile dni do konca roku"), "year end").Contains("Do końca roku:"), "days to year end");
        Check(UtilityToolbox.Easter(2027).Contains("28.03.2027"), "Easter 2027 falls on 28 March: " + UtilityToolbox.Easter(2027));
        Check(UtilityToolbox.Workdays("1.1.2024", "31.1.2024").Contains("Dni robocze: 23"), "January 2024 had 23 weekdays: " + UtilityToolbox.Workdays("1.1.2024", "31.1.2024"));
        Check(UtilityToolbox.Age("01.01.1990").Contains("36 lat"), "someone born 01.01.1990 turns 36 in 2026: " + UtilityToolbox.Age("01.01.1990"));
        Check(UtilityToolbox.WorldClock("tokio").Contains("UTC+9"), "Tokyo is always UTC+9 (no DST): " + UtilityToolbox.WorldClock("tokio"));
        Check(UtilityToolbox.WorldClock("atlantyda").StartsWith("Nie znam"), "an unknown city must say so honestly");

        // --- 0.91 · CENTRUM: randomness and read-only system facts ---
        Check(Require(UtilityToolbox.Process("rzut moneta", "rzut moneta"), "coin").StartsWith("Moneta:"), "a coin flip answers");
        string lotto = Require(UtilityToolbox.Process("lotto", "lotto"), "lotto");
        Check(lotto.Contains("Lotto (6 z 49):") && lotto.Split(':')[1].Split('·')[0].Split(',').Length == 6, "lotto must return six numbers: " + lotto);
        Check(Require(UtilityToolbox.Process("pin 6", "pin 6"), "pin").Contains("PIN (6 cyfr):"), "PIN generation");
        Check(UtilityToolbox.Pin(2).StartsWith("PIN może mieć"), "a too-short PIN must be refused");
        Check(Require(UtilityToolbox.Process("nazwa komputera", "nazwa komputera"), "hostname").StartsWith("Komputer: "), "machine name is readable offline");
        Check(Require(UtilityToolbox.Process("ile rdzeni", "ile rdzeni"), "cores").StartsWith("Rdzenie logiczne:"), "core count is readable offline");
        Check(Require(UtilityToolbox.Process("architektura", "architektura"), "arch").StartsWith("Architektura"), "architecture is readable offline");
        Check(Require(UtilityToolbox.Process("moje ip", "moje ip"), "ip").Contains("adres"), "local IP listing answers offline (address or honest absence)");

        // --- 0.95 · WARSZTAT: diff, regex, wyciąganie danych, porządki w tekście ---
        string diff = Require(UtilityToolbox.Process("porownaj teksty: ala ma kota ||| ala ma psa", "porownaj teksty: ala ma kota ||| ala ma psa"), "diff");
        Check(diff.Contains("− tylko w pierwszym: ala ma kota"), "diff must show the line missing on the right: " + diff);
        Check(diff.Contains("+ tylko w drugim: ala ma psa"), "diff must show the line missing on the left: " + diff);
        Check(diff.Contains("różnice 2"), "diff must summarise the difference count: " + diff);
        Check(Require(UtilityToolbox.Process("porownaj teksty: ten sam tekst ||| ten sam tekst", "porownaj teksty: ten sam tekst ||| ten sam tekst"), "diff-same").StartsWith("Teksty są identyczne"), "identical texts must be reported as identical");
        Check(UtilityToolbox.DiffText("bez separatora").StartsWith("Podaj dwa teksty"), "a missing separator must be explained");
        Check(Require(UtilityToolbox.Process("diff: kot\n---\npies", "diff: kot\n---\npies"), "diff-dashes").Contains("różnice 2"), "a line of dashes must also split the two texts");

        string regex = Require(UtilityToolbox.Process("regex: \\d+ ||| mam 12 kotów i 3 psy", "regex: \\d+ ||| mam 12 kotów i 3 psy"), "regex");
        Check(regex.Contains("dopasowania: 2"), "regex must count the matches: " + regex);
        Check(regex.Contains("„12”"), "regex must show the matched text");
        Check(Require(UtilityToolbox.Process("regex: (\\w+)@(\\w+) ||| biuro@example", "regex: (\\w+)@(\\w+) ||| biuro@example"), "regex-groups").Contains("grupy: 1=biuro, 2=example"), "regex must expose groups");
        Check(UtilityToolbox.RegexTest("[ ||| tekst").StartsWith("Wzorzec nie jest poprawny"), "an invalid pattern must be refused with a reason");
        Check(Require(UtilityToolbox.Process("regex: zebra ||| ala ma kota", "regex: zebra ||| ala ma kota"), "regex-none").StartsWith("Brak dopasowań"), "a pattern without matches must say so");

        string extract = Require(UtilityToolbox.Process("wyciagnij: napisz na biuro@example.com albo wejdź na https://example.com, IP 10.0.0.7 i 42 zł", "wyciagnij: napisz na biuro@example.com albo wejdź na https://example.com, IP 10.0.0.7 i 42 zł"), "extract");
        Check(extract.Contains("biuro@example.com"), "extraction must find e-mails: " + extract);
        Check(extract.Contains("https://example.com"), "extraction must find links");
        Check(extract.Contains("10.0.0.7"), "extraction must find IPv4 addresses");
        Check(extract.Contains("42"), "extraction must find numbers");
        Check(Require(UtilityToolbox.Process("wyciagnij: nic tu nie ma", "wyciagnij: nic tu nie ma"), "extract-none").StartsWith("Nie znalazłem"), "an empty extraction must be honest");

        string sorted = Require(UtilityToolbox.Process("posortuj linie: zebra\nkot\nAla", "posortuj linie: zebra\nkot\nAla"), "sort");
        Check(sorted.IndexOf("Ala", StringComparison.Ordinal) < sorted.IndexOf("kot", StringComparison.Ordinal), "sorting must be case-insensitive and stable: " + sorted);
        Check(sorted.Contains("Posortowane wiersze (3)"), "sorting must report the line count");
        string piped = Require(UtilityToolbox.Process("posortuj linie: zebra | kot | Ala", "posortuj linie: zebra | kot | Ala"), "sort-pipe");
        Check(piped.Contains("Posortowane wiersze (3)") && piped.IndexOf("Ala", StringComparison.Ordinal) < piped.IndexOf("zebra", StringComparison.Ordinal), " „ | ” must also separate lines: " + piped);
        string unique = Require(UtilityToolbox.Process("unikalne linie: kot\npies\nkot", "unikalne linie: kot\npies\nkot"), "unique");
        Check(unique.Contains("usunięte: 1"), "deduplication must report what it removed: " + unique);
        Check(unique.Contains("zostało 2 z 3"), "deduplication must keep one copy of each line");

        // --- 0.95 · WARSZTAT: kwota słownie, czas, moc hasła, QR ---
        Check(UtilityToolbox.AmountInWords("1234,56").Contains("tysiąc dwieście trzydzieści cztery złote 56 groszy"), "amounts need correct Polish forms: " + UtilityToolbox.AmountInWords("1234,56"));
        Check(UtilityToolbox.AmountInWords("1").Contains("jeden złoty 0 groszy"), "one złoty is singular: " + UtilityToolbox.AmountInWords("1"));
        Check(UtilityToolbox.AmountInWords("5").Contains("pięć złotych 0 groszy"), "five złotych is plural genitive");
        Check(UtilityToolbox.AmountInWords("2000").Contains("dwa tysiące złotych"), "2000 → dwa tysiące złotych: " + UtilityToolbox.AmountInWords("2000"));
        Check(UtilityToolbox.AmountInWords("5000").Contains("pięć tysięcy złotych"), "5000 → pięć tysięcy złotych");
        Check(UtilityToolbox.AmountInWords("1000000").Contains("milion złotych"), "a million uses the short form: " + UtilityToolbox.AmountInWords("1000000"));
        Check(UtilityToolbox.AmountInWords("12,5").Contains("dwanaście złotych 50 groszy"), "grosze come from the decimal part: " + UtilityToolbox.AmountInWords("12,5"));
        Check(UtilityToolbox.AmountInWords("abc").StartsWith("Nie rozpoznałem kwoty"), "junk must be refused");
        Check(Require(UtilityToolbox.Process("kwota slownie: 250,05", "kwota slownie: 250,05"), "amount-cmd").Contains("dwieście pięćdziesiąt złotych 5 groszy"), "the chat form must reach the tool");

        Check(Flat(UtilityToolbox.SecondsText("3661", toSeconds: false)).Contains("1 h 1 min 1 s"), "3661 s = 1 h 1 min 1 s: " + Flat(UtilityToolbox.SecondsText("3661", toSeconds: false)));
        Check(Flat(UtilityToolbox.SecondsText("90000", toSeconds: false)).Contains("1 d 1 h"), "days must appear above 24 hours: " + Flat(UtilityToolbox.SecondsText("90000", toSeconds: false)));
        Check(Flat(UtilityToolbox.SecondsText("2h 15m 10s", toSeconds: true)).Contains("= 8 110 s"), "2h 15m 10s = 8110 s: " + Flat(UtilityToolbox.SecondsText("2h 15m 10s", toSeconds: true)));
        Check(Flat(UtilityToolbox.SecondsText("90min", toSeconds: true)).Contains("= 5 400 s"), "90 minutes = 5400 s: " + Flat(UtilityToolbox.SecondsText("90min", toSeconds: true)));
        Check(UtilityToolbox.SecondsText("kiedyś", toSeconds: true).StartsWith("Nie rozpoznałem czasu"), "a missing unit must be refused");
        Check(Flat(Require(UtilityToolbox.Process("sekundy: 3661", "sekundy: 3661"), "seconds-cmd")).Contains("1 h 1 min 1 s"), "the chat form must reach the seconds tool");

        Check(UtilityToolbox.PasswordStrength("abc").Contains("bardzo słabe"), "a three-letter password is very weak: " + UtilityToolbox.PasswordStrength("abc"));
        Check(UtilityToolbox.PasswordStrength("abc").Contains("krótsze niż 12 znaków"), "short passwords must be flagged");
        Check(UtilityToolbox.PasswordStrength("qwerty123").Contains("typowy fragment"), "common fragments must be flagged");
        Check(UtilityToolbox.PasswordStrength("T7#vQ!92Lm$4Zp&Xw").Contains("bardzo silne"), "a long mixed password must score high: " + UtilityToolbox.PasswordStrength("T7#vQ!92Lm$4Zp&Xw"));
        Check(!UtilityToolbox.PasswordStrength("TajneHaslo123!").Contains("TajneHaslo123!"), "the password itself must never be echoed back");
        Check(Require(UtilityToolbox.Process("moc hasla: abc", "moc hasla: abc"), "strength-cmd").Contains("bardzo słabe"), "the chat form must reach the strength meter");

        string qrDirectory = Path.Combine(Path.GetTempPath(), "sentinel-qr-" + Guid.NewGuid().ToString("N"));
        string qr = UtilityToolbox.QrCode("qr: test kodowania", qrDirectory);
        Check(qr.StartsWith("Kod QR zapisany lokalnie"), "a QR code must be written locally: " + qr);
        Check(Directory.GetFiles(qrDirectory, "*.png").Length == 1, "exactly one PNG must be created");
        Check(new FileInfo(Directory.GetFiles(qrDirectory, "*.png")[0]).Length > 200, "the PNG must contain real image data");
        Check(UtilityToolbox.QrCode("qr wifi: MojaSiec|tajnehaslo", qrDirectory).Contains("dane sieci Wi-Fi"), "the Wi-Fi form must be reported as such");
        Check(Directory.GetFiles(qrDirectory, "*.png").Length == 2, "the Wi-Fi code must be a second PNG");
        Check(UtilityToolbox.QrCode("qr:", qrDirectory).StartsWith("Podaj treść kodu"), "an empty QR command must be refused");
        Check(Require(UtilityToolbox.Process("qr: cokolwiek", "qr: cokolwiek"), "qr-cmd") != null, "the chat form must reach the QR tool");
        Directory.Delete(qrDirectory, true);

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
