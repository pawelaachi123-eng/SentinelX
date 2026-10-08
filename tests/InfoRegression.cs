using SentinelX.Services.Info;

namespace SentinelX.Tests;

/// <summary>Weather and NBP parsing/formatting on fixed JSON fixtures. Nothing here touches the network, so the
/// result never depends on the provider being up.</summary>
internal static class InfoRegression
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private const string HourlyFixture = """
        {"hourly":{"time":["2026-10-08T15:00","2026-10-08T16:00","2026-10-08T17:00"],
        "temperature_2m":[29.5,30.2,null],"relative_humidity_2m":[18,16,20],
        "precipitation_probability":[0,5,10],"wind_speed_10m":[10.1,12,14]}}
        """;

    private const string TableAFixture = """
        [{"table":"A","no":"195/A/NBP/2026","effectiveDate":"2026-10-08",
        "rates":[{"currency":"euro","code":"EUR","mid":4.2301},{"currency":"dolar amerykański","code":"USD","mid":3.6512}]}]
        """;

    public static Task RunAsync(string directory)
    {
        // --- hourly forecast: starts at the current full hour, skips earlier hours, keeps missing values honest
        var hours = InfoParsers.ParseHourly(HourlyFixture, new DateTime(2026, 10, 8, 16, 0, 0), 12);
        Check(hours.Count == 2, "only the hours from 16:00 onward must be kept: " + hours.Count);
        string text = InfoParsers.FormatHourly("Wilamowice", hours);
        Check(text.Contains("16:00 — 30,2 °C, wilgotność 16%, szansa opadów 5%, wiatr 12 km/h"),
            "hour line must show temperature, humidity, rain chance and wind: " + text);
        Check(text.Contains("17:00 — brak danych, wilgotność 20%"), "a missing temperature must say so, not invent a value: " + text);
        Check(InfoParsers.ParseHourly(HourlyFixture, new DateTime(2026, 10, 8, 16, 0, 0), 1).Count == 1, "the hour limit must hold");
        Check(InfoParsers.ParseHourly("{}", DateTime.MinValue, 12).Count == 0, "an empty document must give no hours, not crash");
        Check(InfoParsers.FormatHourly("X", Array.Empty<HourForecast>()).StartsWith("Nie mam prognozy"), "no hours must produce a plain message");

        // --- NBP table A: one currency, all currencies, unknown code
        RateTable table = InfoParsers.ParseTableA(TableAFixture);
        Check(table.EffectiveDate == "2026-10-08", "effective date must be read");
        string eur = InfoParsers.FormatRates(table, "eur");
        Check(eur.Contains("EUR (euro): 4,2301 zł") && eur.Contains("z 2026-10-08"), "EUR line must use a Polish decimal comma: " + eur);
        Check(!eur.Contains("USD"), "a single-currency request must not list other currencies");
        Check(InfoParsers.FormatRates(table).Contains("USD (dolar amerykański): 3,6512 zł"), "all-currency list must include USD");
        Check(InfoParsers.FormatRates(table, "gbp").StartsWith("Nie ma w tabeli NBP waluty"), "an unknown code must be reported");
        bool rejectedEmpty = false;
        try { InfoParsers.ParseTableA("[]"); } catch (FormatException) { rejectedEmpty = true; }
        Check(rejectedEmpty, "an empty NBP answer must be rejected explicitly");

        return Task.CompletedTask;
    }
}
