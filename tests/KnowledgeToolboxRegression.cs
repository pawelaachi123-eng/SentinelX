using System;
using SentinelX.Core;

namespace SentinelX.Tests;

/// <summary>0.97 · SEKCJA 8 — wiedza bez embeddingów: fiszki, Anki TSV, plan powtórek, słownik,
/// podobieństwo kosinusowe (dokładna arytmetyka na workach słów), łączenie notatek, mapa, indeks.</summary>
internal static class KnowledgeToolboxRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TEST FAILED: " + message);
    }

    private static string Handle(string command) =>
        KnowledgeToolbox.TryHandle(command, CommandText.Normalize(command))
            ?? throw new InvalidOperationException("TEST FAILED: „" + command + "” nieobsłużone");

    public static Task RunAsync(string directory)
    {
        System.IO.Directory.CreateDirectory(directory);

        string cards = Handle("fiszki: kot = zwierzę domowe; pies = przyjaciel człowieka");
        Check(cards.Contains("1. Q: kot?") && cards.Contains("A: zwierzę domowe"), "fiszki Q/A: " + cards.Split('\n')[1]);

        string anki = Handle("anki: kot = zwierzę; pies = przyjaciel");
        Check(anki.Contains("kot\tzwierzę"), "TSV z tabulatorem do importu Anki");

        string repeats = Handle("powtorki: 2026-09-27");
        Check(repeats.Contains("+1 dni → 28.09.2026"), "pierwsza powtórka jutro: " + repeats.Split('\n')[1]);
        Check(repeats.Contains("+35 dni → 01.11.2026"), "ostatnia odstawa: 27.09 + 35 dni = 01.11");

        string glossary = Handle("slownik pojec: b = drugi; a = pierwszy");
        Check(glossary.IndexOf("a — pierwszy", StringComparison.Ordinal) < glossary.IndexOf("b — drugi", StringComparison.Ordinal), "słownik posortowany alfabetycznie");

        string similar = Handle("podobienstwo: kot pies | kot ryba");
        Check(similar.Contains("50%"), "kosinus {kot,pies}/{kot,ryba} = 0,5 → 50%: " + similar.Split('\n')[0]);
        Check(Handle("podobienstwo: kot pies | kot pies").Contains("100%"), "identyczne zbiory = 100%");
        Check(similar.Contains("kot"), "wspólne słowa wypisane");

        string topics = Handle("wspolne tematy: kot mleko | kot pies | pies kość");
        Check(topics.Contains("[1]+[2]: kot"), "para 1-2 przez „kot”");
        Check(topics.Contains("[2]+[3]: pies"), "para 2-3 przez „pies”");

        Check(Handle("mapa wiedzy: programowanie > języki; algorytmy").Contains("├─ języki"), "mapa wiedzy ma gałęzie");
        Check(Handle("indeks pojec: Ala ma kota. Ala ma psa.").Contains("ala: 2"), "indeks liczy powtórzenia (ala ×2)");
        Check(Handle("pytania kontrolne: Sentinel działa lokalnie. Ollama serwuje modele lokalne. Dane zostają na dysku.").Contains("PYTANIA KONTROLNE"), "pytania kontrolne z notatek");

        foreach (string sentence in new[] { "fiszki do angielskiego mam kupione", "anki to dobra aplikacja", "powtórki z matematyki się przydadzą", "podobieństwo braci jest uderzające" })
            Check(KnowledgeToolbox.TryHandle(sentence, CommandText.Normalize(sentence)) is null,
                "zdanie nie jest poleceniem wiedzy: " + sentence);

        System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "knowledge.txt"),
            "PASS\nflashcards, Anki TSV, spaced repetition dates, glossary, cosine similarity, shared topics, index verified\n");
        return Task.CompletedTask;
    }
}
