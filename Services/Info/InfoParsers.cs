using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SentinelX.Services.Info;

/// <summary>One forecast hour in local time. Null means the provider sent no value for that hour.</summary>
public sealed record HourForecast(DateTime Time, double? TemperatureC, double? HumidityPercent, double? PrecipitationChancePercent, double? WindKmh);

/// <summary>One NBP table A entry: <c>Mid</c> is the average rate in PLN for one unit of <c>Code</c>.</summary>
public sealed record CurrencyRate(string Code, string Name, decimal Mid);

/// <summary>A published NBP table with its effective date (yyyy-MM-dd).</summary>
public sealed record RateTable(string EffectiveDate, IReadOnlyList<CurrencyRate> Rates);

/// <summary>Pure parsers and formatters for the weather and currency answers. No network, no WPF —
/// everything here is covered offline by <c>Tests.InfoRegression</c>.</summary>
public static partial class InfoParsers
{
    private static readonly CultureInfo Pl = new("pl-PL");
    private static readonly HashSet<string> PerHundredQuoted = new(StringComparer.Ordinal) { "JPY", "HUF", "ISK", "KRW", "IDR" };

    /// <summary>Reads Open-Meteo hourly arrays and returns up to <paramref name="hours"/> entries starting at
    /// <paramref name="fromLocal"/>. Unknown or malformed hours are skipped instead of failing the whole answer.</summary>
    public static IReadOnlyList<HourForecast> ParseHourly(string json, DateTime fromLocal, int hours)
    {
        if (hours <= 0) return Array.Empty<HourForecast>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("hourly", out var hourly) || hourly.ValueKind != JsonValueKind.Object)
            return Array.Empty<HourForecast>();
        if (!hourly.TryGetProperty("time", out var times) || times.ValueKind != JsonValueKind.Array)
            return Array.Empty<HourForecast>();

        double?[] temperature = Series(hourly, "temperature_2m");
        double?[] humidity = Series(hourly, "relative_humidity_2m");
        double?[] precipitation = Series(hourly, "precipitation_probability");
        double?[] wind = Series(hourly, "wind_speed_10m");

        var result = new List<HourForecast>();
        int index = 0;
        foreach (JsonElement element in times.EnumerateArray())
        {
            int current = index++;
            if (element.ValueKind != JsonValueKind.String) continue;
            if (!DateTime.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime time)) continue;
            if (time < fromLocal) continue;
            result.Add(new HourForecast(time, At(temperature, current), At(humidity, current), At(precipitation, current), At(wind, current)));
            if (result.Count >= hours) break;
        }
        return result;
    }

    /// <summary>Human-readable hourly table: one line per hour, Polish decimal comma, missing values stated plainly.</summary>
    public static string FormatHourly(string place, IReadOnlyList<HourForecast> hours)
    {
        if (hours.Count == 0) return $"Nie mam prognozy godzinowej dla „{place}”.";
        var text = new StringBuilder();
        text.Append("Pogoda dla ").Append(place).Append(", najbliższe ").Append(hours.Count).AppendLine(" godz.:");
        foreach (HourForecast hour in hours)
        {
            text.Append(hour.Time.ToString("HH:mm", Pl)).Append(" — ")
                .Append(Measure(hour.TemperatureC, "°C")).Append(", wilgotność ")
                .Append(Measure(hour.HumidityPercent, "%")).Append(", szansa opadów ")
                .Append(Measure(hour.PrecipitationChancePercent, "%")).Append(", wiatr ")
                .Append(Measure(hour.WindKmh, "km/h")).AppendLine();
        }
        return text.ToString().TrimEnd();
    }

    /// <summary>Reads the NBP <c>/api/exchangerates/tables/A</c> response (a one-element array).</summary>
    public static RateTable ParseTableA(string json)
    {
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            throw new FormatException("NBP: pusta odpowiedź.");
        JsonElement table = root[0];
        string date = table.GetProperty("effectiveDate").GetString() ?? "";
        var rates = new List<CurrencyRate>();
        foreach (JsonElement rate in table.GetProperty("rates").EnumerateArray())
        {
            string code = rate.GetProperty("code").GetString() ?? "";
            if (code.Length != 3) continue;
            string name = rate.GetProperty("currency").GetString() ?? code;
            decimal mid = rate.GetProperty("mid").GetDecimal();
            // NBP quotes these currencies per 100 units; store per unit so all conversions share one basis.
            if (PerHundredQuoted.Contains(code)) mid /= 100m;
            rates.Add(new CurrencyRate(code, name, mid));
        }
        return new RateTable(date, rates);
    }

    /// <summary>All rates, or one rate when <paramref name="code"/> is given (case-insensitive, e.g. "eur").</summary>
    public static string FormatRates(RateTable table, string? code = null)
    {
        IEnumerable<CurrencyRate> rates = table.Rates;
        if (!string.IsNullOrWhiteSpace(code))
        {
            rates = rates.Where(rate => string.Equals(rate.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        var list = rates.ToList();
        if (list.Count == 0) return $"Nie ma w tabeli NBP waluty „{code}”. Spróbuj kodu, np. EUR, USD, CHF, GBP.";
        var text = new StringBuilder();
        text.Append("Kursy NBP (tabela A) z ").Append(table.EffectiveDate).AppendLine(", zł za jednostkę:");
        foreach (CurrencyRate rate in list)
        {
            text.Append(rate.Code).Append(" (").Append(rate.Name).Append("): ")
                .Append(rate.Mid.ToString("0.0000", Pl)).AppendLine(" zł");
        }
        return text.ToString().TrimEnd();
    }

    private static double?[] Series(JsonElement hourly, string name)
    {
        if (!hourly.TryGetProperty(name, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
            return Array.Empty<double?>();
        return array.EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.Number ? (double?)value.GetDouble() : null)
            .ToArray();
    }

    private static double? At(double?[] series, int index) => index < series.Length ? series[index] : null;

    private static string Measure(double? value, string unit) =>
        value is null ? "brak danych" : value.Value.ToString("0.#", Pl) + (unit == "%" ? "%" : " " + unit);
}
