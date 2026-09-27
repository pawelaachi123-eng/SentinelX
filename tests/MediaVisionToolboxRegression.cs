using System;
using SentinelX.Core;

namespace SentinelX.Tests;

/// <summary>0.97 · SEKCJE 9–10 — media i obraz bez urządzeń: WCAG, PPI, proporcje, bitrate,
/// audio, tempo mowy, decybele. Każda liczba do zweryfikowania ręcznie (wzory wypisane w kodzie).</summary>
internal static class MediaVisionToolboxRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TEST FAILED: " + message);
    }

    private static string Handle(string command) =>
        MediaVisionToolbox.TryHandle(command, CommandText.Normalize(command))
            ?? throw new InvalidOperationException("TEST FAILED: „" + command + "” nieobsłużone");

    public static Task RunAsync(string directory)
    {
        System.IO.Directory.CreateDirectory(directory);

        string whiteBlack = Handle("kontrast: #ffffff #000000");
        Check(whiteBlack.Contains("21:1") && whiteBlack.Contains("AAA"), "biały/czarny = 21:1 (AAA): " + whiteBlack.Split('\n')[0]);

        string gray = Handle("kontrast: #777777 #ffffff");
        Check(gray.Contains("dużego tekstu"), "szary #777 na białym: ~4,48:1 → tylko AA duży tekst");

        string ppi = Handle("ppi: 1920 1080 24");
        Check(ppi.Contains("92 PPI") && ppi.Contains("0,28 mm"), "24\" FHD ≈ 92 PPI, piksel 0,28 mm: " + ppi.Split('\n')[0]);

        string aspect = Handle("proporcje: 1920 1080");
        Check(aspect.Contains("16:9") && aspect.Contains("2,07 Mpx"), "1920×1080 = 16:9, 2,07 Mpx");

        string video = Handle("bitrate wideo: 90 1080p");
        Check(video.Contains("8 Mb/s") && video.Contains("90 MB"), "1080p: 8 Mb/s × 90 s = 90 MB");

        string audio = Handle("audio czas: 50 320");
        Check(audio.Contains("20:50"), "50 MB @ 320 kb/s = 1250 s = 20:50: " + audio.Split('\n')[0]);
        Check(Handle("audio rozmiar: 3:30 320").Contains("8,4 MB"), "3:30 @ 320 kb/s = 8,4 MB");
        Check(Handle("tempo mowy: 420 3").Contains("140 słów/min") && Handle("tempo mowy: 420 3").Contains("naturalnie"), "420 słów / 3 min = 140 (naturalnie)");
        Check(Handle("db: 20 3").Contains("×2 mocy") && Handle("db: 20 3").Contains("podwojenie"), "+3 dB = ×2 mocy");

        foreach (string sentence in new[] { "kontrast w obrazie jest zbyt mocny", "ppi to nie wszystko przy monitorach", "proporcje składników też się liczą" })
            Check(MediaVisionToolbox.TryHandle(sentence, CommandText.Normalize(sentence)) is null,
                "zdanie nie jest poleceniem mediów: " + sentence);

        System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "media-vision.txt"),
            "PASS\nWCAG contrast, PPI, aspect ratio, video size, audio duration/size, speech pace, dB math verified\n");
        return Task.CompletedTask;
    }
}
