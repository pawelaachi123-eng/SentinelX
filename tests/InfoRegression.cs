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

        // --- 7-day forecast: date, max/min, rain chance; a missing minimum is stated, not invented
        const string dailyFixture = """
            {"daily":{"time":["2026-10-09","2026-10-10"],"temperature_2m_max":[18.4,20],
            "temperature_2m_min":[6.1,null],"precipitation_probability_max":[60,10],
            "wind_speed_10m_max":[30.2,22],"uv_index_max":[4.1,3]}}
            """;
        var days = InfoParsers.ParseDaily(dailyFixture, 7);
        Check(days.Count == 2, "both fixture days must be read: " + days.Count);
        string week = InfoParsers.FormatDaily("Wilamowice", days);
        Check(week.Contains("09.10 (") && week.Contains("max 18,4 °C, min 6,1 °C, szansa opadów 60%, wiatr do 30,2 km/h, UV 4,1"),
            "first day must carry max, min, rain, wind and UV: " + week);
        Check(week.Contains("10.10 (") && week.Contains("min brak danych"), "a missing minimum must be stated: " + week);
        Check(InfoParsers.ParseDaily(dailyFixture, 1).Count == 1, "the day limit must hold");
        Check(InfoParsers.ParseDaily("{}", 7).Count == 0, "an empty daily document must give no days");

        // --- currency conversion on the NBP fixture (PLN = 1)
        string toPln = InfoParsers.FormatConversion(table, 100m, "eur", "pln");
        Check(toPln.Contains("100 EUR = 423,01 PLN"), "100 EUR must convert to 423,01 PLN: " + toPln);
        string toEur = InfoParsers.FormatConversion(table, 423.01m, "PLN", "EUR");
        Check(toEur.Contains("423,01 PLN = 100,00 EUR"), "PLN to EUR must round-trip: " + toEur);
        Check(InfoParsers.FormatConversion(table, 10m, "XYZ", "PLN").Contains("Nie znam waluty"), "an unknown currency must be reported");
        Check(InfoParsers.FormatConversion(table, -5m, "EUR", "PLN").StartsWith("Podaj kwotę"), "a non-positive amount must be refused");
        Check(InfoParsers.KnownCurrencies.Contains("EUR") && !InfoParsers.KnownCurrencies.Contains("MILE"), "known-currency list must gate the converter");

        return Task.CompletedTask;
    }
}
