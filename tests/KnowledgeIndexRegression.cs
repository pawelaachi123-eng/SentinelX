using System;
using System.IO;
using SentinelX.Core;

namespace SentinelX.Tests;

/// <summary>0.97 · SEKCJA 8 (dopełnienie) — indeks plików tekstowych w RAM: budowa na folderze
/// tymczasowym, szukanie po słowach treściowych, uczciwe komunikaty gdy indeks pusty/folder zły.
/// Test potwierdza też routing przez KnowledgeIndexService.TryHandleCommand.</summary>
internal static class KnowledgeIndexRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TEST FAILED: " + message);
    }

    public static Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        string docs = Path.Combine(directory, "docs");
        Directory.CreateDirectory(docs);
        File.WriteAllText(Path.Combine(docs, "modele.md"), "Lokalne modele chronią prywatność. Dane nie opuszczają komputera. Ollama serwuje modele lokalne.");
        File.WriteAllText(Path(docs, "kuchnia.txt"), "Przepis na rosół: kurczak, marchew, natka pietruszki i długie gotowanie.");

        string build = KnowledgeIndexService.Build(docs);
        Check(build.Contains("INDEKS ZBUDOWANY") && build.Contains("2 plików"), "indeks z 2 plików: " + build.Split('\n')[0]);
        Check(build.Contains("TYLKO w RAM"), "indeks deklaruje życie tylko w RAM");

        string hits = KnowledgeIndexService.Search("modele prywatność");
        Check(hits.Contains("modele.md"), "szukanie trafia w modele.md: " + hits.Split('\n')[1]);
        string food = KnowledgeIndexService.Search("rosół");
        Check(food.Contains("kuchnia.txt"), "szukanie trafia w kuchnia.txt");
        Check(KnowledgeIndexService.Search("kryptowaluty").Contains("Brak trafień"), "brak trafień jest uczciwy");
        Check(KnowledgeIndexService.Status().Contains("2 plików"), "status pokazuje stan indeksu");

        Check(KnowledgeIndexService.Build(Path.Combine(directory, "nie-ma")).Contains("nie istnieje"), "zły folder — bez zgadywania");
        Check(KnowledgeIndexService.TryHandleCommand("indeks status", CommandText.Normalize("indeks status"))!.Contains("INDEKS"), "routing statusu");
        Check(KnowledgeIndexService.TryHandleCommand("indeks zbuduj", CommandText.Normalize("indeks zbuduj"))!.Contains("Podaj folder"), "routing budowy bez folderu");
        Check(KnowledgeIndexService.TryHandleCommand("losowa rozmowa o indeksach", CommandText.Normalize("losowa rozmowa o indeksach")) is null,
            "zwykłe zdanie nie jest poleceniem indeksu");

        File.WriteAllText(Path.Combine(directory, "knowledge-index.txt"),
            "PASS\nindex build (RAM only), search hits and misses, status, honest guards, command routing verified\n");
        return Task.CompletedTask;
    }
}
