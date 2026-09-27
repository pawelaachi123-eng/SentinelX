using System;
using SentinelX.Core;

namespace SentinelX.Tests;

/// <summary>0.97 · SEKCJA 15 — analiza danych. Wszystkie funkcje są czyste, więc liczby sprawdzam
/// niezależnym rachunkiem (ręcznie policzone wartości), a nie tym samym kodem. Sprawdzam też, że
/// zwykłe zdanie z tymi słowami („trend”, „ranking”, „korelacja”) nie jest przechwytywane.</summary>
internal static class AnalysisToolboxRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TEST FAILED: " + message);
    }

    private static string Require(string? value, string label) =>
        value ?? throw new InvalidOperationException(label + " was not handled as an analysis tool");

    private static string Handle(string command) =>
        Require(AnalysisToolbox.TryHandle(command, CommandText.Normalize(command)), "polecenie „" + command + "”");

    public static Task RunAsync(string directory)
    {
        System.IO.Directory.CreateDirectory(directory);

        // ---------------- liczby z tekstu i statystyki opisowe ----------------
        double[] parsed = AnalysisToolbox.Numbers("3 4; 4  5,5 9 -2");
        Check(parsed.Length == 6 && parsed[3] == 5.5 && parsed[5] == -2, "przecinek to ułamek, resztę dzielą spacje i średniki");

        double[] sample = [3, 4, 4, 5, 9, 12];
        Check(Math.Abs(AnalysisToolbox.Mean(sample) - 6.1666667) < 0.0001, "średnia: " + AnalysisToolbox.Mean(sample));
        Check(Math.Abs(AnalysisToolbox.Variance(sample) - 12.5666667) < 0.0001, "wariancja próby (n−1): " + AnalysisToolbox.Variance(sample));
        Check(Math.Abs(AnalysisToolbox.StdDev(sample) - 3.5449) < 0.001, "odchylenie standardowe: " + AnalysisToolbox.StdDev(sample));

        string described = Handle("statystyki liczb: 3 4 4 5 9 12");
        Check(described.Contains("mediana: 4,5"), "mediana 6 liczb to 4,5: " + described.Replace(Environment.NewLine, " | "));
        Check(described.Contains("Q1 4") && described.Contains("IQR 4"), "kwartyle i IQR liczone z próby (Q1 4, Q3 8, IQR 4): " + described.Replace(Environment.NewLine, " | "));

        // ---------------- kwantyle / percentyle (interpolacja liniowa) ----------------
        double[] ten = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        Check(Math.Abs(AnalysisToolbox.Quantile(ten, 0.25) - 3.25) < 1e-9, "Q1 z 10 liczb = 3,25");
        Check(Math.Abs(AnalysisToolbox.Quantile(ten, 0.75) - 7.75) < 1e-9, "Q3 z 10 liczb = 7,75");
        Check(Handle("percentyl: 1 2 3 4 5 | 90").Contains("4,6"), "percentyl 90 z 1–5 = 4,6");
        Check(Handle("kwartyle: 1 2 3 4 5").Contains("płotki Tukeya"), "raport kwartyli pokazuje płotki");
        Check(Handle("percentyl: 1 2 3 | 120").Contains("0–100"), "percentyl poza zakresem jest odrzucany");

        // ---------------- korelacja i regresja ----------------
        double[] x = [1, 2, 3, 4], y = [2, 4, 6, 8];
        Check(Math.Abs(AnalysisToolbox.Pearson(x, y) - 1) < 1e-9, "idealna linia to r = 1");
        double[] descending = [8, 6, 4, 2];
        Check(Math.Abs(AnalysisToolbox.Pearson(x, descending) + 1) < 1e-9, "odwrotna zależność to r = −1");
        double[] ranks = AnalysisToolbox.Rank([10, 20, 20, 30]);
        Check(ranks[1] == 2.5 && ranks[2] == 2.5 && ranks[3] == 4, "remisy w rangach się uśredniają");

        string correlation = Handle("korelacja: 1 2 3 4 | 2 4 6 8");
        Check(correlation.Contains("Pearson r: 1"), "korelacja doskonała: " + correlation.Replace(Environment.NewLine, " | "));
        Check(correlation.Contains("Spearman"), "raport korelacji podaje Spearmana");
        Check(Handle("korelacja: 1 2 3 | 4 5 6 7").Contains("różną długość"), "różne długości ciągów są odrzucane");

        string regression = Handle("regresja: 1 2 3 4 | 2 4 6 8");
        Check(regression.Contains("a (nachylenie): 2") && regression.Contains("R²: 1"), "regresja y = 2x: " + regression.Replace(Environment.NewLine, " | "));
        Check(Handle("trend: 5 5 5 5").Contains("płaski"), "stały ciąg to trend płaski");
        Check(Handle("prognoza: 10 12 14").Contains("najbliższe 3 wartości"), "prognoza podaje trzy kolejne wartości");

        // ---------------- histogram, normalizacja, odległości ----------------
        string histogram = Handle("histogram: 1 2 2 3 5 8 | 4");
        Check(histogram.Contains("Histogram (6 obserwacji, 4 przedziałów"), "histogram liczy przedziały: " + histogram.Split('\n')[0]);
        Check(Handle("histogram: 7 7 7").Contains("jeden słupek"), "stałe wartości to jeden słupek");

        string normalized = Handle("normalizuj: 2 4 6 10");
        Check(normalized.Contains("0,25") && normalized.Contains("min–max"), "min–max dla 4 z 2–10 to 0,25");
        Check(Handle("odleglosc: 1 2 3 | 4 6 8").Contains("7,071"), "odległość euklidesowa √50 = 7,071");
        Check(Handle("odleglosc: 1 2 3 | 4 6").Contains("tyle samo współrzędnych"), "różne wymiary są odrzucane");

        // ---------------- metryki klasyfikacji ----------------
        string metrics = Handle("macierz pomylek: 50 10 5 35");
        Check(metrics.Contains("85%"), "dokładność (50+35)/100 = 85%: " + metrics.Replace(Environment.NewLine, " | "));
        Check(metrics.Contains("precyzja: 83,33%") && metrics.Contains("czułość (recall): 90,91%"), "precyzja i czułość liczone z macierzy");
        Check(metrics.Contains("F1: 0,87") && metrics.Contains("MCC"), "F1 i MCC są w raporcie");
        Check(Handle("macierz pomylek: 1 2").Contains("TP FP FN TN"), "niepełna macierz dostaje podpowiedź składni");

        // ---------------- entropia, Gini, outliery, wygładzanie, rangi ----------------
        Check(Handle("entropia: 8 1 1").Contains("0,92"), "entropia dla 8/1/1 to ~0,92 bita");
        Check(Handle("entropia: 5 5").Contains("1 bita"), "dwie równe klasy to dokładnie 1 bit");
        Check(Handle("gini: 2 3 5").Contains("0,62"), "nieczystość Giniego dla 2/3/5 to 0,62");
        Check(Handle("outliery: 3 4 4 5 100").Contains("pozycja 5"), "wartość 100 jest poza płotkiem IQR");
        Check(Handle("outliery: 1 2 3 4").Contains("Brak obserwacji odstających"), "bez odstających mówię to wprost");
        Check(Handle("wygladzanie: 10 14 12 18 | 0,5").Contains("wygładzone"), "wygładzanie zwraca szereg");
        Check(Handle("wygladzanie: 1 2 3 | 5").Contains("(0, 1>"), "alpha poza zakresem jest odrzucana");
        Check(Handle("rangi: 30 10 20 10").Contains("1,5."), "rangi uśredniają remis (10 i 10 → 1,5): " + Handle("rangi: 30 10 20 10").Replace(Environment.NewLine, " | "));
        Check(Handle("test t: 1 2 3 4 | 2 4 6 8").Contains("Test t Welcha") && Handle("test t: 1 1 1 | 2 2 2").Contains("zerowe odchylenie"), "test t liczy różnicę i odmawia przy braku zmienności");
        Check(Handle("wspolczynnik zmiennosci: 10 10 10").Contains("0%"), "brak zmienności to CV = 0%");
        Check(Handle("przedzial ufnosci: 10 12 14 16").Contains("95%"), "przedział ufności podaje poziom 95%");

        // ---------------- zwykłe zdania nie mogą wpadać do narzędzia ----------------
        foreach (string sentence in new[]
        {
            "trend jest rosnący", "ranking miast w Polsce się zmienił", "korelacja nie znaczy przyczynowość",
            "to była prosta regresja", "anomalie pogodowe są coraz częstsze", "percentyl brzmi groźnie",
            "entropia to ciekawe pojęcie", "normalizuję relacje z ludźmi", "histogram na tablicy",
        })
            Check(AnalysisToolbox.TryHandle(sentence, CommandText.Normalize(sentence)) is null,
                "zdanie nie jest poleceniem analizy: " + sentence);

        return Task.CompletedTask;
    }
}
