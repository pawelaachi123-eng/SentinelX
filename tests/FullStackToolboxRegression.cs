using System;
using SentinelX.Core;

namespace SentinelX.Tests;

/// <summary>0.97 · SEKCJA 6 — full-stack offline: szkielety API, OpenAPI, modele TS/C#, SQL,
/// compose, CORS, .env, dostępy, statusy HTTP, REST, walidacja, relacje, paginacja. Generatory
/// deterministyczne — każdy fragment tekstu da się porównać z oczekiwanym.</summary>
internal static class FullStackToolboxRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TEST FAILED: " + message);
    }

    private static string Handle(string command) =>
        FullStackToolbox.TryHandle(command, CommandText.Normalize(command))
            ?? throw new InvalidOperationException("TEST FAILED: „" + command + "” nieobsłużone");

    public static Task RunAsync(string directory)
    {
        System.IO.Directory.CreateDirectory(directory);

        string ts = Handle("encja ts: Produkt | nazwa:text; cena:number");
        Check(ts.Contains("interface Produkt") && ts.Contains("cena: number;"), "interfejs TS: " + ts.Split('\n')[0]);

        string cs = Handle("encja csharp: Produkt | nazwa:text; cena:money");
        Check(cs.Contains("record Produkt(string Nazwa, decimal Cena)"), "record C# z typami pieniężnymi: " + cs.Split('\n')[0]);

        Check(Handle("http: 404").Contains("Not Found"), "kod 404 opisany");
        string limited = Handle("status http: 429");
        Check(limited.Contains("Too Many Requests") && limited.Contains("Retry-After"), "429 mówi o Retry-After");
        Check(Handle("status http: 999").Contains("nie zgaduję"), "nieznany kod — uczciwe „nie znam”, nie zmyślenie");

        string page = Handle("paginacja: 1000 20 5");
        Check(page.Contains("50 stron") && page.Contains("OFFSET 80") && page.Contains("wiersze 81–100"), "matematyka stron: " + page.Split('\n')[0]);

        Check(Handle("relacja: m:n").Contains("tabela łącząca"), "m:n przez tabelę łączącą");
        Check(Handle("rest tabela").Contains("idempotentne"), "tabela REST z idempotentnością");
        Check(Handle("sql indeks: zamowienia data,klient").Contains("CREATE INDEX IX_zamowienia_data_klient"), "nazwa indeksu deterministyczna");
        Check(Handle("migracja sql: dodaj zamowienia rabat decimal").Contains("ALTER TABLE zamowienia ADD rabat decimal NULL"), "migracja zaczyna od NULL");
        Check(Handle("api szkielet: produkty | nazwa:text").Contains("/api/produkty"), "szkielet API ma ścieżki");
        Check(Handle("openapi: produkty | nazwa:text").Contains("openapi: 3.0.3"), "OpenAPI YAML");
        Check(Handle("walidacja: email; haslo:min8").Contains("minimalna długość"), "reguła min8 opisana");
        Check(Handle("compose: web 8080 nginx").Contains("image: nginx"), "compose z obrazem");
        Check(Handle("cors: https://a.pl, https://b.pl").Contains("Access-Control-Allow-Origin: https://a.pl, https://b.pl"), "CORS z jawnymi originami");
        Check(Handle("env: DB_HOST=adres bazy; JWT_SECRET=klucz").Contains("DB_HOST=") && Handle("env: DB_HOST=adres bazy").Contains(".env.example"), "szablon env bez wartości");
        string access = Handle("dostep: admin,user | raporty");
        Check(access.Contains("· admin: czytaj, twórz, zmieniaj") && access.Contains("· user: tylko czytaj"), "macierz dostępów");

        foreach (string sentence in new[] { "encyklopedia to skarbnica wiedzy", "http to protokół przeglądarki", "środowisko env jest skomplikowane", "strona api jest chwilowo niedostępna" })
            Check(FullStackToolbox.TryHandle(sentence, CommandText.Normalize(sentence)) is null,
                "zdanie nie jest poleceniem full-stack: " + sentence);

        System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "fullstack.txt"),
            "PASS\nentities, OpenAPI, SQL migration/index, compose, CORS, env, access matrix, HTTP codes, pagination math verified\n");
        return Task.CompletedTask;
    }
}
