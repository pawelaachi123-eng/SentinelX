using System;
using SentinelX.Core;

namespace SentinelX.Tests;

/// <summary>0.97 · SEKCJA 12 — research bez internetu: cytowania, bibliografia, wiarygodność,
/// plan badania, słowa kluczowe, zapytania, macierze, karty faktów. Weryfikacja formatów —
/// tekst wynikowy musi być w 100% złożony z danych wejściowych, nic nie jest zmyślane.</summary>
internal static class ResearchToolboxRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TEST FAILED: " + message);
    }

    private static string Handle(string command) =>
        ResearchToolbox.TryHandle(command, CommandText.Normalize(command))
            ?? throw new InvalidOperationException("TEST FAILED: „" + command + "” nieobsłużone");

    public static Task RunAsync(string directory)
    {
        System.IO.Directory.CreateDirectory(directory);

        string apa = Handle("cytuj apa: Kowalski | 2024 | Lokalne modele | Wydawnictwo X");
        Check(apa.Contains("Kowalski (2024). Lokalne modele. Wydawnictwo X."), "format APA: " + apa);

        string ieee = Handle("cytuj ieee: Kowalski | 2024 | Lokalne modele | Wydawnictwo X");
        Check(ieee.Contains("[1] Kowalski") && ieee.Contains("„Lokalne modele”"), "format IEEE");

        string bib = Handle("bibliografia: Nowak 2020 | Adamska 2021 | Kowalski 2019");
        Check(bib.Contains("[1] Adamska 2021") && bib.Contains("[3] Nowak 2020"), "bibliografia posortowana alfabetycznie: " + bib.Split('\n')[1]);

        Check(Handle("wiarygodnosc: praca naukowa").Contains("recenzowana"), "checklist naukowa mówi o recenzji");
        Check(Handle("plan badan: lokalne LLM").Contains("pytanie główne"), "plan badania ma pytanie główne");

        string keywords = Handle("slowa kluczowe: Model działa lokalnie. Model nie wysyła danych. Dane zostają na dysku.");
        Check(keywords.Contains("model: 2"), "licznik słów kluczowych (model ×2): " + keywords.Split('\n')[1]);

        string query = Handle("zapytanie: lokalne modele ai");
        Check(query.Contains("filetype:pdf") && query.Contains("site:wikipedia.org"), "warianty zapytania z operatorami");
        Check(query.Contains("nie pobieram") || query.Contains("niczego nie pobieram") || query.Contains("nie pobieram sam"), "uczciwa deklaracja braku pobierania");

        Check(Handle("macierz porownania: Ollama | llama.cpp | cena;jakosc").Contains("| cena |  |  |"), "macierz porównania z pustymi komórkami");

        string summary = Handle("podsumuj notatki: Model działa lokalnie i szybko. Bardzo dużo osób lubi modele. Dane zostają na dysku.");
        Check(summary.Contains("Model działa lokalnie i szybko"), "streszczenie wybiera zdanie treściwe");

        Check(Handle("fakt zapisz: Sentinel nie wysyła danych | RELEASE-0.97.md").Contains("status: do weryfikacji"), "karta faktu z obowiązkowym źródłem");
        Check(Handle("pytania badawcze: prywatność modeli").Contains("DLACZEGO"), "pytania 5W+H");

        foreach (string sentence in new[] { "cytowanie przepisów mnie nie interesuje", "plan badań nad borówkami prowadzi uniwersytet", "słowa kluczowe Kamila to singielka" })
            Check(ResearchToolbox.TryHandle(sentence, CommandText.Normalize(sentence)) is null,
                "zdanie nie jest poleceniem researchu: " + sentence);

        System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "research.txt"),
            "PASS\nAPA/IEEE citations, bibliography sort, credibility checklists, keyword counts, query variants, fact cards verified\n");
        return Task.CompletedTask;
    }
}
