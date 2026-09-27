using System;
using System.IO;
using System.Linq;
using SentinelX.Core;

namespace SentinelX.Tests;

/// <summary>0.97 · SEKCJA 3 — prywatność i bezpieczeństwo danych. Test pracuje na własnym katalogu
/// tymczasowym, więc zależy wyłącznie od plików, które sam utworzył: sprawdzam realne odczyty
/// (rozmiary, wiek, próg dużych plików, podgląd retencji), teksty deklaracji (szyfrowanie,
/// uprawnienia, wysyłka) oraz to, że zwykłe zdanie o danych nie jest przechwytywane.
/// Kluczowe: retencja to PODGLĄD — test sprawdza, że po jej uruchomieniu pliki nadal istnieją.</summary>
internal static class PrivacyToolboxRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TEST FAILED: " + message);
    }

    private static string Require(string? value, string label) =>
        value ?? throw new InvalidOperationException(label + " was not handled as a privacy tool");

    public static Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        string root = Path.Combine(directory, "dane");
        string memory = Path.Combine(root, "Memory");
        string logs = Path.Combine(root, "Logs");
        Directory.CreateDirectory(memory);
        Directory.CreateDirectory(logs);
        string freshFile = Path.Combine(memory, "rozmowa.json");
        string oldFile = Path.Combine(logs, "stary.log");
        File.WriteAllBytes(freshFile, new byte[2048]);
        File.WriteAllBytes(oldFile, new byte[512]);
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-100));

        // ---------------- odczyt katalogu danych ----------------
        var entries = PrivacyToolbox.Scan(root);
        Check(entries.Count == 2, "skan widzi dwa podkatalogi, widzi: " + entries.Count);
        Check(entries.Any(x => x.Name == "Memory" && x.Bytes == 2048 && x.Files == 1), "rozmiar katalogu Memory policzony z plików");
        Check(entries.Any(x => x.Name == "Logs" && x.Files == 1), "katalog Logs policzony");

        string map = PrivacyToolbox.DataMap(root);
        Check(map.Contains("Memory") && map.Contains("Logs") && map.Contains("razem: "), "mapa danych wypisuje katalogi i sumę: " + map.Replace(Environment.NewLine, " | "));
        Check(map.Contains("2 KB") || map.Contains("2,0 KB"), "rozmiar Memory to 2 KB: " + map.Replace(Environment.NewLine, " | "));

        string summary = PrivacyToolbox.Summary(root);
        Check(summary.Contains("brak telemetrii"), "raport prywatności mówi o braku telemetrii");
        Check(summary.Contains(root), "raport wskazuje katalog danych");
        Check(summary.Contains("nic nie usuwa") || summary.Contains("nic nie zmienia"), "raport mówi, że nic nie zmienia");

        string empty = PrivacyToolbox.DataMap(Path.Combine(directory, "nie-ma-takiego"));
        Check(empty.Contains("jeszcze nic nie zostało zapisane"), "brak katalogu nie jest udawany: " + empty);

        // ---------------- duże pliki (realny próg) ----------------
        string tiny = PrivacyToolbox.LargeFiles(root, 0.001);
        Check(tiny.Contains("rozmowa.json"), "próg 1 KB wskazuje plik 2 KB: " + tiny.Replace(Environment.NewLine, " | "));
        Check(PrivacyToolbox.LargeFiles(root, 10).Contains("Brak plików danych"), "próg 10 MB nic nie znajduje");

        // ---------------- wiek danych ----------------
        string age = PrivacyToolbox.DataAge(root);
        Check(age.Contains("najstarszy plik") && age.Contains("Memory") && age.Contains("Logs"), "raport wieku danych opisuje katalogi: " + age.Replace(Environment.NewLine, " | "));

        // ---------------- retencja: podgląd, nie usuwanie ----------------
        string preview = PrivacyToolbox.Retention(root, 90);
        Check(preview.Contains("PODGLĄD") && preview.Contains("stary.log"), "podgląd retencji wskazuje stary plik: " + preview.Replace(Environment.NewLine, " | "));
        Check(File.Exists(oldFile), "podgląd retencji NIE MOŻE usuwać plików");
        Check(PrivacyToolbox.Retention(root, 200).Contains("nie ma nic starszego"), "retencja 200 dni nic nie znajduje");
        Check(PrivacyToolbox.Retention(root, 0).Contains("większą od zera"), "retencja 0 dni jest odrzucana");
        Check(PrivacyToolbox.RetentionPlan().Contains("nic nie usuwa"), "plan retencji mówi, że nic nie usuwa");

        // ---------------- deklaracje: szyfrowanie, uprawnienia, wysyłka ----------------
        Check(PrivacyToolbox.Encryption().Contains("AES-256-GCM"), "szyfrowanie wymienia sejf AES-256-GCM");
        Check(PrivacyToolbox.Encryption().Contains("NIE ZASZYFROWANE"), "szyfrowanie mówi też, co nie jest zaszyfrowane");
        Check(PrivacyToolbox.Permissions().Contains("autostart") && PrivacyToolbox.Permissions().Contains("HKCU"), "uprawnienia opisują klucz autostartu w profilu");
        Check(PrivacyToolbox.Outgoing().Contains("telemetrii") && PrivacyToolbox.Outgoing().Contains("127.0.0.1"), "wysyłka danych: brak telemetrii, tylko lokalna Ollama");
        Check(PrivacyToolbox.ExportPlan().Contains("markdown") && PrivacyToolbox.ExportPlan().Contains("kopia danych"), "plan eksportu wymienia realne polecenia");
        Check(PrivacyToolbox.Minimization().Contains("Pamięć i prywatność"), "minimalizacja wskazuje konkretne ustawienie");

        // ---------------- dispatch i brak przechwytywania zdań ----------------
        Check(Require(PrivacyToolbox.TryHandle("prywatnosc", CommandText.Normalize("prywatnosc")), "prywatnosc").Contains("Prywatność Sentinel X"), "polecenie „prywatnosc” działa");
        string dispatched = Require(PrivacyToolbox.TryHandle("gdzie sa moje dane", CommandText.Normalize("gdzie sa moje dane")), "gdzie dane");
        Check(dispatched.Contains("Gdzie są Twoje dane") || dispatched.Contains("jeszcze nic nie zostało zapisane"), "polecenie „gdzie sa moje dane” działa: " + dispatched);
        string large = Require(PrivacyToolbox.TryHandle("duze pliki danych: 5", CommandText.Normalize("duze pliki danych: 5")), "duże pliki");
        Check(large.Contains("Największe pliki danych") || large.Contains("Brak plików danych") || large.Contains("Nie ma katalogu danych"), "polecenie „duze pliki danych” działa: " + large);
        string retention = Require(PrivacyToolbox.TryHandle("retencja: 90", CommandText.Normalize("retencja: 90")), "retencja");
        Check(retention.Contains("PODGLĄD") || retention.Contains("nie ma nic starszego") || retention.Contains("nie ma czego liczyć"), "polecenie „retencja: 90” działa (na realnym katalogu danych): " + retention);
        Check(Require(PrivacyToolbox.TryHandle("szyfrowanie", CommandText.Normalize("szyfrowanie")), "szyfrowanie").Contains("AES-256-GCM"), "polecenie „szyfrowanie” działa");

        foreach (string sentence in new[]
        {
            "uprawnienia kierowcy są ważne", "prywatność w internecie to temat rzeka", "co wysyłasz do internetu",
            "gdzie są moje klucze", "retencja wody w organizmie", "szyfrowanie to trudny temat", "minimalizm w życiu",
        })
            Check(PrivacyToolbox.TryHandle(sentence, CommandText.Normalize(sentence)) is null,
                "zdanie nie jest poleceniem prywatności: " + sentence);

        return Task.CompletedTask;
    }
}
