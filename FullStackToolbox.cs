using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>
/// SEKCJA 6 · pozycje 421–520 — full-stack bez frameworków i bez sieci: szkielet API z encji,
/// OpenAPI, modele TS/C#, SQL (migracja, indeks, relacje), compose, CORS, .env, macierz dostępów,
/// statusy HTTP, tabela REST, walidacja formularza i matematyka paginacji. Generatory tekstu —
/// punkt wyjścia do pracy, nie gotowiec produkcyjny; każda odpowiedź mówi, że nic nie uruchamiam.
/// </summary>
public static class FullStackToolbox
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    private static readonly Dictionary<string, string> HttpCodes = new(StringComparer.Ordinal)
    {
        ["200"] = "OK — powodzenie",
        ["201"] = "Created — utworzono; dołącz Location z adresem nowego zasobu",
        ["204"] = "No Content — sukces bez treści (typowe dla DELETE i PATCH)",
        ["301"] = "Moved Permanently — trwałe przekierowanie; cache'uje się na zawsze",
        ["302"] = "Found — tymczasowe przekierowanie; nie cache'uj",
        ["304"] = "Not Modified — klient ma aktualną wersję (ETag/If-Modified-Since)",
        ["400"] = "Bad Request — błąd żądania klienta; powiedz CO dokładnie",
        ["401"] = "Unauthorized — nie zalogowany albo token wygasł",
        ["403"] = "Forbidden — zalogowany, ale brak uprawnień; nie próbuj ponownie",
        ["404"] = "Not Found — zasób nie istnieje (albo nie chcesz ujawniać, że istnieje)",
        ["405"] = "Method Not Allowed — żądanie do dobrego adresu, złym czasownikiem",
        ["409"] = "Conflict — konflikt stanu (duplikat, stara wersja, równoległa edycja)",
        ["410"] = "Gone — było, świadomie usunięte na stałe",
        ["413"] = "Payload Too Large — za duże żądanie; podaj limit w treści",
        ["415"] = "Unsupported Media Type — klient wysłał nie ten format",
        ["418"] = "I'm a teapot — żart z 1998 roku; świetny do testów i sztuk Crushersów",
        ["422"] = "Unprocessable Content — składnia OK, treść nie przechodzi walidacji",
        ["429"] = "Too Many Requests — limit; dołącz Retry-After",
        ["500"] = "Internal Server Error — błąd serwera; szczegóły do logów, nie do klienta",
        ["502"] = "Bad Gateway — warstwa pośrednia nie dostała sensownej odpowiedzi",
        ["503"] = "Service Unavailable — przeciążenie albo przerwa techniczna",
        ["504"] = "Gateway Timeout — czekano zbyt długo na usługę za proxy",
    };

    public static string? TryHandle(string command, string text)
    {
        string norm = Flat(text);
        string raw = (command ?? "").Trim();

        if (Is(norm, "api szkielet")) return ApiSkeleton(Payload(raw, "api szkielet"));
        if (Is(norm, "openapi")) return OpenApi(Payload(raw, "openapi"));
        if (Is(norm, "encja ts")) return EntityTs(Payload(raw, "encja ts"));
        if (Is(norm, "encja csharp")) return EntityCs(Payload(raw, "encja csharp"));
        if (Is(norm, "migracja sql")) return SqlMigration(Payload(raw, "migracja sql"));
        if (Is(norm, "sql indeks")) return SqlIndex(Payload(raw, "sql indeks"));
        if (Is(norm, "compose")) return Compose(Payload(raw, "compose"));
        if (Is(norm, "cors")) return Cors(Payload(raw, "cors"));
        if (Is(norm, "env")) return EnvTemplate(Payload(raw, "env"));
        if (Is(norm, "dostep")) return AccessMatrix(Payload(raw, "dostep"));
        if (Is(norm, "status http")) return HttpStatus(Payload(raw, "status http"));
        if (Is(norm, "http")) return HttpStatus(Payload(raw, "http"));
        if (Is(norm, "rest tabela")) return RestTable();
        if (Is(norm, "walidacja")) return Validation(Payload(raw, "walidacja"));
        if (Is(norm, "relacja")) return Relation(Payload(raw, "relacja"));
        if (Is(norm, "paginacja")) return Pagination(Payload(raw, "paginacja"));
        return null;
    }

    private static string ApiSkeleton(string input)
    {
        var (name, fields) = ParseEntity(input);
        if (name.Length == 0)
            return "Użycie: „api szkielet: produkty | nazwa:text; cena:number”. Dostaniesz tabelę endpointów REST do pracy — niczego nie uruchamiam.";
        string base_ = "/api/" + name.ToLowerInvariant();
        return "API SZKIELET: " + base_ + " (projekt, nie działająca usługa)" + Environment.NewLine +
            "· GET    " + base_ + "        → lista (paginacja ?page=1&size=20, zawsze sortowanie stabilne)" + Environment.NewLine +
            "· GET    " + base_ + "/{id}   → szczegół · 404 gdy brak" + Environment.NewLine +
            "· POST   " + base_ + "        → utworzenie · 201 + Location · 400 przy błędzie walidacji" + Environment.NewLine +
            "· PUT    " + base_ + "/{id}   → podmiana całości · PATCH → częściowa zmiana" + Environment.NewLine +
            "· DELETE " + base_ + "/{id}   → usunięcie · 204 (zgoda użytkownika obowiązuje tu też)" + Environment.NewLine +
            "· pola: " + fields + Environment.NewLine +
            "· dalej: „openapi: …” (specyfikacja) · „encja csharp: …” / „encja ts: …” (modele)";
    }

    private static string OpenApi(string input)
    {
        var (name, _) = ParseEntity(input);
        if (name.Length == 0)
            return "Użycie: „openapi: produkty | nazwa:text; cena:number”. Wygeneruję fragment YAML do wklejenia.";
        string low = name.ToLowerInvariant();
        return "openapi: 3.0.3" + Environment.NewLine +
            "info:" + Environment.NewLine +
            "  title: " + name + " API" + Environment.NewLine +
            "  version: 0.1.0" + Environment.NewLine +
            "paths:" + Environment.NewLine +
            "  /api/" + low + ":" + Environment.NewLine +
            "    get:" + Environment.NewLine +
            "      summary: Lista " + low + Environment.NewLine +
            "      responses:" + Environment.NewLine +
            "        '200': { description: OK }" + Environment.NewLine +
            "    post:" + Environment.NewLine +
            "      summary: Nowy " + low + Environment.NewLine +
            "      responses:" + Environment.NewLine +
            "        '201': { description: Created }" + Environment.NewLine +
            "        '400': { description: Blad walidacji }" + Environment.NewLine +
            "components:" + Environment.NewLine +
            "  schemas:" + Environment.NewLine +
            "    " + name + ":" + Environment.NewLine +
            YamlProperties(input) +
            "· schemat wygenerowany z pól; uzupełnij wymagania (required) i limity przed publikacją";
    }

    private static string YamlProperties(string input)
    {
        var (_, fields) = ParseEntity(input);
        var sb = new StringBuilder();
        foreach (var (fieldName, type) in fields)
        {
            sb.Append("      ").Append(Snake(fieldName)).Append(":").AppendLine();
            sb.Append("        type: ").Append(OpenApiType(type)).AppendLine();
        }
        return sb.ToString();
    }

    private static string EntityTs(string input)
    {
        var (name, fields) = ParseEntity(input);
        if (name.Length == 0)
            return "Użycie: „encja ts: Produkt | nazwa:text; cena:number; aktywny:bool”. Typy: text, number, int, money, date, bool.";
        var sb = new StringBuilder("interface ").Append(name).Append(" {").AppendLine();
        foreach (var (f, t) in fields)
            sb.Append("  ").Append(Camel(f)).Append(": ").Append(TsType(t)).Append(';').AppendLine();
        sb.Append('}').AppendLine().Append("· daty jako Date (albo ISO string — konsekwentnie w całym projekcie); pieniądze w groszach (int), nie we floatach");
        return sb.ToString();
    }

    private static string EntityCs(string input)
    {
        var (name, fields) = ParseEntity(input);
        if (name.Length == 0)
            return "Użycie: „encja csharp: Produkt | nazwa:text; cena:money”. Typy: text, number, int, money, date, bool.";
        string args = string.Join(", ", fields.Select(f => CsName(f.Name) + " " + CsType(f.Type)));
        return "record " + name + "(" + args + ");" + Environment.NewLine +
            "· record = niemutowalny model z porównaniem po wartościach; walidację dodaj w konstruktorze/warstwie aplikacji";
    }

    private static string SqlMigration(string input)
    {
        var m = Regex.Match((input ?? "").Trim(), @"^(?:dodaj\s+)?(\w{1,40})\s+(\w{1,40})\s+(\w{1,20})$");
        if (!m.Success)
            return "Użycie: „migracja sql: dodaj zamowienia rabat decimal”. Plan bez przestoju opiszę też osobno: „migracja bazy: …”.";
        return "ALTER TABLE " + m.Groups[1].Value + " ADD " + m.Groups[2].Value + " " + m.Groups[3].Value + " NULL;" + Environment.NewLine +
            "· krok 1: kolumna NULL (stary kod żyje) → krok 2: backfill + podwójny zapis → krok 3: dopiero teraz NOT NULL" + Environment.NewLine +
            "· jednej tranzycji „dodaj i od razu NOT NULL” nie da się wykonać na żywej tabeli bez blokad";
    }

    private static string SqlIndex(string input)
    {
        var m = Regex.Match((input ?? "").Trim(), @"^(\w{1,40})\s+([\w,\s]{1,80})$");
        if (!m.Success)
            return "Użycie: „sql indeks: zamowienia data,klient”. Pokażę CREATE INDEX i powiem, kiedy indeks pomaga.";
        string table = m.Groups[1].Value;
        var cols = m.Groups[2].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => x.Length > 0).Take(4).ToList();
        string name = "IX_" + table + "_" + string.Join("_", cols);
        return "CREATE INDEX " + name + " ON " + table + " (" + string.Join(", ", cols) + ");" + Environment.NewLine +
            "· indeks pomaga gdy kolumny są w WHERE/JOIN/ORDER BY; lewym prefiksie złożonego indeksu też (data → data+klient)" + Environment.NewLine +
            "· indeks kosztuje: każdy INSERT/UPDATE go utrzymuje — nie składaj ich na zapas";
    }

    private static string Compose(string input)
    {
        var services = new List<(string Name, int Port, string Image)>();
        foreach (string part in (input ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var t = part.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (t.Length == 3 && int.TryParse(t[1], out int port) && port is > 0 and < 65536)
                services.Add((t[0], port, t[2]));
        }
        if (services.Count == 0)
            return "Użycie: „compose: web 8080 nginx; api 5000 api”. Wygeneruję docker-compose.yml do wklejenia (porty dopasuj do obrazów).";
        var sb = new StringBuilder("services:").AppendLine();
        foreach (var (n, p, img) in services)
        {
            sb.Append("  ").Append(n).Append(':').AppendLine();
            sb.Append("    image: ").Append(img).AppendLine();
            sb.Append("    ports:").AppendLine();
            sb.Append("      - \"").Append(p).Append(':').Append(p).Append('"').AppendLine();
            sb.Append("    restart: unless-stopped").AppendLine();
        }
        sb.Append("· szkielet do edycji: mapowania portów, wolumeny i zmienne wklej świadomie; sekrety nie do pliku compose");
        return sb.ToString();
    }

    private static string Cors(string input)
    {
        var origins = (input ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => x.StartsWith("http")).Take(6).ToList();
        if (origins.Count == 0)
            return "Użycie: „cors: https://aplikacja.pl, https://staging.aplikacja.pl”. Konfiguracja nagłówków CORS — lista, nigdy gwiazdka z poświadczeniami.";
        var sb = new StringBuilder("CORS (wlasciwe originy, nie *):").AppendLine();
        sb.Append("· Access-Control-Allow-Origin: ").Append(string.Join(", ", origins)).AppendLine();
        sb.Append("· Access-Control-Allow-Methods: GET, POST, PUT, PATCH, DELETE").AppendLine();
        sb.Append("· Access-Control-Allow-Headers: Content-Type, Authorization").AppendLine();
        sb.Append("· z poświadczeniami (cookies): Allow-Credentials true + wyłącznie jawne originy").AppendLine();
        sb.Append("· preflight (OPTIONS) obsłuż bez uwierzytelniania — przeglądarka pyta, zanim wyśle dane");
        return sb.ToString();
    }

    private static string EnvTemplate(string input)
    {
        var entries = new List<(string K, string V)>();
        foreach (string part in (input ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            string k = part[..eq].Trim();
            if (Regex.IsMatch(k, "^[A-Z][A-Z0-9_]{1,40}$")) entries.Add((k, part[(eq + 1)..].Trim()));
        }
        if (entries.Count == 0)
            return "Użycie: „env: DB_HOST=adres bazy; JWT_SECRET=klucz podpisu”. Wygeneruję .env.example bez wartości.";
        var sb = new StringBuilder("# .env.example — wypełnij lokalnie; do repo trafia tylko ten plik, nigdy .env").AppendLine();
        foreach (var (k, v) in entries) sb.Append(k).Append("=  # ").Append(v).AppendLine();
        return sb.ToString();
    }

    private static string AccessMatrix(string input)
    {
        string[] p = SplitParts(input, 2);
        var roles = p.Length > 0 ? p[0].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(5).ToList() : [];
        if (roles.Count == 0 || p.Length < 2 || p[1].Length == 0)
            return "Użycie: „dostep: admin,user | raporty”. Pierwsza rola = pełny dostęp, kolejne = tylko czytanie; macierz zapisuję jawnie.";
        var sb = new StringBuilder("DOSTĘP DO: ").Append(p[1]).AppendLine();
        for (int i = 0; i < roles.Count; i++)
            sb.Append("· ").Append(roles[i]).Append(": ").Append(i == 0 ? "czytaj, twórz, zmieniaj" : "tylko czytaj").AppendLine();
        sb.Append("· zasady: domyślnie odmów · role rzeczownikami · uprawnienia sprawdzaj przy KAŻDym żądaniu, nie tylko przy logowaniu");
        return sb.ToString();
    }

    private static string HttpStatus(string input)
    {
        string code = (input ?? "").Trim();
        if (HttpCodes.TryGetValue(code, out var desc))
            return "HTTP " + code + " — " + desc;
        return code.Length == 0
            ? "Użycie: „status http: 404”. Znam główne kody 200–504 — zapytaj o konkretny."
            : "Kodu „" + code + "” nie mam w tabeli (znam 200–504 z listy głównej). Nie zgaduję opisów spoza listy.";
    }

    private static string RestTable() =>
        "REST — CZYNNOŚCI:" + Environment.NewLine +
        "· GET    — czytanie · bezpieczne i idempotentne (można powtarzać bez skutków)" + Environment.NewLine +
        "· POST   — tworzenie · NIE idempotentne (dwa razy = dwa zasoby)" + Environment.NewLine +
        "· PUT    — podmiana całości · idempotentne" + Environment.NewLine +
        "· PATCH  — częściowa zmiana · nie gwarantowana idempotentność (uważaj)" + Environment.NewLine +
        "· DELETE — usuwanie · idempotentne (drugi raz → 404/204, nic się nie psuje)" + Environment.NewLine +
        "· zasada: rzeczowniki w adresach, czasowniki w metodach; operacje nie-CRUD jako podzasoby (POST /zamowienia/5/anuluj)";

    private static string Validation(string input)
    {
        var rules = (input ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(8).ToList();
        if (rules.Count == 0)
            return "Użycie: „walidacja: email; haslo:min8; wiek:1-120”. Rozpiszę reguły i komunikaty.";
        var sb = new StringBuilder("WALIDACJA (serwer sprawdza ZAWSZE, klient tylko dla wygody):").AppendLine();
        foreach (string rule in rules)
        {
            if (Flat(rule).Contains("email"))
                sb.Append("· ").Append(rule).Append(" → format: [^@\\s]+@[^@\\s]+\\.[^@\\s]+ (pełnej zgodności z RFC nikt nie wymaga regexem)").AppendLine();
            else if (Flat(rule).StartsWith("min"))
                sb.Append("· ").Append(rule).Append(" → minimalna długość; komunikat: „za krótko, wymagane N znaków”").AppendLine();
            else if (rule.Contains('-'))
                sb.Append("· ").Append(rule).Append(" → zakres liczbowy; komunikat z granicami").AppendLine();
            else
                sb.Append("· ").Append(rule).Append(" → pole wymagane, niepuste po trim").AppendLine();
        }
        sb.Append("· jeden błąd na razie czytaj wszystkie: zbierz wszystkie pola i pokaż listę — turystyka po formularzu męczy");
        return sb.ToString();
    }

    private static string Relation(string input)
    {
        string what = Flat(input ?? "").Trim();
        return what switch
        {
            "1:n" or "1 n" =>
                "RELACJA 1:N (klient → zamówienia):" + Environment.NewLine +
                "· klucz obcy po stronie N: CREATE TABLE zamowienia (klient_id INT REFERENCES klienci(id));" + Environment.NewLine +
                "· nigdy lista ID w polu tekstowym po stronie 1 — tego nie zaindeksujesz ani nie utrzymasz",
            "m:n" or "m n" =>
                "RELACJA M:N (produkty ↔ zamówienia):" + Environment.NewLine +
                "· tabela łącząca: CREATE TABLE zamowienia_produkty (zamowienie_id INT, produkt_id INT, ilosc INT, PRIMARY KEY (zamowienie_id, produkt_id));" + Environment.NewLine +
                "· dodatkowe atrybuty relacji (ilość, cena w momencie) lądują właśnie tutaj",
            "1:1" or "1 1" =>
                "RELACJA 1:1 (użytkownik → dane osobowe):" + Environment.NewLine +
                "· klucz obcy z ograniczeniem UNIQUE — użyteczne do odseparowania rzadko czytanych albo wrażliwych kolumn",
            _ => "Użycie: „relacja: 1:n” albo „m:n” albo „1:1”. Wyjaśnię i pokażę SQL.",
        };
    }

    private static string Pagination(string input)
    {
        double[] nums = Numbers(input, 2);
        if (nums.Length < 2 || nums[0] < 1 || nums[1] < 1)
            return "Użycie: „paginacja: 1000 20 5” (wierszy, na stronę, strona). Matematyka offsetów i limitów.";
        int total = (int)nums[0], size = (int)nums[1], page = nums.Length > 2 ? Math.Max(1, (int)nums[2]) : 1;
        int pages = (int)Math.Ceiling(total / (double)size);
        int offset = (page - 1) * size;
        return "PAGINACJA: " + total + " wierszy po " + size + " → " + pages + " stron" + Environment.NewLine +
            "· strona " + page + ": OFFSET " + offset + ", LIMIT " + size + " (wiersze " + (offset + 1) + "–" + Math.Min(offset + size, total) + ")" + Environment.NewLine +
            "· przy dużych OFFSET-ach baza czyta i odrzuca wiersze — wtedy strona po kluczu (keyset), nie po numerze" + Environment.NewLine +
            "· sortowanie zawsze stabilne (dodaj id na końcu), inaczej strony się zazębiają";
    }

    // ————— pomocnicze —————

    private static (string Name, List<(string Name, string Type)> Fields) ParseEntity(string input)
    {
        string[] p = SplitParts(input, 2);
        if (p.Length < 2 || p[0].Length == 0) return ("", []);
        string name = Pascal(p[0].Trim());
        if (!Regex.IsMatch(name, "^[A-Z][A-Za-z0-9]{0,39}$")) return ("", []);
        var fields = new List<(string, string)>();
        foreach (string part in p[1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = part.IndexOf(':');
            if (colon <= 0) continue;
            string fname = part[..colon].Trim();
            string ftype = part[(colon + 1)..].Trim();
            if (fname.Length > 0 && fname.Length <= 30 && ftype.Length > 0 && ftype.Length <= 12) fields.Add((fname, Flat(ftype)));
        }
        return (fields.Count > 0 ? name : "", fields);
    }

    private static string Pascal(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    private static string Camel(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
    private static string CsName(string s) => Pascal(s);
    private static string Snake(string s) => Regex.Replace(Camel(s), "([A-Z])", "_$1").ToLowerInvariant();

    private static string TsType(string t) => t switch
    {
        "number" or "int" or "money" or "decimal" => "number",
        "date" => "Date",
        "bool" or "boolean" => "boolean",
        _ => "string",
    };

    private static string CsType(string t) => t switch
    {
        "number" => "decimal",
        "int" => "int",
        "money" or "decimal" => "decimal",
        "date" => "DateTime",
        "bool" or "boolean" => "bool",
        _ => "string",
    };

    private static string OpenApiType(string t) => t switch
    {
        "number" or "money" or "decimal" => "number",
        "int" => "integer",
        "bool" or "boolean" => "boolean",
        "date" => "string",
        _ => "string",
    };

    private static string[] SplitParts(string input, int max) =>
        (input ?? "").Split('|', max, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Flat(string input)
    {
        string s = (input ?? "").ToLowerInvariant();
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            sb.Append(c switch { 'ą' => 'a', 'ć' => 'c', 'ę' => 'e', 'ł' => 'l', 'ń' => 'n', 'ó' => 'o', 'ś' => 's', 'ź' => 'z', 'ż' => 'z', _ => c });
        return sb.ToString();
    }

    private static bool Is(string norm, string trigger) => norm == trigger || norm.StartsWith(trigger + ":");
    private static string Payload(string raw, params string[] prefixes)
    {
        string t = (raw ?? "").Trim();
        foreach (string p in prefixes)
        {
            if (!t.StartsWith(p, StringComparison.OrdinalIgnoreCase)) continue;
            string rest = t[p.Length..].TrimStart();
            if (rest.StartsWith(':')) rest = rest[1..].Trim();
            return rest;
        }
        return t;
    }

    private static double[] Numbers(string input, int min)
    {
        var result = new List<double>();
        string normalized = Regex.Replace(input ?? "", @"(?<=\d),(?=\d)", ".");
        foreach (string token in Regex.Split(normalized, "[\\s;+]+"))
        {
            double v = Num(token);
            if (double.IsFinite(v)) result.Add(v);
            if (result.Count >= 6) break;
        }
        return result.Count >= min ? result.ToArray() : [];
    }

    private static double Num(string s)
    {
        s = (s ?? "").Trim().Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;
    }
}
