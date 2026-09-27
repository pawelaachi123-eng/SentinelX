using System;
using System.IO;
using System.Linq;
using SentinelX.Core;

namespace SentinelX.Tests;

/// <summary>0.97 · SEKCJA 19 — wykres jako obraz: regresja renderuje PNG do folderu tymczasowego
/// i sprawdza, że plik istnieje, ma rozsądny rozmiar, a komunikat zawiera statystyki danych.
/// Rendering działa w realnym aplikacyjnym wątku STA (to samo środowisko co zrzuty ekranu).</summary>
internal static class ChartRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TEST FAILED: " + message);
    }

    public static Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        string charts = Path.Combine(directory, "charts");
        Directory.CreateDirectory(charts);

        string summary = ChartCommands.Handle("3 5 8 4 | Sty Lut Mar Kwi", charts);
        Check(summary.Contains("WYKRES ZAPISANY"), "komunikat wskazuje zapisany plik: " + summary.Split('\n')[0]);
        Check(summary.Contains("min 3") && summary.Contains("maks 8") && summary.Contains("średnia 5"), "statystyki danych w komunikacie: " + summary.Split('\n')[1]);

        var png = new DirectoryInfo(charts).GetFiles("*.png").Single();
        Check(png.Length > 800, "PNG ma treść (" + png.Length + " B) — nie jest pustym plikiem");
        Check(png.Name.StartsWith("wykres-"), "nazwa pliku z prefiksem: " + png.Name);

        Check(ChartCommands.Handle("abc bez liczb", charts).Contains("Użycie"), "bez liczb — uczciwe użycie, nie pusty wykres");
        Check(ChartCommands.Handle("7", charts).Contains("Użycie"), "jedna liczba to nie wykres");

        // routing przez AnalysisToolbox (zapis do danych aplikacji — tu sprawdzam sam routing i komunikat)
        string routed = AnalysisToolbox.TryHandle("wykres: 10 20", CommandText.Normalize("wykres: 10 20"))
            ?? throw new InvalidOperationException("TEST FAILED: „wykres” nieprzechwycone przez AnalysisToolbox");
        Check(routed.Contains("WYKRES ZAPISANY"), "routing wykresu działa: " + routed.Split('\n')[0]);

        File.WriteAllText(Path.Combine(directory, "charts.txt"),
            "PASS\nbar chart PNG rendered, stats in message, usage guards, toolbox routing verified\n");
        return Task.CompletedTask;
    }
}
