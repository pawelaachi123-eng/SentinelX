using System;
using SentinelX.Core;

namespace SentinelX.Tests;

/// <summary>0.97 · SEKCJA 5 — architektura bez sieci: graf, cykle, sprzężenia, warstwy, dług,
/// ADR, pojemność, latencja. Wszystkie liczby sprawdzalne ręcznie; nic nie uruchamia procesów.</summary>
internal static class ArchitectureToolboxRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TEST FAILED: " + message);
    }

    private static string Handle(string command) =>
        ArchitectureToolbox.TryHandle(command, CommandText.Normalize(command))
            ?? throw new InvalidOperationException("TEST FAILED: „" + command + "” nieobsłużone");

    public static Task RunAsync(string directory)
    {
        System.IO.Directory.CreateDirectory(directory);

        string graph = Handle("moduly: a>b,c b>c c");
        Check(graph.Contains("kolejność budowy (zależności pierwsze): c → b → a"), "kolejność topologiczna zależności pierwsze: " + graph.Split('\n')[2]);
        Check(graph.Contains("CYKL") == false, "graf bez cykli nie zgłasza cyklu");

        string cycles = Handle("cykle: a>b b>c c>a");
        Check(cycles.Contains("CYKL(I): a, b, c"), "cykl wykryty i wypisany: " + cycles.Split('\n')[0]);

        string coupling = Handle("sprzezenie: a>b,c b>c c");
        Check(coupling.Contains("· a: wyjścia 2, wejścia 0"), "sprzężenia fan-out: " + coupling.Split('\n')[1]);
        Check(coupling.Contains("· c: wyjścia 0, wejścia 2"), "sprzężenia fan-in");

        string layers = Handle("warstwy: ui>logika>dane | ui>dane, logika>ui");
        Check(layers.Contains("naruszenia: 2"), "dwa naruszenia warstw (przeskok + w górę): " + layers.Split('\n')[1]);
        Check(layers.Contains("W GÓRĘ"), "wywołanie w górę nazwane wprost");

        string debt = Handle("dlug techniczny: a=3; b=1; c=7");
        Check(debt.Contains("suma 11"), "suma długu: " + debt.Split('\n')[0]);
        Check(debt.Contains("· c = 7 → WYSOKI"), "największy dług najwyżej");

        string adr = Handle("adr: cache odpowiedzi | latencja 200 ms | redis przed baza | koszt pamieci");
        Check(adr.Contains("Status: proponowany") && adr.Contains("Decyzja: redis przed baza"), "karta ADR kompletna");

        string style = Handle("styl: mikro");
        Check(style.Contains("MIKROUSŁUGI"), "styl mikro opisany: " + style.Split('\n')[0]);

        string capacity = Handle("kapacyt: 5000 250");
        Check(capacity.Contains("czynne instancje: 20") && capacity.Contains("z rezerwą na awarię/rolling deploy: 21"), "20 czynnych + 1 rezerwowa");

        string queue = Handle("pojemnosc kolejki: 5000 100");
        Check(queue.Contains("KOLEJKA") && queue.Contains("50 min"), "kolejka 5000 przy 100/min = 50 min: " + queue);

        string latency = Handle("latencja: 50 20 10 5");
        Check(latency.Contains("suma 85 ms"), "suma budżetu 85 ms");
        Check(latency.Contains("krok 1"), "największy kawałek to krok 1 (50 ms)");

        Check(Handle("migracja bazy: rabat").Contains("ROZSZERZ"), "plan migracji expand-migrate-contract");
        Check(Handle("wdrozenie kanary: 5").Contains("25%"), "etapy kanaryjskie 1→100%");
        Check(Handle("karta modulu: autoryzacja | logowanie | api").Contains("pytanie SRP"), "karta modułu pyta o SRP");

        foreach (string sentence in new[] { "adresowanie sieci to osobny temat", "stylizacja włosów to sztuka", "grafika wakacyjna wyszła świetnie", "c4 to nazwa materiału wybuchowego" })
            Check(ArchitectureToolbox.TryHandle(sentence, CommandText.Normalize(sentence)) is null,
                "zdanie nie jest poleceniem architektury: " + sentence);

        System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "architecture.txt"),
            "PASS\ntopological order, cycles, coupling, layer violations, debt register, ADR, capacity, latency budget verified\n");
        return Task.CompletedTask;
    }
}
