using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>
/// SEKCJA 4–7 — narzędzia deweloperskie bez sieci i bez zgadywania. Każda funkcja jest czysta
/// (te same dane wejściowe = ten sam wynik), więc można ją sprawdzić testem bez komputera.
/// <para>Zakres: porównanie tekstów, wyrażenia regularne, wersje semantyczne, adresy i podsieci,
/// JWT, identyfikatory (UUID v7, ULID), generatory kodu (C#, SQL, mocki JSON), kodowania
/// (Base32/Base58/ASCII), skróty (MD5/SHA-1/SHA-256/SHA-512/CRC32), CSV/JSON/Markdown,
/// walidatory, konwencje nazw, szablony plików konfiguracyjnych i szacunek budżetu kontekstu.</para>
/// </summary>
public static class DeveloperToolbox
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    /// <summary>Zwraca odpowiedź, gdy polecenie jest jednym z narzędzi; null = nie moje.</summary>
    public static string? TryHandle(string command, string text)
    {
        string raw = command ?? "";

        // ---------------- diff / porównanie tekstów ----------------
        var diff = Regex.Match(text, @"^(?:diff|porownaj teksty|porownaj tekst)[:\s]+(.+)$", RegexOptions.Singleline);
        if (diff.Success)
        {
            var (left, right) = SplitPayload(Payload(raw, "diff", "porownaj teksty", "porownaj tekst"), "---");
            if (left.Length == 0 && right.Length == 0) return "Użyj: „diff: pierwszy tekst | drugi tekst” (albo rozdziel linią „---”).";
            return LineDiff(left, right);
        }

        // ---------------- wyrażenia regularne ----------------
        var regexTest = Regex.Match(text, @"^(?:regex|regex test|wyrazenie regularne)[:\s]+(.+)$", RegexOptions.Singleline);
        if (regexTest.Success)
        {
            var (pattern, subject) = SplitPayload(Payload(raw, "regex test", "wyrazenie regularne", "regex"), "|");
            if (pattern.Length == 0) return "Użyj: „regex: wzorzec | tekst”.";
            if (subject.Length == 0) return "Podaj tekst do sprawdzenia po pionowej kresce: „regex: \\d+ | abc123”.";
            return RegexTest(pattern, subject);
        }

        // ---------------- wersje semantyczne ----------------
        var semverBump = Regex.Match(text, @"^semver (?:podbij|bump)[:\s]+(.+)$");
        if (semverBump.Success)
        {
            string payload = Payload(raw, "semver podbij", "semver bump");
            var parts = Regex.Match(payload, @"^(major|minor|patch)\s*[:=]?\s*(.+)$", RegexOptions.IgnoreCase);
            if (!parts.Success) return "Użyj: „semver podbij minor: 1.2.3” (major, minor albo patch).";
            return SemverBump(parts.Groups[2].Value, parts.Groups[1].Value);
        }
        var semverCompare = Regex.Match(text, @"^semver[:\s]+(.+)$");
        if (semverCompare.Success)
        {
            string payload = Payload(raw, "semver");
            string[] parts = Regex.Split(payload, @"\s+(?:vs|i|a|od|->|>|<)\s+");
            if (parts.Length == 2) return SemverCompare(parts[0].Trim(), parts[1].Trim());
            return "Użyj: „semver: 1.2.3 vs 1.3.0” albo „semver podbij minor: 1.2.3”.";
        }

        // ---------------- adresy IP i podsieci ----------------
        var ipInfo = Regex.Match(text, @"^(?:ip|adres ip|ip oblicz)[:\s]+(.+)$");
        if (ipInfo.Success) return IpDescribe(Payload(raw, "adres ip", "ip oblicz", "ip").Trim());

        var subnet = Regex.Match(text, @"^(?:podsiec|ip podziel|podziel siec)[:\s]+(.+)$");
        if (subnet.Success)
        {
            string payload = Payload(raw, "podziel siec", "ip podziel", "podsiec");
            var match = Regex.Match(payload, @"^(\d{1,3}(?:\.\d{1,3}){3}(?:/\d{1,2})?)\s*(?:na|/)\s*(\d{1,2})$");
            if (!match.Success) return "Użyj: „podsiec: 10.0.0.0/24 na 4”.";
            if (!int.TryParse(match.Groups[2].Value, out int parts) || parts is < 2 or > 64) return "Liczba podsieci musi być z zakresu 2–64.";
            return SubnetSplit(match.Groups[1].Value, parts);
        }

        // ---------------- JWT ----------------
        var jwt = Regex.Match(text, @"^jwt[:\s]+(.+)$");
        if (jwt.Success) return JwtDecode(Payload(raw, "jwt").Trim());

        // ---------------- identyfikatory ----------------
        if (text is "uuid7" or "uuid v7" or "ulid" or "ulid nowy")
            return text.StartsWith("ulid", StringComparison.Ordinal) ? UlidNow() : UuidV7(DateTimeOffset.Now);

        // ---------------- kod / SQL / mocki ----------------
        var jsonToCs = Regex.Match(text, @"^(?:json csharp|json klasa|json do csharp|model csharp)[:\s]+(.+)$", RegexOptions.Singleline);
        if (jsonToCs.Success) return JsonToCSharp(Payload(raw, "json do csharp", "json klasa", "json csharp", "model csharp"));

        var sqlTable = Regex.Match(text, @"^sql tabela[:\s]+(.+)$");
        if (sqlTable.Success) return SqlTable(Payload(raw, "sql tabela"));

        var sqlJson = Regex.Match(text, @"^sql z json[:\s]+(.+)$", RegexOptions.Singleline);
        if (sqlJson.Success) return SqlFromJson(Payload(raw, "sql z json"), "tabela");

        var mock = Regex.Match(text, @"^(?:mock json|json mock|przyklad json)[:\s]+(.+)$", RegexOptions.Singleline);
        if (mock.Success) return JsonMock(Payload(raw, "przyklad json", "mock json", "json mock"));

        // ---------------- kodowania ----------------
        var base32 = Regex.Match(text, @"^base32[:\s]+(.+)$");
        if (base32.Success) return "Base32 (RFC 4648): " + Base32Encode(Encoding.UTF8.GetBytes(Payload(raw, "base32")));
        var base32Decode = Regex.Match(text, @"^dekoduj base32[:\s]+(.+)$");
        if (base32Decode.Success) return Base32Decode(Payload(raw, "dekoduj base32"));
        var base58 = Regex.Match(text, @"^base58[:\s]+(.+)$");
        if (base58.Success) return "Base58 (alfabet Bitcoin): " + Base58Encode(Encoding.UTF8.GetBytes(Payload(raw, "base58")));
        var base58Decode = Regex.Match(text, @"^dekoduj base58[:\s]+(.+)$");
        if (base58Decode.Success) return Base58Decode(Payload(raw, "dekoduj base58"));

        var hash = Regex.Match(text, @"^(md5|sha1|sha512|sha384|crc32)[:\s]+(.+)$", RegexOptions.Singleline);
        if (hash.Success) return HashWith(hash.Groups[1].Value, Payload(raw, hash.Groups[1].Value));

        var asciiCode = Regex.Match(text, @"^(?:ascii kod|kod ascii|kody znakow|unicode kod)[:\s]+(.+)$", RegexOptions.Singleline);
        if (asciiCode.Success) return Codepoints(Payload(raw, "kody znakow", "unicode kod", "ascii kod", "kod ascii"));

        var fromAscii = Regex.Match(text, @"^z ascii[:\s]+(.+)$");
        if (fromAscii.Success) return FromCodepoints(Payload(raw, "z ascii"));

        // ---------------- CSV / JSON / Markdown ----------------
        var csvMarkdown = Regex.Match(text, @"^(?:csv markdown|csv tabela|tabela z csv)[:\s]+(.+)$", RegexOptions.Singleline);
        if (csvMarkdown.Success) return CsvToMarkdown(Payload(raw, "tabela z csv", "csv markdown", "csv tabela"));

        var csvJson = Regex.Match(text, @"^(?:csv json|csv do json)[:\s]+(.+)$", RegexOptions.Singleline);
        if (csvJson.Success) return CsvToJson(Payload(raw, "csv do json", "csv json"));

        var jsonCsv = Regex.Match(text, @"^(?:json csv|json do csv)[:\s]+(.+)$", RegexOptions.Singleline);
        if (jsonCsv.Success) return JsonToCsv(Payload(raw, "json do csv", "json csv"));

        var toc = Regex.Match(text, @"^(?:markdown spis|spis tresci|toc)[:\s]+(.+)$", RegexOptions.Singleline);
        if (toc.Success) return MarkdownToc(Payload(raw, "spis tresci", "markdown spis", "toc"));

        // ---------------- walidatory ----------------
        var check = Regex.Match(text, @"^(?:czy|sprawdz czy|waliduj)[:\s]+(.+)$", RegexOptions.Singleline);
        if (check.Success) return Validate(Payload(raw, "sprawdz czy", "waliduj", "czy"));

        // ---------------- konwencje nazw ----------------
        var naming = Regex.Match(text, @"^(camel|snake|kebab|pascal|stala|zmienna|nazwa zmiennej)[:\s]+(.+)$", RegexOptions.Singleline);
        if (naming.Success) return Naming(naming.Groups[1].Value, Payload(raw, naming.Groups[1].Value));

        // ---------------- commit ----------------
        var commit = Regex.Match(text, @"^(?:commit|commit sprawdz)[:\s]+(.+)$", RegexOptions.Singleline);
        if (commit.Success) return CommitMessage(Payload(raw, "commit sprawdz", "commit"));

        // ---------------- szablony ----------------
        if (text is "szablony" or "lista szablonow" or "szablony plikow") return TemplateList();
        var template = Regex.Match(text, @"^(?:szablon|szablon pliku|wygeneruj plik)[:\s]+(.+)$");
        if (template.Success) return Template(Payload(raw, "szablon pliku", "wygeneruj plik", "szablon"));

        // ---------------- tokeny i kontekst ----------------
        var tokens = Regex.Match(text, @"^(?:tokeny|policz tokeny|tokeny tekstu)[:\s]+(.+)$", RegexOptions.Singleline);
        if (tokens.Success) return TokenEstimate(Payload(raw, "policz tokeny", "tokeny tekstu", "tokeny"));

        var context = Regex.Match(text, @"^(?:kontekst|budzet kontekstu|okno kontekstu)[:\s]+(.+)$", RegexOptions.Singleline);
        if (context.Success)
        {
            var (window, content) = SplitPayload(Payload(raw, "budzet kontekstu", "okno kontekstu", "kontekst"), "|");
            if (content.Length == 0) return "Użyj: „kontekst: 8192 | tekst” (rozmiar okna | treść).";
            if (!int.TryParse(window.Trim(), out int size) || size is < 128 or > 4_000_000) return "Rozmiar okna to liczba tokenów z zakresu 128–4000000.";
            return ContextBudget(size, content);
        }

        return null;
    }

    // ============================================================
    // Pomocnicze
    // ============================================================

    private static string Payload(string raw, params string[] prefixes)
    {
        string text = (raw ?? "").Trim();
        foreach (string prefix in prefixes)
        {
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            string rest = text[prefix.Length..];
            rest = rest.TrimStart();
            if (rest.StartsWith(':')) rest = rest[1..];
            return rest.Trim();
        }
        return text;
    }

    /// <summary>Dzieli treść na dwie części: po pionowej kresce albo po linii „---”.</summary>
    internal static (string Left, string Right) SplitPayload(string payload, string separator)
    {
        string text = payload ?? "";
        if (separator == "|")
        {
            int index = text.IndexOf('|');
            if (index < 0) return (text.Trim(), "");
            return (text[..index].Trim(), text[(index + 1)..].Trim());
        }
        var lines = text.Replace("\r\n", "\n").Split('\n');
        int marker = Array.FindIndex(lines, x => x.Trim() == "---");
        if (marker < 0) return (text.Trim(), "");
        return (string.Join("\n", lines.Take(marker)).Trim(), string.Join("\n", lines.Skip(marker + 1)).Trim());
    }

    // ============================================================
    // Diff
    // ============================================================

    /// <summary>Porównanie wiersz po wierszu (algorytm LCS). Limity: 600 linii i 2000 znaków na linię.</summary>
    public static string LineDiff(string left, string right)
    {
        string[] a = Normalize(left).Take(600).ToArray();
        string[] b = Normalize(right).Take(600).ToArray();
        if (a.Length == 0 && b.Length == 0) return "Nie ma czego porównywać — oba teksty są puste.";
        var table = new int[a.Length + 1, b.Length + 1];
        for (int i = a.Length - 1; i >= 0; i--)
            for (int j = b.Length - 1; j >= 0; j--)
                table[i, j] = a[i] == b[j] ? table[i + 1, j + 1] + 1 : Math.Max(table[i + 1, j], table[i, j + 1]);

        var lines = new List<string>();
        int added = 0, removed = 0, same = 0;
        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y]) { lines.Add("  " + Trim(a[x])); same++; x++; y++; }
            else if (table[x + 1, y] >= table[x, y + 1]) { lines.Add("- " + Trim(a[x])); removed++; x++; }
            else { lines.Add("+ " + Trim(b[y])); added++; y++; }
        }
        while (x < a.Length) { lines.Add("- " + Trim(a[x])); removed++; x++; }
        while (y < b.Length) { lines.Add("+ " + Trim(b[y])); added++; y++; }

        if (added == 0 && removed == 0) return "Teksty są identyczne (" + same + " linii, bez różnic).";
        var header = new List<string>
        {
            "Różnice: +" + added + " dodane · -" + removed + " usunięte · " + same + " bez zmian" +
            ((left.Length > 0 ? Normalize(left).Length : 0) > 600 ? " (porównałem pierwsze 600 linii)" : "")
        };
        header.AddRange(lines.Take(200));
        if (lines.Count > 200) header.Add("… i " + (lines.Count - 200) + " dalszych linii.");
        return string.Join(Environment.NewLine, header);
    }

    private static string[] Normalize(string text) => (text ?? "").Replace("\r\n", "\n").Split('\n');
    private static string Trim(string line) => line.Length <= 2000 ? line : line[..2000] + "…";

    // ============================================================
    // Regex
    // ============================================================

    /// <summary>Test wyrażenia regularnego z limitem czasu 1 s (katastrofalne wzorce nie zamrażają aplikacji).</summary>
    public static string RegexTest(string pattern, string subject)
    {
        try
        {
            var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
            var matches = regex.Matches(subject);
            if (matches.Count == 0) return "Wzorzec „" + pattern + "” nie pasuje do podanego tekstu (0 trafień).";
            var lines = new List<string> { "Trafienia: " + matches.Count + " · wzorzec „" + pattern + "”" };
            foreach (Match match in matches.Take(20))
            {
                lines.Add("· poz. " + match.Index + ": „" + match.Value + "”");
                for (int group = 1; group < match.Groups.Count; group++)
                    lines.Add("    grupa " + group + " = " + (match.Groups[group].Success ? "„" + match.Groups[group].Value + "”" : "(brak)"));
            }
            if (matches.Count > 20) lines.Add("… i " + (matches.Count - 20) + " więcej trafień.");
            return string.Join(Environment.NewLine, lines);
        }
        catch (ArgumentException ex)
        {
            return "Wzorzec jest niepoprawny: " + ex.Message;
        }
        catch (RegexMatchTimeoutException)
        {
            return "Przerwałem po 1 sekundzie — wzorzec jest zbyt kosztowny (to zabezpieczenie, nie błąd tekstu).";
        }
    }

    // ============================================================
    // Semver
    // ============================================================

    public static string SemverCompare(string first, string second)
    {
        if (!TrySemver(first, out var a, out string errorA)) return "Nie rozumiem wersji „" + first + "”: " + errorA;
        if (!TrySemver(second, out var b, out string errorB)) return "Nie rozumiem wersji „" + second + "”: " + errorB;
        int result = CompareSemver(a, b);
        string verdict = result == 0 ? "równe" : result < 0 ? first + " jest starsza" : first + " jest nowsza";
        return "Semver: " + a + " vs " + b + " → " + verdict +
            (result == 0 ? "" : " · " + (a.Major != b.Major ? "zmiana główna (major)" : a.Minor != b.Minor ? "zmiana poboczna (minor)" : "poprawka (patch)"));
    }

    public static string SemverBump(string version, string part, string? extra = null)
    {
        if (!TrySemver(version, out var parsed, out string error)) return "Nie rozumiem wersji „" + version + "”: " + error;
        string kind = part.Trim().ToLowerInvariant();
        (int Major, int Minor, int Patch) bumped = kind switch
        {
            "major" => (parsed.Major + 1, 0, 0),
            "minor" => (parsed.Major, parsed.Minor + 1, 0),
            "patch" => (parsed.Major, parsed.Minor, parsed.Patch + 1),
            _ => (-1, -1, -1)
        };
        if (bumped.Major < 0) return "Podbij „major”, „minor” albo „patch”.";
        string label = extra is { Length: > 0 } ? "-" + extra.Trim().TrimStart('-') : "";
        return "Nowa wersja: " + bumped.Major + "." + bumped.Minor + "." + bumped.Patch + label +
            " (było " + parsed + ", podbito " + kind + ").";
    }

    private static bool TrySemver(string text, out (int Major, int Minor, int Patch) version, out string error)
    {
        version = (0, 0, 0);
        error = "";
        var match = Regex.Match((text ?? "").Trim().TrimStart('v'), @"^(\d{1,6})\.(\d{1,6})\.(\d{1,6})");
        if (!match.Success) { error = "oczekuję postaci 1.2.3"; return false; }
        version = (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));
        return true;
    }

    private static int CompareSemver((int Major, int Minor, int Patch) a, (int Major, int Minor, int Patch) b)
    {
        if (a.Major != b.Major) return a.Major.CompareTo(b.Major);
        if (a.Minor != b.Minor) return a.Minor.CompareTo(b.Minor);
        return a.Patch.CompareTo(b.Patch);
    }

    // ============================================================
    // IP i podsieci
    // ============================================================

    /// <summary>Opis adresu IPv4 (z maską lub bez): sieć, rozgłoszenie, zakres hostów, liczba adresów.</summary>
    public static string IpDescribe(string payload)
    {
        string text = (payload ?? "").Trim().Trim('"');
        if (text.Length == 0) return "Użyj: „ip: 192.168.1.10/24”.";
        string addressPart = text;
        int prefix = -1;
        int slash = text.IndexOf('/');
        if (slash >= 0)
        {
            addressPart = text[..slash];
            if (!int.TryParse(text[(slash + 1)..].Trim(), out prefix) || prefix is < 0 or > 32)
                return "Prefiks maski musi być liczbą 0–32.";
        }
        if (!TryIpv4(addressPart, out uint address)) return "„" + addressPart + "” to nie jest poprawny adres IPv4.";
        if (prefix < 0)
        {
            string className = address switch
            {
                < 0x80000000 => "A",
                < 0xC0000000 => "B",
                < 0xE0000000 => "C",
                < 0xF0000000 => "D (multicast)",
                _ => "E (zarezerwowana)"
            };
            bool isPrivate = IsPrivate(address);
            return "Adres " + ToIpv4(address) + " · klasa " + className + (isPrivate ? " · zakres prywatny" : " · zakres publiczny") +
                Environment.NewLine + "Podaj maskę, np. „ip: " + ToIpv4(address) + "/24”, żeby zobaczyć sieć, rozgłoszenie i liczbę hostów.";
        }
        uint mask = prefix == 0 ? 0u : 0xFFFFFFFFu << (32 - prefix);
        uint network = address & mask;
        uint broadcast = network | ~mask;
        uint hosts = prefix >= 31 ? 0 : (1u << (32 - prefix)) - 2;
        var lines = new List<string>
        {
            "Adres: " + ToIpv4(address) + "/" + prefix,
            "· maska: " + ToIpv4(mask) + " (" + prefix + " bitów)",
            "· sieć: " + ToIpv4(network),
            "· rozgłoszenie: " + ToIpv4(broadcast),
            "· zakres hostów: " + (prefix >= 31 ? "brak (sieć /31 i /32 nie mają hostów)" : ToIpv4(network + 1) + " – " + ToIpv4(broadcast - 1)),
            "· adresów w podsieci: " + (1ul << (32 - prefix)) + " · użytecznych: " + hosts,
            "· zapis CIDR: " + ToIpv4(network) + "/" + prefix,
            "· zakres: " + (IsPrivate(address) ? "prywatny (RFC 1918)" : IsLoopback(address) ? "pętla lokalna" : "publiczny")
        };
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Dzieli sieć na N równych podsieci (N musi być potęgą dwójki).</summary>
    public static string SubnetSplit(string networkText, int parts)
    {
        string[] pieces = networkText.Split('/');
        if (!TryIpv4(pieces[0], out uint address)) return "To nie jest poprawny adres IPv4: " + pieces[0];
        int prefix = pieces.Length > 1 && int.TryParse(pieces[1], out int parsed) ? parsed : 24;
        if (prefix is < 0 or > 32) return "Prefiks musi być z zakresu 0–32.";
        if ((parts & (parts - 1)) != 0) return "Liczba podsieci musi być potęgą dwójki (2, 4, 8, 16, 32, 64) — inaczej podział nie jest równy.";
        int newPrefix = prefix + (int)Math.Log2(parts);
        if (newPrefix > 32) return "Z /" + prefix + " nie da się zrobić " + parts + " równych podsieci (potrzeba /" + newPrefix + ").";
        uint mask = prefix == 0 ? 0u : 0xFFFFFFFFu << (32 - prefix);
        uint baseNetwork = address & mask;
        uint step = 1u << (32 - newPrefix);
        var lines = new List<string> { "Podział " + ToIpv4(baseNetwork) + "/" + prefix + " na " + parts + " podsieci /" + newPrefix + ":" };
        for (int i = 0; i < parts; i++)
        {
            uint start = baseNetwork + (uint)i * step;
            uint end = start + step - 1;
            lines.Add("· " + ToIpv4(start) + "/" + newPrefix + " → hosty " + ToIpv4(start + 1) + " – " + ToIpv4(end - 1));
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static bool TryIpv4(string text, out uint value)
    {
        value = 0;
        if (!IPAddress.TryParse((text ?? "").Trim(), out var parsed)) return false;
        byte[] bytes = parsed.GetAddressBytes();
        if (bytes.Length != 4) return false;
        value = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        return true;
    }

    private static string ToIpv4(uint value) =>
        ((value >> 24) & 0xFF) + "." + ((value >> 16) & 0xFF) + "." + ((value >> 8) & 0xFF) + "." + (value & 0xFF);

    private static bool IsPrivate(uint value) =>
        (value & 0xFF000000) == 0x0A000000 ||
        (value & 0xFFF00000) == 0xAC100000 ||
        (value & 0xFFFF0000) == 0xC0A80000;

    private static bool IsLoopback(uint value) => (value & 0xFF000000) == 0x7F000000;

    // ============================================================
    // JWT
    // ============================================================

    /// <summary>Rozkodowuje nagłówek i ładunek JWT (Base64URL -> JSON). Podpisu nie weryfikuje i mówi to wprost.</summary>
    public static string JwtDecode(string token)
    {
        string clean = (token ?? "").Trim().Trim('"');
        string[] parts = clean.Split('.');
        if (parts.Length < 2) return "To nie wygląda na JWT — potrzebne co najmniej dwie części rozdzielone kropkami.";
        var lines = new List<string> { "JWT: " + (parts.Length > 2 ? "3 części (nagłówek.ładunek.podpis)" : parts.Length + " części") };
        string header = Base64UrlDecode(parts[0], out string headerError);
        if (headerError.Length > 0) lines.Add("· nagłówek: " + headerError);
        else lines.Add("· nagłówek: " + Pretty(header));
        string payload = Base64UrlDecode(parts[1], out string payloadError);
        if (payloadError.Length > 0) lines.Add("· ładunek: " + payloadError);
        else
        {
            lines.Add("· ładunek: " + Pretty(payload));
            try
            {
                using var document = JsonDocument.Parse(payload);
                if (document.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out long seconds))
                {
                    var expires = DateTimeOffset.FromUnixTimeSeconds(seconds);
                    bool expired = expires < DateTimeOffset.Now;
                    lines.Add("· ważność: " + expires.ToString("yyyy-MM-dd HH:mm:ss") + (expired ? " — WYGASŁ" : " — jeszcze ważny"));
                }
            }
            catch { /* sam podgląd ładunku wystarczy */ }
        }
        lines.Add("· Uwaga: to tylko odczyt treści. Podpisu nie sprawdzam — bez klucza publicznego nie mogę i nie udaję, że mogę.");
        return string.Join(Environment.NewLine, lines);
    }

    private static string Pretty(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return json;
        }
    }

    private static string Base64UrlDecode(string part, out string error)
    {
        error = "";
        string padded = part.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
            case 1: error = "niepoprawna długość base64url"; return "";
        }
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }
        catch (FormatException)
        {
            error = "to nie jest poprawne base64url";
            return "";
        }
    }

    // ============================================================
    // Identyfikatory
    // ============================================================

    /// <summary>UUID v7 — uporządkowany czasowo (48 bitów czasu + losowość), zgodny z RFC 9562.</summary>
    public static string UuidV7(DateTimeOffset moment)
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        long milliseconds = moment.ToUnixTimeMilliseconds() & 0xFFFFFFFFFFFFL;
        bytes[0] = (byte)(milliseconds >> 40);
        bytes[1] = (byte)(milliseconds >> 32);
        bytes[2] = (byte)(milliseconds >> 24);
        bytes[3] = (byte)(milliseconds >> 16);
        bytes[4] = (byte)(milliseconds >> 8);
        bytes[5] = (byte)milliseconds;
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        string hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return hex[..8] + "-" + hex[8..12] + "-" + hex[12..16] + "-" + hex[16..20] + "-" + hex[20..] +
            Environment.NewLine + "(UUID v7: znacznik czasu " + moment.ToString("yyyy-MM-dd HH:mm:ss.fff") + " + losowość, sortuje się chronologicznie)";
    }

    /// <summary>ULID — 48 bitów czasu + 80 bitów losowości w Crockford Base32 (26 znaków, sortowalny).</summary>
    public static string UlidNow() => Ulid(DateTimeOffset.Now);

    internal static string Ulid(DateTimeOffset moment)
    {
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        ulong time = (ulong)moment.ToUnixTimeMilliseconds();
        Span<byte> random = stackalloc byte[10];
        RandomNumberGenerator.Fill(random);
        var chars = new char[26];
        for (int i = 9; i >= 0; i--)
        {
            chars[i] = alphabet[(int)(time & 31)];
            time >>= 5;
        }
        int index = 10;
        ulong buffer = 0;
        int bits = 0;
        foreach (byte value in random)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5 && index < 26)
            {
                bits -= 5;
                chars[index++] = alphabet[(int)((buffer >> bits) & 31)];
            }
        }
        while (index < 26) chars[index++] = alphabet[0];
        return new string(chars) + Environment.NewLine + "(ULID: 26 znaków Crockford Base32, sortuje się chronologicznie)";
    }

    // ============================================================
    // Generatory kodu
    // ============================================================

    /// <summary>Generuje rekordy C# z przykładu JSON (bez zgadywania typów spoza dokumentu).</summary>
    public static string JsonToCSharp(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return "Podaj obiekt JSON w klamrach, np. „json csharp: {\"id\":1,\"name\":\"Ala\"}”.";
            var records = new List<string>();
            BuildRecord(document.RootElement, "Root", records, 0);
            return "Wygenerowane rekordy C# (typy wynikają z wartości w JSON — sprawdź null vs liczba):" +
                Environment.NewLine + string.Join(Environment.NewLine + Environment.NewLine, records);
        }
        catch (JsonException ex)
        {
            return "To nie jest poprawny JSON: " + ex.Message;
        }
    }

    private static void BuildRecord(JsonElement element, string name, List<string> records, int depth)
    {
        if (depth > 3) return;
        var properties = new List<string>();
        foreach (var property in element.EnumerateObject().Take(40))
        {
            string propertyName = Pascal(property.Name);
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Object:
                    BuildRecord(property.Value, propertyName, records, depth + 1);
                    properties.Add("    public " + propertyName + "? " + propertyName + " { get; init; }");
                    break;
                case JsonValueKind.Array:
                {
                    var first = property.Value.EnumerateArray().FirstOrDefault();
                    string inner = first.ValueKind switch
                    {
                        JsonValueKind.Number => first.TryGetInt64(out _) ? "long" : "double",
                        JsonValueKind.True or JsonValueKind.False => "bool",
                        JsonValueKind.Object => propertyName,
                        _ => "string"
                    };
                    if (first.ValueKind == JsonValueKind.Object) BuildRecord(first, propertyName, records, depth + 1);
                    properties.Add("    public List<" + inner + "> " + propertyName + " { get; init; } = [];");
                    break;
                }
                case JsonValueKind.Number:
                    properties.Add("    public " + (property.Value.TryGetInt64(out _) ? "long" : "double") + " " + propertyName + " { get; init; }");
                    break;
                case JsonValueKind.True or JsonValueKind.False:
                    properties.Add("    public bool " + propertyName + " { get; init; }");
                    break;
                case JsonValueKind.Null:
                    properties.Add("    public string? " + propertyName + " { get; init; }");
                    break;
                default:
                    properties.Add("    public string " + propertyName + " { get; init; } = \"\";");
                    break;
            }
        }
        records.Add("public sealed record " + name + Environment.NewLine + "{" + Environment.NewLine +
            string.Join(Environment.NewLine, properties) + Environment.NewLine + "}");
    }

    /// <summary>CREATE TABLE z opisu: „users | id: int pk, name: text not null”.</summary>
    public static string SqlTable(string spec)
    {
        string[] halves = (spec ?? "").Split('|', 2);
        if (halves.Length < 2) return "Użyj: „sql tabela: users | id: int pk, name: text not null”.";
        string table = halves[0].Trim();
        if (!Regex.IsMatch(table, @"^[A-Za-z_][A-Za-z0-9_]{0,60}$")) return "Nazwa tabeli może zawierać tylko litery, cyfry i podkreślenia.";
        var columns = new List<string>();
        var lines = new List<string>();
        foreach (string rawColumn in halves[1].Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pieces = rawColumn.Split(':', 2);
            if (pieces.Length < 2) { lines.Add("· pominięte (brak typu): " + rawColumn.Trim()); continue; }
            string column = pieces[0].Trim();
            string rest = pieces[1].Trim().ToLowerInvariant();
            if (!Regex.IsMatch(column, @"^[A-Za-z_][A-Za-z0-9_]{0,60}$")) { lines.Add("· pominięte (zła nazwa): " + column); continue; }
            string type = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "text";
            bool knownType = type is "int" or "integer" or "long" or "float" or "double" or "real" or "decimal"
                or "bool" or "boolean" or "date" or "datetime" or "timestamp" or "uuid"
                or "text" or "string" or "varchar" or "blob" or "bytes";
            if (!knownType) lines.Add("· nieznany typ „" + type + "” dla kolumny " + column + " → przyjąłem TEXT (sprawdź to)");
            string sqlType = type switch
            {
                "int" or "integer" or "long" => "INTEGER",
                "float" or "double" or "real" or "decimal" => "REAL",
                "bool" or "boolean" => "INTEGER",
                "date" or "datetime" or "timestamp" => "TEXT",
                "uuid" => "TEXT",
                "text" or "string" or "varchar" => "TEXT",
                "blob" or "bytes" => "BLOB",
                _ => "TEXT"
            };
            string constraints = "";
            if (rest.Contains("pk") || rest.Contains("primary")) constraints += " PRIMARY KEY";
            if (rest.Contains("not null")) constraints += " NOT NULL";
            if (rest.Contains("unique")) constraints += " UNIQUE";
            columns.Add("    " + column + " " + sqlType + constraints);
        }
        if (columns.Count == 0) return "Nie znalazłem żadnej kolumny z typem." + Environment.NewLine + string.Join(Environment.NewLine, lines);
        string statement = "CREATE TABLE IF NOT EXISTS " + table + " (" + Environment.NewLine +
            string.Join("," + Environment.NewLine, columns) + Environment.NewLine + ");";
        return (lines.Count > 0 ? string.Join(Environment.NewLine, lines) + Environment.NewLine : "") + statement +
            Environment.NewLine + "-- Wygenerowane lokalnie; sprawdź typy pod swój dialekt SQL (Postgres/MySQL/SQLite różnią się typami).";
    }

    /// <summary>CREATE TABLE + przykład INSERT z obiektu JSON.</summary>
    public static string SqlFromJson(string json, string table)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return "Podaj obiekt JSON, np. „sql z json: {\"id\":1,\"name\":\"Ala\"}”.";
            var columns = new List<string>();
            var values = new List<string>();
            foreach (var property in document.RootElement.EnumerateObject().Take(60))
            {
                string name = Regex.Replace(property.Name, @"[^A-Za-z0-9_]", "_").TrimStart('_');
                if (name.Length == 0) name = "kolumna";
                string type = property.Value.ValueKind switch
                {
                    JsonValueKind.Number => property.Value.TryGetInt64(out _) ? "INTEGER" : "REAL",
                    JsonValueKind.True or JsonValueKind.False => "INTEGER",
                    JsonValueKind.Object or JsonValueKind.Array => "TEXT",
                    _ => "TEXT"
                };
                columns.Add("    " + name + " " + type);
                values.Add(property.Value.ValueKind switch
                {
                    JsonValueKind.Number => property.Value.ToString(),
                    JsonValueKind.True => "1",
                    JsonValueKind.False => "0",
                    JsonValueKind.Null => "NULL",
                    JsonValueKind.Object or JsonValueKind.Array => "'" + property.Value.GetRawText().Replace("'", "''") + "'",
                    _ => "'" + property.Value.GetString()!.Replace("'", "''") + "'"
                });
            }
            if (columns.Count == 0) return "Obiekt JSON nie ma pól.";
            return "CREATE TABLE IF NOT EXISTS " + table + " (" + Environment.NewLine +
                string.Join("," + Environment.NewLine, columns) + Environment.NewLine + ");" + Environment.NewLine + Environment.NewLine +
                "INSERT INTO " + table + " (" + string.Join(", ", document.RootElement.EnumerateObject().Take(60).Select(x => Regex.Replace(x.Name, @"[^A-Za-z0-9_]", "_").TrimStart('_'))) +
                ") VALUES (" + string.Join(", ", values) + ");";
        }
        catch (JsonException ex)
        {
            return "To nie jest poprawny JSON: " + ex.Message;
        }
    }

    /// <summary>Przykładowy dokument JSON z prostego schematu: „{name:text, age:int, tags:[text]}”.</summary>
    public static string JsonMock(string schema)
    {
        string clean = (schema ?? "").Trim().Trim('{', '}');
        if (clean.Length == 0) return "Użyj: „mock json: {name:text, age:int, tags:[text]}”.";
        var lines = new List<string> { "{" };
        var fields = clean.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        var rendered = new List<string>();
        int index = 0;
        foreach (string field in fields)
        {
            index++;
            string[] pieces = field.Split(':', 2);
            string name = pieces[0].Trim();
            string type = pieces.Length > 1 ? pieces[1].Trim().ToLowerInvariant() : "text";
            string value = type.Trim('[', ']') switch
            {
                "int" or "integer" or "long" => index.ToString(CultureInfo.InvariantCulture),
                "float" or "double" or "number" => "1.5",
                "bool" or "boolean" => index % 2 == 0 ? "true" : "false",
                "date" or "datetime" => "\"2026-01-01T12:00:00Z\"",
                "uuid" => "\"00000000-0000-7000-8000-000000000000\"",
                "email" => "\"" + name + "@example.com\"",
                "text" or "string" => "\"przyklad " + name + "\"",
                _ => "\"przykład\""
            };
            if (type.StartsWith('[')) value = "[" + value + "]";
            rendered.Add("  \"" + name + "\": " + value);
        }
        return "{\n" + string.Join(",\n", rendered) + "\n}\n" +
            "-- Dane przykładowe wygenerowane z Twojego schematu. Nie są prawdziwe i nigdzie nie są zapisywane.";
    }

    // ============================================================
    // Kodowania i skróty
    // ============================================================

    public static string Base32Encode(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var builder = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (byte value in data)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                builder.Append(alphabet[(buffer >> bits) & 31]);
            }
        }
        if (bits > 0) builder.Append(alphabet[(buffer << (5 - bits)) & 31]);
        while (builder.Length % 8 != 0) builder.Append('=');
        return builder.ToString();
    }

    public static string Base32Decode(string text)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        string clean = (text ?? "").Trim().TrimEnd('=').ToUpperInvariant().Replace(" ", "");
        if (clean.Length == 0) return "Podaj tekst Base32.";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (char character in clean)
        {
            int index = alphabet.IndexOf(character);
            if (index < 0) return "Znak „" + character + "” nie należy do alfabetu Base32 (RFC 4648).";
            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)((buffer >> bits) & 0xFF));
            }
        }
        try
        {
            return "Zdekodowane (" + bytes.Count + " B): " + Encoding.UTF8.GetString(bytes.ToArray());
        }
        catch (Exception)
        {
            return "Zdekodowane bajty (" + bytes.Count + " B) nie są poprawnym tekstem UTF-8.";
        }
    }

    public static string Base58Encode(byte[] data)
    {
        const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        int zeros = 0;
        while (zeros < data.Length && data[zeros] == 0) zeros++;
        var digits = new List<byte> { 0 };
        foreach (byte value in data)
        {
            int carry = value;
            for (int i = 0; i < digits.Count; i++)
            {
                carry += digits[i] << 8;
                digits[i] = (byte)(carry % 58);
                carry /= 58;
            }
            while (carry > 0)
            {
                digits.Add((byte)(carry % 58));
                carry /= 58;
            }
        }
        var builder = new StringBuilder();
        for (int i = 0; i < zeros; i++) builder.Append('1');
        for (int i = digits.Count - 1; i >= 0; i--) builder.Append(alphabet[digits[i]]);
        return builder.ToString();
    }

    public static string Base58Decode(string text)
    {
        const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        string clean = (text ?? "").Trim();
        if (clean.Length == 0) return "Podaj tekst Base58.";
        int zeros = 0;
        while (zeros < clean.Length && clean[zeros] == '1') zeros++;
        var bytes = new List<byte> { 0 };
        foreach (char character in clean)
        {
            int index = alphabet.IndexOf(character);
            if (index < 0) return "Znak „" + character + "” nie należy do alfabetu Base58.";
            int carry = index;
            for (int i = 0; i < bytes.Count; i++)
            {
                carry += bytes[i] * 58;
                bytes[i] = (byte)(carry & 0xFF);
                carry >>= 8;
            }
            while (carry > 0)
            {
                bytes.Add((byte)(carry & 0xFF));
                carry >>= 8;
            }
        }
        bytes.Reverse();
        var result = new List<byte>();
        for (int i = 0; i < zeros; i++) result.Add(0);
        result.AddRange(bytes.SkipWhile(x => x == 0));
        try
        {
            return "Zdekodowane (" + result.Count + " B): " + Encoding.UTF8.GetString(result.ToArray());
        }
        catch (Exception)
        {
            return "Zdekodowane bajty (" + result.Count + " B) nie są poprawnym tekstem UTF-8.";
        }
    }

    public static string HashWith(string algorithm, string text)
    {
        byte[] data = Encoding.UTF8.GetBytes(text ?? "");
        byte[] digest;
        string name;
        switch (algorithm.ToLowerInvariant())
        {
            case "md5":
                digest = MD5.HashData(data);
                name = "MD5 (tylko do sum kontrolnych — nie do haseł)";
                break;
            case "sha1":
                digest = SHA1.HashData(data);
                name = "SHA-1 (przestarzały do podpisów)";
                break;
            case "sha384":
                digest = SHA384.HashData(data);
                name = "SHA-384";
                break;
            case "sha512":
                digest = SHA512.HashData(data);
                name = "SHA-512";
                break;
            case "crc32":
                return "CRC32 (UTF-8, " + data.Length + " B): " + Crc32(data).ToString("x8", CultureInfo.InvariantCulture);
            default:
                digest = SHA256.HashData(data);
                name = "SHA-256";
                break;
        }
        return name + " (UTF-8, " + data.Length + " B): " + Convert.ToHexString(digest).ToLowerInvariant();
    }

    /// <summary>CRC32 (IEEE 802.3) — ten sam wynik co w ZIP i PNG.</summary>
    public static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return crc ^ 0xFFFFFFFF;
    }

    public static string Codepoints(string text)
    {
        if (string.IsNullOrEmpty(text)) return "Podaj tekst, np. „kody znakow: Zażółć”.";
        var lines = text.Take(40).Select(character =>
            "· „" + character + "” = U+" + char.ConvertToUtf32(character.ToString(), 0).ToString("X4", CultureInfo.InvariantCulture) +
            " (" + (int)character + ")" + char.GetUnicodeCategory(character).ToString());
        return "Znaki (" + text.Length + "):" + Environment.NewLine + string.Join(Environment.NewLine, lines) +
            (text.Length > 40 ? Environment.NewLine + "… i " + (text.Length - 40) + " więcej." : "");
    }

    public static string FromCodepoints(string payload)
    {
        var numbers = Regex.Matches(payload ?? "", @"(?:u\+)?([0-9a-fA-F]{1,6})").Select(x => x.Groups[1].Value).ToArray();
        if (numbers.Length == 0) return "Podaj kody w zapisie szesnastkowym, np. „z ascii: 5A 61 2E”.";
        var builder = new StringBuilder();
        foreach (string hex in numbers)
        {
            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code)) continue;
            if (code is < 0 or > 0x10FFFF) return "Kod U+" + hex + " jest poza zakresem Unicode.";
            builder.Append(char.ConvertFromUtf32(code));
        }
        return "Z kodów: " + builder;
    }

    // ============================================================
    // CSV / JSON / Markdown
    // ============================================================

    public static string CsvToMarkdown(string csv)
    {
        var rows = ParseCsv(csv);
        if (rows.Count == 0) return "Nie widzę danych CSV. Wklej wiersze, np. „csv markdown: a,b\\n1,2”.";
        if (rows.Count > 200) rows = rows.Take(200).ToList();
        int columns = rows.Max(x => x.Count);
        var builder = new StringBuilder();
        builder.AppendLine("| " + string.Join(" | ", rows[0].Concat(Enumerable.Repeat("", columns - rows[0].Count))) + " |");
        builder.AppendLine("|" + string.Concat(Enumerable.Repeat(" --- |", columns)));
        foreach (var row in rows.Skip(1))
            builder.AppendLine("| " + string.Join(" | ", row.Concat(Enumerable.Repeat("", columns - row.Count)).Select(x => x.Replace("|", "\\|"))) + " |");
        return builder.ToString().TrimEnd() + Environment.NewLine + "(" + (rows.Count - 1) + " wierszy danych, " + columns + " kolumn)";
    }

    public static string CsvToJson(string csv)
    {
        var rows = ParseCsv(csv);
        if (rows.Count < 2) return "Potrzebuję wiersza nagłówka i co najmniej jednego wiersza danych.";
        if (rows.Count > 500) rows = rows.Take(500).ToList();
        var items = new List<Dictionary<string, string>>();
        foreach (var row in rows.Skip(1))
        {
            var item = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < rows[0].Count; i++)
                item[rows[0][i]] = i < row.Count ? row[i] : "";
            items.Add(item);
        }
        return JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string JsonToCsv(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return "Podaj tablicę obiektów JSON.";
            var rows = new List<string>();
            var headers = new List<string>();
            foreach (var element in document.RootElement.EnumerateArray().Take(500))
            {
                if (element.ValueKind != JsonValueKind.Object) continue;
                foreach (var property in element.EnumerateObject())
                    if (!headers.Contains(property.Name, StringComparer.Ordinal)) headers.Add(property.Name);
            }
            rows.Add(string.Join(",", headers.Select(EscapeCsv)));
            foreach (var element in document.RootElement.EnumerateArray().Take(500))
            {
                if (element.ValueKind != JsonValueKind.Object) continue;
                var values = new List<string>();
                foreach (string header in headers)
                {
                    if (element.TryGetProperty(header, out var value))
                        values.Add(EscapeCsv(value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText()));
                    else values.Add("");
                }
                rows.Add(string.Join(",", values));
            }
            if (rows.Count == 1) return "Tablica nie ma obiektów do zapisania.";
            return string.Join(Environment.NewLine, rows);
        }
        catch (JsonException ex)
        {
            return "To nie jest poprawny JSON: " + ex.Message;
        }
    }

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n')) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    internal static List<List<string>> ParseCsv(string csv)
    {
        var rows = new List<List<string>>();
        var current = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        string text = (csv ?? "").Replace("\r\n", "\n");
        for (int i = 0; i < text.Length; i++)
        {
            char character = text[i];
            if (quoted)
            {
                if (character == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else quoted = false;
                }
                else field.Append(character);
                continue;
            }
            switch (character)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                case ';':
                    current.Add(field.ToString().Trim());
                    field.Clear();
                    break;
                case '\n':
                    current.Add(field.ToString().Trim());
                    field.Clear();
                    rows.Add(current);
                    current = new List<string>();
                    break;
                case '\t':
                    current.Add(field.ToString().Trim());
                    field.Clear();
                    break;
                default:
                    field.Append(character);
                    break;
            }
        }
        if (field.Length > 0 || current.Count > 0)
        {
            current.Add(field.ToString().Trim());
            rows.Add(current);
        }
        return rows.Where(x => x.Any(v => v.Length > 0)).ToList();
    }

    public static string MarkdownToc(string markdown)
    {
        var lines = (markdown ?? "").Replace("\r\n", "\n").Split('\n');
        var toc = new List<string>();
        bool inCode = false;
        foreach (string line in lines.Take(2000))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) { inCode = !inCode; continue; }
            if (inCode) continue;
            var match = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
            if (!match.Success) continue;
            int level = match.Groups[1].Value.Length;
            string title = match.Groups[2].Value.Trim();
            string anchor = "#" + Regex.Replace(title.ToLowerInvariant(), @"[^\p{L}\p{Nd} ]", "").Replace(' ', '-');
            toc.Add(new string(' ', (level - 1) * 2) + "- [" + title + "](" + anchor + ")");
        }
        return toc.Count == 0 ? "Nie znalazłem żadnego nagłówka Markdown (# …)." :
            "Spis treści (" + toc.Count + " nagłówków):" + Environment.NewLine + string.Join(Environment.NewLine, toc);
    }

    // ============================================================
    // Walidatory
    // ============================================================

    public static string Validate(string payload)
    {
        string text = (payload ?? "").Trim().Trim('"');
        if (text.Length == 0) return "Użyj: „czy email: ala@example.com”, „czy url: https://…”, „czy ip: 10.0.0.1”, „czy uuid: …”, „czy semver: 1.2.3”.";
        if (Regex.IsMatch(text, @"^[^@\s]+@[^@\s.]+(\.[^@\s.]+)+$"))
        {
            string[] parts = text.Split('@');
            return "E-mail: wygląda poprawnie składniowo · lokalna część „" + parts[0] + "” · domena „" + parts[1] +
                "”. Uwaga: to kontrola składni — czy skrzynka istnieje, wie tylko serwer poczty.";
        }
        if (Regex.IsMatch(text, @"^https?://[^\s]+\.[^\s]{2,}$", RegexOptions.IgnoreCase) &&
            Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return "URL: poprawny · schemat " + uri.Scheme + " · host " + uri.Host + " · ścieżka „" + uri.AbsolutePath + "”" +
                (uri.Query.Length > 0 ? " · parametry: " + uri.Query : "") + " · HTTPS = szyfrowane połączenie.";
        }
        if (TryIpv4(text, out _)) return IpDescribe(text);
        if (Guid.TryParse(text, out _))
        {
            string compact = text.Replace("-", "");
            char versionDigit = compact.Length > 14 ? char.ToLowerInvariant(compact[12]) : '0';
            char variantDigit = compact.Length > 16 ? char.ToLowerInvariant(compact[16]) : '0';
            string variant = "01234567".Contains(variantDigit) ? "NCS (stary)" : "89ab".Contains(variantDigit) ? "RFC 4122/9562" : "przyszły/rezerwowany";
            return "UUID: poprawny · wersja " + versionDigit + " · wariant " + variant;
        }
        if (TrySemver(text, out _, out _)) return SemverCompare(text, text) + " (to kontrola postaci wersji)";
        if (Regex.IsMatch(text, @"^\d{11}$")) return "Wygląda na PESEL — sprawdź pełną walidację: „pesel: " + text + "”.";
        if (Regex.IsMatch(text, @"^\d{10}$")) return "Wygląda na NIP — sprawdź: „nip: " + text + "”.";
        return "Nie rozpoznaję tego jako e-mail, URL, IPv4, UUID ani wersję semantyczną. Nie zgaduję — sprawdź zapis.";
    }

    // ============================================================
    // Konwencje nazw
    // ============================================================

    public static string Naming(string style, string text)
    {
        // Granice camelCase też są separatorem: „mojaNowaKlasa” → moja_nowa_klasa.
        string spaced = Regex.Replace(text ?? "", @"(?<=[\p{Ll}\p{Nd}])(?=\p{Lu})", " ");
        string[] words = Regex.Split(spaced, @"[^\p{L}\p{Nd}]+")
            .Where(x => x.Length > 0)
            .Select(x => x.ToLowerInvariant())
            .ToArray();
        if (words.Length == 0) return "Podaj nazwę, np. „snake: mojaNowaKlasa”.";
        string camel = words[0] + string.Concat(words.Skip(1).Select(Capital));
        string pascal = string.Concat(words.Select(Capital));
        string snake = string.Join("_", words);
        string kebab = string.Join("-", words);
        string konstant = snake.ToUpperInvariant();
        string chosen = style.ToLowerInvariant() switch
        {
            "camel" => camel,
            "pascal" => pascal,
            "snake" => snake,
            "kebab" => kebab,
            "stala" => konstant,
            _ => snake
        };
        return "Nazwy z „" + text + "”:" + Environment.NewLine +
            "· camelCase: " + camel + Environment.NewLine +
            "· PascalCase: " + pascal + Environment.NewLine +
            "· snake_case: " + snake + Environment.NewLine +
            "· kebab-case: " + kebab + Environment.NewLine +
            "· STAŁA: " + konstant + Environment.NewLine +
            "· wybrane (" + style + "): " + chosen;
    }

    private static string Capital(string word) => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];

    private static string Pascal(string name)
    {
        string[] words = Regex.Split(name ?? "", @"[^\p{L}\p{Nd}]+").Where(x => x.Length > 0).ToArray();
        if (words.Length == 0) return "Pole";
        return string.Concat(words.Select(Capital));
    }

    // ============================================================
    // Commit
    // ============================================================

    public static string CommitMessage(string payload)
    {
        string text = (payload ?? "").Trim();
        if (text.Length == 0) return "Użyj: „commit: feat dodalem eksport CSV”.";
        var check = Regex.Match(text, @"^(feat|fix|docs|style|refactor|perf|test|build|ci|chore|revert)(\([^)]+\))?!?:\s+(.{3,})$", RegexOptions.IgnoreCase);
        if (check.Success)
        {
            string type = check.Groups[1].Value.ToLowerInvariant();
            string scope = check.Groups[2].Success ? check.Groups[2].Value : "";
            string description = check.Groups[3].Value;
            var notes = new List<string>();
            if (description.Length > 72) notes.Add("opis ma " + description.Length + " znaków — konwencja mówi o maks. 72");
            if (description.EndsWith('.')) notes.Add("usuń kropkę na końcu");
            if (description.Length > 0 && char.IsUpper(description[0])) notes.Add("opis zaczyna się wielką literą — konwencja woli małą");
            return "Commit poprawny konwencjonalnie: " + type + (scope.Length > 0 ? " (zakres: " + scope.Trim('(', ')') + ")" : "") +
                Environment.NewLine + "· opis: " + description +
                (notes.Count == 0 ? Environment.NewLine + "· uwagi: brak — wszystko trzyma się konwencji." :
                    Environment.NewLine + "· uwagi: " + string.Join("; ", notes) + ".");
        }
        string clean = (payload ?? "").Trim().TrimEnd('.');
        if (clean.Length == 0) return "Nie mam z czego zbudować commita.";
        string kind = Regex.IsMatch(clean, @"\b(dodaj|dodaje|nowy|nowa|nowe|dodalem|wprowadzam)\b") ? "feat"
            : Regex.IsMatch(clean, @"\b(napraw|naprawiam|poprawiam|blad|bug|fix)\b") ? "fix"
            : Regex.IsMatch(clean, @"\b(refaktor|porzadk|czyszcze|przenosze|zmieniam)\b") ? "refactor"
            : Regex.IsMatch(clean, @"\b(test|testy|sprawdzam)\b") ? "test"
            : Regex.IsMatch(clean, @"\b(dokument|readme|opis)\b") ? "docs"
            : Regex.IsMatch(clean, @"\b(wydajnosc|optymaliz|przyspiesz)\b") ? "perf"
            : "chore";
        string description = char.ToLowerInvariant(clean[0]) + clean[1..];
        if (description.Length > 72) description = description[..71] + "…";
        return kind + ": " + description + Environment.NewLine +
            "· Uwaga: typ dobrany z czasownika w Twoim opisie (" + kind + ") — popraw, jeśli nie pasuje.";
    }

    // ============================================================
    // Szablony plików
    // ============================================================

    private static readonly Dictionary<string, string> Templates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dockerfile-dotnet"] = """
            # syntax=docker/dockerfile:1
            FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
            WORKDIR /src
            COPY *.csproj ./
            RUN dotnet restore
            COPY . .
            RUN dotnet publish -c Release -o /app --no-restore

            FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
            WORKDIR /app
            COPY --from=build /app .
            ENTRYPOINT ["dotnet", "App.dll"]
            """,
        ["dockerfile-node"] = """
            FROM node:22-alpine AS build
            WORKDIR /app
            COPY package*.json ./
            RUN npm ci
            COPY . .
            RUN npm run build

            FROM node:22-alpine
            WORKDIR /app
            ENV NODE_ENV=production
            COPY --from=build /app/dist ./dist
            COPY package*.json ./
            RUN npm ci --omit=dev
            CMD ["node", "dist/index.js"]
            """,
        ["dockerfile-python"] = """
            FROM python:3.13-slim AS base
            ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1
            WORKDIR /app
            COPY requirements.txt .
            RUN pip install --no-cache-dir -r requirements.txt
            COPY . .
            CMD ["python", "-m", "app"]
            """,
        ["compose"] = """
            services:
              app:
                build: .
                ports: ["8080:8080"]
                environment:
                  - TZ=Europe/Warsaw
                restart: unless-stopped
            """,
        ["makefile"] = """
            .PHONY: build test run clean
            build:
            	dotnet build -c Release
            test:
            	dotnet test
            run:
            	dotnet run -c Release
            clean:
            	dotnet clean && rm -rf bin obj
            """,
        ["systemd"] = """
            [Unit]
            Description=Moja usługa
            After=network.target

            [Service]
            Type=simple
            User=%i
            WorkingDirectory=/opt/app
            ExecStart=/opt/app/app
            Restart=on-failure
            RestartSec=5

            [Install]
            WantedBy=multi-user.target
            """,
        ["github-actions"] = """
            name: CI
            on:
              push:
                branches: [main]
              pull_request:
            jobs:
              build:
                runs-on: ubuntu-latest
                steps:
                  - uses: actions/checkout@v5
                  - uses: actions/setup-dotnet@v5
                    with:
                      dotnet-version: 10.0.x
                  - run: dotnet restore
                  - run: dotnet build -c Release --no-restore
                  - run: dotnet test -c Release --no-build
            """,
        ["gitignore-dotnet"] = """
            bin/
            obj/
            *.user
            .vs/
            TestResults/
            """,
        ["editorconfig"] = """
            root = true

            [*]
            charset = utf-8
            end_of_line = lf
            insert_final_newline = true
            indent_style = space
            indent_size = 4
            trim_trailing_whitespace = true
            """,
        ["dockerignore"] = """
            bin/
            obj/
            .git/
            .vs/
            **/*.user
            """,
        ["nginx"] = """
            server {
                listen 80;
                server_name example.local;
                location / {
                    proxy_pass http://127.0.0.1:8080;
                    proxy_set_header Host $host;
                    proxy_set_header X-Real-IP $remote_addr;
                }
            }
            """,
        ["tsconfig"] = """
            {
              "compilerOptions": {
                "target": "ES2022",
                "module": "ESNext",
                "moduleResolution": "Bundler",
                "strict": true,
                "skipLibCheck": true,
                "noEmit": true
              },
              "include": ["src"]
            }
            """,
        ["pyproject"] = """
            [project]
            name = "moj-projekt"
            version = "0.1.0"
            requires-python = ">=3.12"
            dependencies = []

            [tool.ruff]
            line-length = 120
            """,
        ["test-unit"] = """
            [Test]
            public void Metoda_DlaWejscia_ZwracaOczekiwane()
            {
                // Arrange
                var sut = new Klasa();

                // Act
                var result = sut.Metoda(1);

                // Assert
                Assert.AreEqual(1, result);
            }
            """
    };

    public static string TemplateList() =>
        "Szablony plików (wszystko offline, nic nie zapisuję na dysk):" + Environment.NewLine +
        string.Join(Environment.NewLine, Templates.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Select(x => "· " + x)) +
        Environment.NewLine + "Użycie: „szablon: dockerfile-dotnet”.";

    public static string Template(string name)
    {
        string key = (name ?? "").Trim().ToLowerInvariant();
        if (key.Length == 0) return TemplateList();
        if (Templates.TryGetValue(key, out string? template)) return template;
        string? closest = Templates.Keys.FirstOrDefault(x => x.Contains(key, StringComparison.OrdinalIgnoreCase));
        return closest is not null
            ? Templates[closest] + Environment.NewLine + "(dopasowałem nazwę „" + name + "” do szablonu „" + closest + "”)"
            : "Nie mam szablonu „" + name + "”. Dostępne: " + string.Join(", ", Templates.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) + ".";
    }

    // ============================================================
    // Tokeny i kontekst
    // ============================================================

    /// <summary>Szacunek liczby tokenów. To heurystyka (~4 znaki na token dla angielskiego, ~3 dla
    /// polskiego), a nie licznik konkretnego modelu — i tak jest opisana.</summary>
    public static string TokenEstimate(string text)
    {
        string content = text ?? "";
        int characters = content.Length;
        int words = Regex.Matches(content, @"\S+").Count;
        double tokens = characters / 3.6;
        return "Szacunek tokenów (heurystyka, nie licznik modelu):" + Environment.NewLine +
            "· znaków: " + characters.ToString("N0", Pl) + " · słów: " + words.ToString("N0", Pl) + Environment.NewLine +
            "· ~" + Math.Round(tokens).ToString("N0", Pl) + " tokenów (≈3,6 znaku na token — tyle wychodzi dla tekstu polskiego)" +
            Environment.NewLine + "· Dokładną liczbę poda tokenizer modelu; ta liczba służy do planowania budżetu kontekstu.";
    }

    public static string ContextBudget(int window, string text)
    {
        double tokens = (text ?? "").Length / 3.6;
        double reserved = window * 0.25;
        double available = window - reserved;
        double usage = available <= 0 ? 100 : tokens / available * 100;
        return "Budżet kontekstu:" + Environment.NewLine +
            "· okno modelu: " + window.ToString("N0", Pl) + " tokenów" + Environment.NewLine +
            "· rezerwa na odpowiedź i system (25%): " + Math.Round(reserved).ToString("N0", Pl) + Environment.NewLine +
            "· treść: ~" + Math.Round(tokens).ToString("N0", Pl) + " tokenów" + Environment.NewLine +
            "· zajęcie dostępnej części: " + usage.ToString("0.#", Pl) + "%" + Environment.NewLine +
            (usage > 100
                ? "· Wniosek: treść się NIE zmieści — podziel ją albo podsumuj przed wysłaniem."
                : "· Wniosek: mieści się z zapasem ~" + Math.Round(available - tokens).ToString("N0", Pl) + " tokenów na odpowiedź.");
    }
}
