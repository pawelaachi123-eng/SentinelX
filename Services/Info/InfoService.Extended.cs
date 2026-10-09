using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace SentinelX.Services.Info;

/// <summary>Multi-day forecast and NBP-based conversion. Same rules as the rest of the service: public read-only
/// endpoints, short timeouts, no redirects, plain Polish failure messages.</summary>
public static partial class InfoService
{
    private const int DaysShown = 7;

    /// <summary>Daily forecast for the next <see cref="DaysShown"/> days.</summary>
    public static async Task<string> GetWeekAsync(string city, CancellationToken cancellationToken = default)
    {
        string place = (city ?? "").Trim();
        if (place.Length == 0) return "Podaj miejscowość, np. „prognoza na 7 dni w Krakowie”.";
        if (place.Length > MaxCityLength) return "Nazwa miejscowości jest za długa.";
        try
        {
            (string name, double latitude, double longitude)? found = await GeocodeAsync(place, cancellationToken);
            if (found is null) return $"Nie znalazłem miejscowości „{place}”. Podaj nazwę w mianowniku, np. „prognoza na 7 dni Kraków”.";

            string url = ForecastUrl
                + "?latitude=" + found.Value.latitude.ToString(CultureInfo.InvariantCulture)
                + "&longitude=" + found.Value.longitude.ToString(CultureInfo.InvariantCulture)
                + "&daily=temperature_2m_max,temperature_2m_min,precipitation_probability_max,wind_speed_10m_max,uv_index_max"
                + "&timezone=auto&forecast_days=" + DaysShown.ToString(CultureInfo.InvariantCulture);
            string json = await GetStringAsync(url, cancellationToken);
            return InfoParsers.FormatDaily(found.Value.name, InfoParsers.ParseDaily(json, DaysShown));
        }
        catch (Exception exception) when (IsTransient(exception) && !cancellationToken.IsCancellationRequested)
        {
            return "Nie udało się pobrać prognozy (brak sieci albo usługa nie odpowiada). Spróbuj za chwilę.";
        }
        catch (Exception exception) when (exception is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            return "Usługa pogody zwróciła nieczytelną odpowiedź. Spróbuj później.";
        }
    }

    /// <summary>Converts an amount between two currencies using today's NBP table A.</summary>
    public static async Task<string> ConvertCurrencyAsync(decimal amount, string from, string to, CancellationToken cancellationToken = default)
    {
        try
        {
            string json = await GetStringAsync(NbpTableAUrl, cancellationToken);
            return InfoParsers.FormatConversion(InfoParsers.ParseTableA(json), amount, from, to);
        }
        catch (Exception exception) when (IsTransient(exception) && !cancellationToken.IsCancellationRequested)
        {
            return "Nie udało się pobrać kursów NBP (brak sieci albo usługa nie odpowiada). Spróbuj za chwilę.";
        }
        catch (Exception exception) when (exception is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            return "Kursy NBP przyszły w nieoczekiwanym formacie. Spróbuj później.";
        }
    }
}
