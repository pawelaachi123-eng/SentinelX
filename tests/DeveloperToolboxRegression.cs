using System;
using System.Linq;
using System.Text;
using SentinelX.Core;

namespace SentinelX.Tests;

/// <summary>0.97 · narzędzia deweloperskie i produktywnościowe: wszystkie wyniki są czystymi
/// funkcjami (stałe skrótów i kodowań są wartościami publikowanymi), więc test nie zależy od maszyny.</summary>
internal static class DeveloperToolboxRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TEST FAILED: " + message);
    }

    private static string Require(string? value, string label) =>
        value ?? throw new InvalidOperationException(label + " was not handled as a tool");

    public static Task RunAsync(string directory)
    {
        System.IO.Directory.CreateDirectory(directory);

        // ---------------- diff ----------------
        string diff = DeveloperToolbox.LineDiff("ala\nma\nkota", "ala\nma\npsa");
        Check(diff.Contains("+1 dodane") && diff.Contains("-1 usunięte"), "diff liczy zmiany: " + diff.Replace(Environment.NewLine, " | "));
        Check(diff.Contains("+ psa") && diff.Contains("- kota"), "diff pokazuje wiersze dodane i usunięte");
        Check(DeveloperToolbox.LineDiff("ten sam", "ten sam").Contains("identyczne"), "identyczne teksty są rozpoznane");

        // ---------------- regex ----------------
        Check(DeveloperToolbox.RegexTest(@"\d+", "abc123def456").Contains("Trafienia: 2"), "regex liczy trafienia");
        Check(DeveloperToolbox.RegexTest(@"(\w)(\d)", "a1").Contains("grupa 1 = „a”"), "regex pokazuje grupy");
        Check(DeveloperToolbox.RegexTest("((", "x").Contains("niepoprawny"), "błędny wzorzec jest zgłaszany");

        // ---------------- semver ----------------
        Check(DeveloperToolbox.SemverCompare("1.2.3", "1.3.0").Contains("starsza"), "1.2.3 starsza niż 1.3.0");
        Check(DeveloperToolbox.SemverCompare("2.0.0", "1.9.9").Contains("nowsza"), "2.0.0 nowsza niż 1.9.9");
        Check(DeveloperToolbox.SemverCompare("1.2.3", "1.2.3").Contains("równe"), "równe wersje");
        Check(DeveloperToolbox.SemverBump("1.2.3", "minor").Contains("1.3.0"), "podbicie minor");
        Check(DeveloperToolbox.SemverBump("1.2.3", "major").Contains("2.0.0"), "podbicie major");
        Check(DeveloperToolbox.SemverBump("nie-wersja", "patch").Contains("Nie rozumiem"), "zła wersja jest odrzucana");

        // ---------------- IP i podsieci ----------------
        string subnet = DeveloperToolbox.IpDescribe("192.168.1.10/24");
        Check(subnet.Contains("192.168.1.0") && subnet.Contains("192.168.1.255"), "sieć i rozgłoszenie /24");
        Check(subnet.Contains("użytecznych: 254"), "liczba hostów /24: " + subnet.Replace(Environment.NewLine, " | "));
        Check(subnet.Contains("prywatny"), "zakres prywatny rozpoznany");
        Check(DeveloperToolbox.IpDescribe("8.8.8.8").Contains("publiczny"), "adres publiczny rozpoznany");
        Check(DeveloperToolbox.IpDescribe("1.2.3").Contains("nie jest poprawny"), "błędny adres jest odrzucany");
        Check(DeveloperToolbox.SubnetSplit("10.0.0.0/24", 4).Contains("10.0.0.64/26"), "podział /24 na 4 podsieci /26");
        Check(DeveloperToolbox.SubnetSplit("10.0.0.0/24", 3).Contains("potęgą dwójki"), "niepotęgowy podział jest odrzucany");

        // ---------------- JWT ----------------
        string header = Base64Url("{\"alg\":\"HS256\",\"typ\":\"JWT\"}");
        long future = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        string validToken = header + "." + Base64Url("{\"sub\":\"ala\",\"exp\":" + future + "}") + ".podpis";
        string jwt = DeveloperToolbox.JwtDecode(validToken);
        Check(jwt.Contains("\"sub\": \"ala\""), "ładunek JWT jest czytelny: " + jwt.Replace(Environment.NewLine, " | "));
        Check(jwt.Contains("jeszcze ważny"), "ważność tokenu jest sprawdzana");
        long past = DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds();
        Check(DeveloperToolbox.JwtDecode(header + "." + Base64Url("{\"exp\":" + past + "}") + ".x").Contains("WYGASŁ"), "wygasły token jest oznaczony");
        Check(DeveloperToolbox.JwtDecode("to-nie-jwt").Contains("nie wygląda na JWT"), "śmieci nie są traktowane jak JWT");

        // ---------------- identyfikatory ----------------
        string uuidLine = DeveloperToolbox.UuidV7(new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero)).Split(Environment.NewLine)[0];
        Check(System.Text.RegularExpressions.Regex.IsMatch(uuidLine, "^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$"),
            "UUID v7 ma wersję 7 i wariant RFC: " + uuidLine);
        string ulid = DeveloperToolbox.UlidNow().Split(Environment.NewLine)[0];
        Check(ulid.Length == 26 && ulid.All(x => "0123456789ABCDEFGHJKMNPQRSTVWXYZ".Contains(x)), "ULID: 26 znaków Crockford: " + ulid);

        // ---------------- generatory kodu ----------------
        string csharp = DeveloperToolbox.JsonToCSharp("{\"id\":1,\"name\":\"Ala\",\"tags\":[\"x\"],\"meta\":{\"active\":true}}");
        Check(csharp.Contains("public sealed record Root"), "rekord główny z JSON");
        Check(csharp.Contains("public long Id { get; init; }"), "liczba całkowita mapuje się na long: " + csharp);
        Check(csharp.Contains("List<string> Tags"), "tablica tekstów mapuje się na listę");
        Check(csharp.Contains("public Meta? Meta"), "zagnieżdżony obiekt mapuje się na własny rekord");
        Check(DeveloperToolbox.JsonToCSharp("{zły").Contains("nie jest poprawny"), "błędny JSON jest odrzucany");

        string table = DeveloperToolbox.SqlTable("users | id: int pk, name: text not null, ocena: float, tag: nierozpoznany, opis");
        Check(table.Contains("CREATE TABLE IF NOT EXISTS users"), "CREATE TABLE powstaje");
        Check(table.Contains("id INTEGER PRIMARY KEY"), "klucz główny: " + table);
        Check(table.Contains("name TEXT NOT NULL"), "ograniczenie NOT NULL");
        Check(table.Contains("pominięte (brak typu)"), "kolumna bez typu jest jawnie pominięta");
        Check(table.Contains("nieznany typ"), "nieznany typ jest zgłaszany, a nie przemilczany");
        Check(table.Contains("tag TEXT"), "nieznany typ dostaje TEXT z ostrzeżeniem");
        Check(DeveloperToolbox.SqlTable("zły;drop | id: int").Contains("Tylko litery"), "wstrzyknięcie w nazwie tabeli jest odrzucane");
        string insert = DeveloperToolbox.SqlFromJson("{\"id\":1,\"name\":\"Ala\",\"ok\":true}", "osoby");
        Check(insert.Contains("INSERT INTO osoby") && insert.Contains("'Ala'"), "INSERT z wartości JSON");
        Check(insert.Contains(") VALUES (1, 'Ala', 1);"), "wartości liczbowe bez cudzysłowów, teksty w apostrofach");

        string mock = DeveloperToolbox.JsonMock("{name:text, age:int, tags:[text]}");
        Check(mock.Contains("\"name\": \"przyklad name\"") && mock.Contains("\"age\": 2") && mock.Contains("\"tags\": ["), "mock JSON z schematu: " + mock.Replace(Environment.NewLine, " "));
        Check(mock.Contains("Nie są prawdziwe"), "mock uczciwie opisuje pochodzenie danych");

        // ---------------- kodowania i skróty ----------------
        Check(DeveloperToolbox.Base32Encode(Encoding.UTF8.GetBytes("abc")) == "MFRGG===", "Base32 „abc” = MFRGG===");
        Check(DeveloperToolbox.Base32Decode("MFRGG===") == "Zdekodowane (3 B): abc", "Base32 w obie strony: " + DeveloperToolbox.Base32Decode("MFRGG==="));
        Check(DeveloperToolbox.Base32Decode("1111").Contains("nie należy do alfabetu"), "cyfry 0/1 nie są w alfabecie Base32");
        Check(DeveloperToolbox.Base58Encode(Encoding.UTF8.GetBytes("abc")) == "ZiCa", "Base58 „abc” = ZiCa");
        Check(DeveloperToolbox.Base58Decode("ZiCa") == "Zdekodowane (3 B): abc", "Base58 w obie strony");
        Check(DeveloperToolbox.Base58Decode("0OIl").Contains("nie należy do alfabetu"), "0, O, I, l nie występują w Base58");
        Check(DeveloperToolbox.HashWith("md5", "abc").Contains("900150983cd24fb0d6963f7d28e17f72"), "MD5 „abc” to znana stała");
        Check(DeveloperToolbox.HashWith("sha1", "abc").Contains("a9993e364706816aba3e25717850c26c9cd0d89d"), "SHA-1 „abc” to znana stała");
        Check(DeveloperToolbox.HashWith("sha512", "abc").Contains("ddaf35a193617aba"), "SHA-512 „abc”");
        Check(DeveloperToolbox.HashWith("crc32", "123456789") == "CRC32 (UTF-8, 9 B): cbf43926", "CRC32 „123456789” = cbf43926");
        Check(DeveloperToolbox.Crc32(Encoding.UTF8.GetBytes("123456789")) == 0xCBF43926, "CRC32 zwraca stałą IEEE");
        Check(DeveloperToolbox.Codepoints("A").Contains("U+0041"), "kod znaku A");
        Check(DeveloperToolbox.FromCodepoints("41 42") == "Z kodów: AB", "odczyt z kodów szesnastkowych");

        // ---------------- CSV / JSON / Markdown ----------------
        string markdown = DeveloperToolbox.CsvToMarkdown("a,b\n1,2");
        Check(markdown.Contains("| a | b |") && markdown.Contains("| 1 | 2 |"), "CSV → tabela Markdown: " + markdown.Replace(Environment.NewLine, " "));
        Check(DeveloperToolbox.CsvToJson("a,b\n1,2").Contains("\"a\": \"1\""), "CSV → JSON");
        string csv = DeveloperToolbox.JsonToCsv("[{\"a\":1,\"b\":\"x,y\"}]");
        Check(csv.Contains("a,b") && csv.Contains("\"x,y\""), "JSON → CSV z ucieczką przecinka: " + csv);
        Check(DeveloperToolbox.MarkdownToc("# Tytuł\n## Sekcja").Contains("- [Tytuł](#tytuł)"), "spis treści z kotwicami");
        Check(DeveloperToolbox.MarkdownToc("bez nagłówków").Contains("Nie znalazłem"), "brak nagłówków jest zgłaszany");

        // ---------------- walidatory i konwencje ----------------
        Check(DeveloperToolbox.Validate("ala@example.com").Contains("E-mail"), "walidacja e-maila");
        Check(DeveloperToolbox.Validate("https://example.com/x").Contains("URL"), "walidacja URL");
        Check(DeveloperToolbox.Validate("10.0.0.1").Contains("Adres"), "walidacja adresu IP");
        Check(DeveloperToolbox.Validate("nonsens").Contains("Nie rozpoznaję"), "nierozpoznana wartość nie jest zgadywana");
        string naming = DeveloperToolbox.Naming("snake", "mojaNowaKlasa");
        Check(naming.Contains("camelCase: mojaNowaKlasa"), "camelCase: " + naming.Replace(Environment.NewLine, " | "));
        Check(naming.Contains("snake_case: moja_nowa_klasa"), "snake_case dzieli camelCase");
        Check(naming.Contains("kebab-case: moja-nowa-klasa"), "kebab-case");
        Check(naming.Contains("STAŁA: MOJA_NOWA_KLASA"), "stała");
        Check(DeveloperToolbox.CommitMessage("feat(core): dodalem rdzen").Contains("Commit poprawny konwencjonalnie"), "poprawny commit");
        Check(DeveloperToolbox.CommitMessage("dodaj eksport csv").Contains("feat: dodaj eksport"), "commit z opisu");
        Check(DeveloperToolbox.CommitMessage("feat: " + new string('x', 90)).Contains("maks. 72"), "długi opis jest sygnalizowany");

        // ---------------- szablony ----------------
        Check(DeveloperToolbox.Template("dockerfile-dotnet").Contains("FROM mcr.microsoft.com/dotnet/sdk:10.0"), "szablon Dockerfile .NET");
        Check(DeveloperToolbox.Template("compose").Contains("services:"), "szablon compose");
        Check(DeveloperToolbox.TemplateList().Contains("github-actions"), "lista szablonów");
        Check(DeveloperToolbox.Template("nie-ma-takiego").Contains("Nie mam szablonu"), "nieznany szablon jest zgłaszany");

        // ---------------- tokeny i kontekst ----------------
        Check(DeveloperToolbox.TokenEstimate(new string('a', 360)).Contains("~100 tokenów"), "szacunek tokenów: " + DeveloperToolbox.TokenEstimate(new string('a', 360)).Replace(Environment.NewLine, " | "));
        Check(DeveloperToolbox.ContextBudget(8192, "krótki tekst").Contains("mieści się"), "krótka treść mieści się w oknie");
        Check(DeveloperToolbox.ContextBudget(512, new string('a', 5000)).Contains("NIE zmieści się"), "zbyt długa treść jest zgłaszana");

        // ---------------- finanse i produktywność ----------------
        Check(ProductivityToolbox.NumberToPolishWords("1234").Contains("tysiąc dwieście trzydzieści cztery"),
            "słownie 1234: " + ProductivityToolbox.NumberToPolishWords("1234").Replace(Environment.NewLine, " | "));
        Check(ProductivityToolbox.NumberToPolishWords("1000000").Contains("milion"), "słownie 1 000 000");
        Check(ProductivityToolbox.NumberToPolishWords("2000").Contains("dwa tysiące"), "odmiana: dwa tysiące");
        Check(ProductivityToolbox.NumberToPolishWords("5000").Contains("pięć tysięcy"), "odmiana: pięć tysięcy");
        Check(ProductivityToolbox.NumberToPolishWords("21").Contains("dwadzieścia jeden"), "słownie 21");
        Check(ProductivityToolbox.NumberToPolishWords("1234,56").Contains("56/100"), "grosze w zapisie słownym");
        Check(ProductivityToolbox.NumberToPolishWords("abc").Contains("Podaj liczbę"), "nierozpoznana liczba jest odrzucana");
        Check(ProductivityToolbox.Lorem(10).Contains("10 słów"), "lorem z licznikiem słów");
        Check(ProductivityToolbox.TextStatistics("ala ma kota").Contains("słów: 3"), "statystyki tekstu liczą słowa");
        Check(ProductivityToolbox.TextStatistics("ala ma kota").Contains("unikalnych: 3"), "statystyki tekstu liczą unikalne słowa");
        Check(ProductivityToolbox.TextStatistics("").Contains("Podaj tekst"), "pusty tekst jest zgłaszany");
        Check(ProductivityToolbox.Roi("2000", "15000").Contains("13,33%"), "ROI: " + ProductivityToolbox.Roi("2000", "15000").Replace(Environment.NewLine, " | "));
        Check(ProductivityToolbox.Roi("100", "0").Contains("nie może być zerem"), "koszt zerowy jest odrzucany");
        Check(ProductivityToolbox.BreakEven("100", "40", "12000").Contains("200 sztuk"), "próg rentowności 200 sztuk");
        Check(ProductivityToolbox.BreakEven("30", "40", "12000").Contains("nie istnieje"), "ujemna marża nie ma progu rentowności");
        Check(ProductivityToolbox.Depreciation("12000", "4").Contains("odpis roczny"), "amortyzacja liniowa");
        Check(ProductivityToolbox.Depreciation("12000", "0").Contains("1–50"), "zakres lat jest walidowany");
        Check(ProductivityToolbox.Inflation("1000", "5", "10").Contains("613,9"), "siła nabywcza po inflacji: " + ProductivityToolbox.Inflation("1000", "5", "10").Replace(Environment.NewLine, " | "));
        Check(ProductivityToolbox.Savings("500", "0", "1").Contains("wpłacone razem"), "plan wypisuje sumę wpłat");
        Check(ProductivityToolbox.Savings("500", "0", "1").Contains("odsetki: 0,00"), "zerowe odsetki są policzone jawnie");
        Check(ProductivityToolbox.Goal("20000", "800").Contains("potrzebne miesiące: 25"), "cel 20 000 przy 800/mies. to 25 miesięcy");
        Check(ProductivityToolbox.Goal("20000", "0").Contains("nigdy"), "zerowa wpłata nie osiągnie celu");
        Check(ProductivityToolbox.Budget5030_20("6000").Contains("(50%)") && ProductivityToolbox.Budget5030_20("6000").Contains("(20%)"), "budżet 50/30/20");
        Check(ProductivityToolbox.Eisenhower("wazne pilne oddac raport").Contains("1 — zrób teraz"), "macierz: ważne i pilne");
        Check(ProductivityToolbox.Eisenhower("nieważne").Contains("4 — odłóż"), "macierz: nieważne i niepilne");
        Check(ProductivityToolbox.Slides("# Tytuł\n## Punkt\n## Drugi").Contains("slajdów: 3"), "generator slajdów");

        // ---------------- dispatch przez router poleceń ----------------
        foreach (string command in new[]
        {
            "diff: ala | ola", "regex: \\d+ | abc123", "semver: 1.2.3 vs 1.3.0", "ip: 10.0.0.1/8", "jwt: a.b",
            "uuid7", "ulid", "md5: abc", "base32: abc", "kody znakow: Ala", "spis tresci: # T",
            "szablon: makefile", "tokeny: tekst", "commit: fix blad", "mock json: {a:text}",
            "tabela z csv: a,b\\n1,2", "sql tabela: t | id: int"
        })
        {
            Check(Require(DeveloperToolbox.TryHandle(command, CommandText.Normalize(command)), "polecenie „" + command + "”").Length > 5,
                "polecenie deweloperskie odpowiada: " + command);
        }

        foreach (string command in new[] { "lorem 5", "liczba slownie: 7", "roi: 100 200", "macierz: pilne kupic", "slajdy: # A", "cel: 2000 100" })
        {
            Check(Require(ProductivityToolbox.TryHandle(command, CommandText.Normalize(command)), "polecenie „" + command + "”").Length > 5,
                "polecenie produktywności odpowiada: " + command);
        }
        Check(DeveloperToolbox.TryHandle("zwykłe pytanie do modelu", CommandText.Normalize("zwykłe pytanie do modelu")) is null,
            "obcy tekst nie jest przechwytywany przez narzędzia");

        return Task.CompletedTask;
    }

    private static string Base64Url(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
