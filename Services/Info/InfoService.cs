using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace SentinelX.Services.Info;

/// <summary>Weather (Open-Meteo, no API key) and currency rates (NBP table A, no API key).
/// Only public read-only endpoints are called; the city name is the only user data sent. Redirects are refused
/// and every call has a short timeout. Failures come back as plain Polish text, never as exceptions.</summary>
public static class InfoService
{
    /// <summary>Used when the user asks just for "pogoda" (no city yet — a settings field comes later).</summary>
    public const string DefaultCity = "Wilamowice";

    private const string GeocodingUrl = "https://geocoding-api.open-meteo.com/v1/search";
    private const string ForecastUrl = "https://api.open-meteo.com/v1/forecast";
    private const string NbpTableAUrl = "https://api.nbp.pl/api/exchangerates/tables/A/?format=json";
    private const int MaxCityLength = 60;
    private const int HoursShown = 12;

    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(12),
    };

    /// <summary>Weather for the next hours in <paramref name="city"/>; <paramref name="now"/> is injectable for tests.</summary>
    public static async Task<string> GetWeatherAsync(string city, DateTime now, CancellationToken cancellationToken = default)
    {
        string place = (city ?? "").Trim();
        if (place.Length == 0) return "Podaj miejscowość, np. „pogoda Kraków”.";
        if (place.Length > MaxCityLength) return "Nazwa miejscowości jest za długa.";
        try
        {
            (string name, double latitude, double longitude)? found = await GeocodeAsync(place, cancellationToken);
            if (found is null) return $"Nie znalazłem miejscowości „{place}”. Podaj nazwę w mianowniku, np. „pogoda Kraków”.";

            string url = ForecastUrl
                + "?latitude=" + found.Value.latitude.ToString(CultureInfo.InvariantCulture)
                + "&longitude=" + found.Value.longitude.ToString(CultureInfo.InvariantCulture)
                + "&hourly=temperature_2m,relative_humidity_2m,precipitation_probability,wind_speed_10m"
                + "&timezone=auto&forecast_days=2";
            string json = await GetStringAsync(url, cancellationToken);
            DateTime fromLocal = new(now.Year, now.Month, now.Day, now.Hour, 0, 0);
            return InfoParsers.FormatHourly(found.Value.name, InfoParsers.ParseHourly(json, fromLocal, HoursShown));
        }
        catch (Exception exception) when (IsTransient(exception) && !cancellationToken.IsCancellationRequested)
        {
            return "Nie udało się pobrać pogody (brak sieci albo usługa nie odpowiada). Spróbuj za chwilę.";
        }
        catch (Exception exception) when (exception is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            return "Usługa pogody zwróciła nieczytelną odpowiedź. Spróbuj później.";
        }
    }

    /// <summary>Today's NBP table A, optionally filtered to one currency code.</summary>
    public static async Task<string> GetCurrencyRatesAsync(string? code = null, CancellationToken cancellationToken = default)
    {
        try
        {
            string json = await GetStringAsync(NbpTableAUrl, cancellationToken);
            return InfoParsers.FormatRates(InfoParsers.ParseTableA(json), code);
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

    private static async Task<(string name, double latitude, double longitude)?> GeocodeAsync(string place, CancellationToken cancellationToken)
    {
        string url = GeocodingUrl + "?name=" + Uri.EscapeDataString(place) + "&count=1&language=pl&format=json";
        string json = await GetStringAsync(url, cancellationToken);
        using JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out JsonElement results)
            || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
            return null;
        JsonElement first = results[0];
        string name = first.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() ?? place : place;
        return (name, first.GetProperty("latitude").GetDouble(), first.GetProperty("longitude").GetDouble());
    }

    private static async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await Http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)response.StatusCode}");
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static bool IsTransient(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or TimeoutException;
}
