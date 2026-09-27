using System;
using SentinelX.Core;

namespace SentinelX.Tests;

/// <summary>0.97 · SEKCJA 18 — smart home bez sprzętu: energia i koszty (arytmetyka do sprawdzenia
/// na kalkulatorze), termostat, scena, YAML, MQTT, prąd, lumeny, bateria, taryfa. Sentinel niczego
/// nie łączy — wszystkie odpowiedzi mówią o liczeniu, nie sterowaniu.</summary>
internal static class SmartHomeToolboxRegression
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TEST FAILED: " + message);
    }

    private static string Handle(string command) =>
        SmartHomeToolbox.TryHandle(command, CommandText.Normalize(command))
            ?? throw new InvalidOperationException("TEST FAILED: „" + command + "” nieobsłużone");

    public static Task RunAsync(string directory)
    {
        System.IO.Directory.CreateDirectory(directory);

        string energy = Handle("energia: 100 5 1,0");
        Check(energy.Contains("0,5 kWh/dzień") && energy.Contains("182,5 kWh/rok"), "100 W × 5 h = 0,5 kWh/d, 182,5 kWh/rok: " + energy.Split('\n')[1]);
        Check(energy.Contains("0,5 zł/dzień") && energy.Contains("182,5 zł/rok"), "koszt przy 1 zł/kWh");

        string multi = Handle("koszt urzadzen: 100 5 1,0; 60 10 1,0");
        Check(multi.Contains("RAZEM: 1,1 kWh/dzień") && multi.Contains("401,5 zł/rok"), "suma urządzeń: 0,5+0,6 kWh, 401,5 zł/rok");

        Check(Handle("termostat: 21 20").Contains("≈6%"), "1 K ≈ 5–7%, tu ~6%");
        Check(Handle("scena dom: film | światła 20, rolety 90").Contains("światła 20"), "karta sceny");
        string yaml = Handle("yaml automatyzacji: 22:00 | wyłącz światła");
        Check(yaml.Contains("platform: time") && yaml.Contains("at: \"22:00\""), "YAML automatyzacji z godziną");
        Check(Handle("mqtt: dom/parter/lampa/stan").Contains("poprawny (4 poziomy)"), "poprawny temat MQTT");
        string wildcard = Handle("mqtt: dom/+/stan");
        Check(wildcard.Contains("SYMBOLE") && wildcard.Contains("jeden poziom"), "symbol + wyjaśniony");
        Check(Handle("prad: 1500").Contains("6,52 A"), "1500 W / 230 V = 6,52 A");
        Check(Handle("luminy: 18").Contains("2700 lm"), "18 m² × 150 lx = 2700 lm");
        Check(Handle("czujnik baterii: 3000 15 8").Contains("25 dni"), "3000 mAh @ 5 mA średnio = 25 dni");
        Check(Handle("tarif: 2000 1,0 0,85").Contains("różnica 300 zł/rok"), "2000 kWh × (1,0−0,85) = 300 zł");

        foreach (string sentence in new[] { "energia słoneczna jest darmowa", "mqtt bez powiadomień jest spokojny", "prąd w domu bije z gniazdka", "scenariusz z px33 był fajny" })
            Check(SmartHomeToolbox.TryHandle(sentence, CommandText.Normalize(sentence)) is null,
                "zdanie nie jest poleceniem smart home: " + sentence);

        System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "smarthome.txt"),
            "PASS\nenergy math, devices sum, thermostat rule, scene card, YAML, MQTT topics, amps, lumens, battery, tariff verified\n");
        return Task.CompletedTask;
    }
}
