using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SentinelX.Services.Info;

/// <summary>One forecast day. Null means the provider sent no value for that day.</summary>
public sealed record DayForecast(DateOnly Date, double? MaxC, double? MinC, double? PrecipitationChancePercent, double? WindMaxKmh, double? UvMax);

/// <summary>Daily forecast, currency conversion and the NBP-based amount formatting. Pure, offline-testable.</summary>
public static partial class InfoParsers
{
    /// <summary>Currency codes the chat understands for conversion (NBP table A plus PLN itself).</summary>
    public static readonly IReadOnlyCollection<string> KnownCurrencies = new[]
    {
        "PLN", "EUR", "USD", "CHF", "GBP", "CZK", "SEK", "NOK", "DKK", "JPY", "HUF", "CAD", "AUD",
    };

    private const decimal MaxConversionAmount = 1_000_000_000m;

    /// <summary>Open-Meteo <c>daily</c> arrays, up to <paramref name="days"/> days.</summary>
    public static IReadOnlyList<DayForecast> ParseDaily(string json, int days)
    {
        if (days <= 0) return Array.Empty<DayForecast>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("daily", out JsonElement daily) || daily.ValueKind != JsonValueKind.Object)
            return Array.Empty<DayForecast>();
        if (!daily.TryGetProperty("time", out JsonElement times) || times.ValueKind != JsonValueKind.Array)
            return Array.Empty<DayForecast>();

        double?[] max = SeriesOf(daily, "temperature_2m_max");
        double?[] min = SeriesOf(daily, "temperature_2m_min");
        double?[] rain = SeriesOf(daily, "precipitation_probability_max");
        double?[] wind = SeriesOf(daily, "wind_speed_10m_max");
        double?[] uv = SeriesOf(daily, "uv_index_max");

        var result = new List<DayForecast>();
        int index = 0;
        foreach (JsonElement element in times.EnumerateArray())
        {
            int current = index++;
            if (result.Count >= days) break;
            if (element.ValueKind != JsonValueKind.String) continue;
            if (!DateOnly.TryParseExact(element.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)) continue;
            result.Add(new DayForecast(date, AtOrNull(max, current), AtOrNull(min, current), AtOrNull(rain, current), AtOrNull(wind, current), AtOrNull(uv, current)));
        }
        return result;
    }

    /// <summary>Human-readable multi-day forecast: date and weekday, max/min, rain chance, wind and UV.</summary>
    public static string FormatDaily(string place, IReadOnlyList<DayForecast> days)
    {
        if (days.Count == 0) return $"Nie mam prognozy na kilka dni dla „{place}”.";
        var pl = new CultureInfo("pl-PL");
        var text = new StringBuilder();
        text.Append("Prognoza dla ").Append(place).Append(", ").Append(days.Count).AppendLine(" dni:");
        foreach (DayForecast day in days)
        {
            text.Append(day.Date.ToString("dd.MM (ddd)", pl)).Append(" — max ")
                .Append(DayMeasure(day.MaxC, "°C")).Append(", min ")
                .Append(DayMeasure(day.MinC, "°C")).Append(", szansa opadów ")
                .Append(DayMeasure(day.PrecipitationChancePercent, "%")).Append(", wiatr do ")
                .Append(DayMeasure(day.WindMaxKmh, "km/h")).Append(", UV ")
                .Append(day.UvMax is null ? "brak danych" : day.UvMax.Value.ToString("0.#", pl)).AppendLine();
        }
        return text.ToString().TrimEnd();
    }

    /// <summary>Converts <paramref name="amount"/> using NBP mid rates (PLN = 1). Explains plainly when a code is unknown
    /// or the amount is out of range. The result is an orientation value, not a bank quote.</summary>
    public static string FormatConversion(RateTable table, decimal amount, string from, string to)
    {
        string source = (from ?? "").Trim().ToUpperInvariant();
        string target = (to ?? "").Trim().ToUpperInvariant();
        if (amount <= 0m || amount > MaxConversionAmount) return "Podaj kwotę większą od zera (do miliarda).";
        decimal? sourceRate = PlnPerUnit(table, source);
        decimal? targetRate = PlnPerUnit(table, target);
        if (sourceRate is null) return $"Nie znam waluty „{source}” w tabeli NBP.";
        if (targetRate is null) return $"Nie znam waluty „{target}” w tabeli NBP.";
        decimal result = amount * sourceRate.Value / targetRate.Value;
        var pl = new CultureInfo("pl-PL");
        return $"{amount.ToString("#,##0.##", pl)} {source} = {result.ToString("#,##0.00", pl)} {target} "
            + $"(kurs NBP z {table.EffectiveDate}, orientacyjnie; kantor może mieć inny kurs)";
    }

    private static decimal? PlnPerUnit(RateTable table, string code)
    {
        if (code == "PLN") return 1m;
        return table.Rates.Where(rate => rate.Code == code).Select(rate => (decimal?)rate.Mid).FirstOrDefault();
    }

    private static double?[] SeriesOf(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement array) || array.ValueKind != JsonValueKind.Array)
            return Array.Empty<double?>();
        return array.EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.Number ? (double?)value.GetDouble() : null)
            .ToArray();
    }

    private static double? AtOrNull(double?[] series, int index) => index < series.Length ? series[index] : null;

    private static string DayMeasure(double? value, string unit) =>
        value is null ? "brak danych" : value.Value.ToString("0.#", new CultureInfo("pl-PL")) + (unit == "%" ? "%" : " " + unit);
}
