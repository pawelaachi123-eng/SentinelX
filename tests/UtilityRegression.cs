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
        // ============================ 0.94 · narzędzia ============================
        // --- VAT with an explicit rate; the frozen default stays „brutto 123,00” ---
        Check(Require(UtilityToolbox.Process("vat 8 100", "vat 8 100"), "vat 8%").Contains("brutto 108,00"), "8% of 100 grosses to 108,00");
        Check(Require(UtilityToolbox.Process("vat 5 100 netto", "vat 5 100 netto"), "vat netto").Contains("brutto 105,00"), "explicit netto input");
        Check(Require(UtilityToolbox.Process("vat 23 200 brutto", "vat 23 200 brutto"), "vat brutto").Contains("netto 162,6"), "gross input extracts the net");
        Check(UtilityToolbox.Process("vat 120 100", "vat 120 100")!.Contains("0–100"), "an impossible rate must be refused");

        // --- finance ---
        Check(Require(UtilityToolbox.Process("znizka 20 80", "znizka 20 80"), "discount").Contains("64,00"), "20% off 80 is 64,00");
        Check(Require(UtilityToolbox.Process("napiwek 50 10", "napiwek 50 10"), "tip").Contains("55,00"), "50 + 10% tip is 55,00");
        Check(Require(UtilityToolbox.Process("raty 10000 5 12", "raty 10000 5 12"), "loan").Contains("Rata równa"), "an installment estimate must be labeled");
        Check(Require(UtilityToolbox.Process("odsetki proste 1000 5 2", "odsetki proste 1000 5 2"), "interest").Contains("100,00"), "simple interest 1000·5%·2y");
        Check(Require(UtilityToolbox.Process("procent skladany 1000 5 10", "procent skladany 1000 5 10"), "compound").Contains("Procent składany"), "compound interest runs");
        Check(Require(UtilityToolbox.Process("procent skladany 1000 5 10", "procent skladany 1000 5 10"), "compound").Contains("1628,89"), "compound interest is a published number");

        // --- math beyond the calculator ---
        Check(Require(UtilityToolbox.Process("log 1000", "log 1000"), "log").Contains("log10(1000) = 3"), "decimal log");
        Check(Require(UtilityToolbox.Process("log 8 podstawie 2", "log 8 podstawie 2"), "log2").Contains("= 3"), "log base 2 of 8");
        Check(Require(UtilityToolbox.Process("potega 2 10", "potega 2 10"), "power").Contains("= 1024"), "2^10");
        Check(Require(UtilityToolbox.Process("modulo 17 5", "modulo 17 5"), "mod").Contains("= 2"), "17 mod 5");
        Check(Require(UtilityToolbox.Process("wartosc bezwzgledna -5", "wartosc bezwzgledna -5"), "abs").Contains("= 5"), "|-5|");
        Check(Require(UtilityToolbox.Process("sin 0", "sin 0"), "sin").Contains("="), "trigonometry runs");
        Check(Require(UtilityToolbox.Process("srednia wazona: 4 3 5 2", "srednia wazona: 4 3 5 2"), "weighted").Contains("Średnia ważona"), "weighted average beats the generic stats regex");
        Check(Require(UtilityToolbox.Process("srednia wazona: 4 3 5 2", "srednia wazona: 4 3 5 2"), "weighted").Contains("4,4"), "(4·3+5·2)/5 = 4,4");
        string quadratic = Require(UtilityToolbox.Process("rownanie 1 -3 2", "rownanie 1 -3 2"), "quadratic");
        Check(quadratic.Contains("x₁") && quadratic.Contains("x₂"), "quadratic finds both roots: " + quadratic);

        // --- calendar ---
        Check(Require(UtilityToolbox.Process("kalendarz 2 2024", "kalendarz 2 2024"), "calendar").Contains("29"), "February 2024 has 29 days");
        Check(Require(UtilityToolbox.Process("ile dni miedzy 01.01.2024 a 31.01.2024", "ile dni miedzy 01.01.2024 a 31.01.2024"), "between").Contains("30 dni"), "30 days between 1 and 31 January");
        Check(Require(UtilityToolbox.Process("rok przestepny 2024", "rok przestepny 2024"), "leap").Contains("JEST przestępny"), "2024 is a leap year");
        Check(Require(UtilityToolbox.Process("przestepny 2023", "przestepny 2023"), "leap2").Contains("NIE jest"), "2023 is not");
        Check(Require(UtilityToolbox.Process("ile dni do konca kwartalu", "ile dni do konca kwartalu"), "quarter").Contains("kwartał"), "quarter countdown");
        Check(Require(UtilityToolbox.Process("kwartal 15.02.2024", "kwartal 15.02.2024"), "quarter of").Contains("1 kwartał 2024"), "which quarter a date is in");

        // --- validators (local math only) ---
        Check(Require(UtilityToolbox.Process("ean 5901234123457", "ean 5901234123457"), "ean ok").Contains("jest POPRAWNY"), "a correct EAN-13 passes");
        Check(Require(UtilityToolbox.Process("ean 5901234123458", "ean 5901234123458"), "ean bad").Contains("NIEPOPRAWNY"), "a flipped check digit fails");
        Check(Require(UtilityToolbox.Process("isbn 83-246-0917-8", "isbn 83-246-0917-8"), "isbn").Contains("ISBN"), "ISBN-10 resolves");
        Check(Require(UtilityToolbox.Process("luhn 4111111111111111", "luhn 4111111111111111"), "luhn").Contains("Luhn"), "a Visa test number passes Luhn");
        Check(Require(UtilityToolbox.Process("regon 123456785", "regon 123456785"), "regon").Contains("REGON"), "REGON-9 resolves");

        // --- text ---
        string spelled = Require(UtilityToolbox.Process("literuj kot", "literuj kot"), "spell");
        Check(spelled.Contains("Literowanie") && spelled.Contains("kapelusz"), "spelling out letters: " + spelled);
        Check(Require(UtilityToolbox.Process("czestotliwosc slow kot kot pies", "czestotliwosc slow kot kot pies"), "freq").Contains("4 słów, 2 różnych"), "word frequency counts");
        Check(Require(UtilityToolbox.Process("skrable kot", "skrable kot"), "scrabble").Contains("5 pkt"), "kot is worth 5 scrabble points");
        Check(Require(UtilityToolbox.Process("posortuj slowa: c a b", "posortuj slowa: c a b"), "sort").Contains("a b c"), "words get sorted");
        Check(Require(UtilityToolbox.Process("bez powtorzen: ala ma kota ala", "bez powtorzen: ala ma kota ala"), "distinct").Contains("ala ma kota"), "duplicates are removed");
        Check(Require(UtilityToolbox.Process("odwroc slowa: Ala ma kota", "odwroc slowa: Ala ma kota"), "reverse words").Contains("kota ma Ala"), "word order flips");
        Check(Require(UtilityToolbox.Process("tylko cyfry: ab12cd34", "tylko cyfry: ab12cd34"), "digits").Contains("1234"), "digits are extracted");
        Check(Require(UtilityToolbox.Process("tylko litery: ab12cd", "tylko litery: ab12cd"), "letters").Contains("abcd"), "letters are extracted");

        // --- system (offline, kernel32 where available) ---
        string resolution = Require(UtilityToolbox.Process("rozdzielczosc ekranu", "rozdzielczosc ekranu"), "resolution");
        Check(resolution.Contains("Rozdzielczość") || resolution.Contains("niedostępny"), "screen resolution runs offline: " + resolution);
        string battery = Require(UtilityToolbox.Process("stan baterii", "stan baterii"), "battery").ToLowerInvariant();
        Check(battery.Contains("bateria") || battery.Contains("baterii") || battery.Contains("niedostępny"), "battery status runs offline: " + battery);
        Check(Require(UtilityToolbox.Process("strefa czasu", "strefa czasu"), "tz").Contains("UTC"), "local timezone");
        Check(Require(UtilityToolbox.Process("czas w strefie UTC+2", "czas w strefie UTC+2"), "offset").Contains("UTC+2"), "an offset clock beats the world clock regex");
        Check(Require(UtilityToolbox.Process("czas w londynie", "czas w londynie"), "london").Contains("Londyn"), "the locative city alias works");
        Check(Require(UtilityToolbox.Process("czas w atenach", "czas w atenach"), "athens").Contains("Ateny"), "another locative alias");

        // --- randomness ---
        string dice = Require(UtilityToolbox.Process("rzuc 3k6", "rzuc 3k6"), "dice");
        Check(dice.Contains("3k6") || dice.Contains("suma"), "polyhedral dice: " + dice);
        string card = Require(UtilityToolbox.Process("wylosuj karte", "wylosuj karte"), "card");
        Check(card.Contains("Karta"), "a playing card draws");

        // --- the toolbox must never steal conversations or side effects ---
        Check(UtilityToolbox.Process("napisz wiersz", "napisz wiersz") == null, "a poem request is not a tool call");
        Check(UtilityToolbox.Process("ile mam ramu", "ile mam ramu") == null, "system status is not a tool call");
        Check(UtilityToolbox.Process("zamknij notatnik", "zamknij notatnik") == null, "destruction is not a tool call");

        // ============================ 0.95 · Jarvis tools ============================
        // --- volume: honest answer whether or not the machine has audio ---
        string volume = Require(UtilityToolbox.Process("glosnosc", "glosnosc"), "volume");
        Check(volume.Contains("Głośność") || volume.Contains("Nie udało") || volume.Contains("niedostępne"),
            "volume reads or fails honestly: " + volume);
        string volumeSet = Require(UtilityToolbox.Process("glosnosc 40", "glosnosc 40"), "volume set");
        Check(volumeSet.Contains("40") || volumeSet.Contains("niedostępne") || volumeSet.Contains("Nie udało"),
            "volume set works or fails honestly: " + volumeSet);
        Check(UtilityToolbox.Process("glosnosc 400", "glosnosc 400")!.Contains("0–100"), "an impossible volume is refused");
        string mute = Require(UtilityToolbox.Process("wycisz", "wycisz"), "mute");
        Check(mute.Contains("wycisz") || mute.Contains("niedostępne"), "mute works or fails honestly: " + mute);

        // --- clipboard ---
        string clip = Require(UtilityToolbox.Process("kopiuj: spotkanie o 15:00", "kopiuj: spotkanie o 15:00"), "clipboard copy");
        Check(clip.Contains("Skopiowane") || clip.Contains("niedostępny"), "clipboard copy works or fails honestly: " + clip);
        string clipRead = Require(UtilityToolbox.Process("co w schowku", "co w schowku"), "clipboard read");
        Check(clipRead.Contains("Schowek") || clipRead.Contains("niedostępny"), "clipboard read works or fails honestly: " + clipRead);

        // --- screenshot ---
        string shot = Require(UtilityToolbox.Process("zrzut ekranu", "zrzut ekranu"), "screenshot");
        Check(shot.Contains("Zrzut ekranu zapisany") || shot.Contains("Nie udało"),
            "a screenshot saves or fails honestly: " + shot);

        return Task.CompletedTask;
    }
}
