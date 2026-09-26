using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace SentinelX.Services.Jarvis;

/// <summary>Jedna odczytana prognoza. Wszystkie liczby są z Open-Meteo — nic tu nie jest
/// wyliczone „na oko”, a brak odczytu nie zamienia się w zero.</summary>
public sealed record WeatherReading(
    string City, double TemperatureC, int Code, double WindKmh, double HumidityPercent,
    double? DayMaxC, double? DayMinC, int? PrecipitationChance, DateTimeOffset TakenAt, bool FromCache)
{
    public string Description => WeatherService.Describe(Code);
    public string Icon => WeatherService.IconFor(Code);
    public string AgeLabel(DateTimeOffset now)
    {
        TimeSpan age = now - TakenAt.ToLocalTime();
        if (age.TotalMinutes < 1) return "przed chwilą";
        if (age.TotalHours < 1) return $"od {(int)age.TotalMinutes} min";
        if (age.TotalDays < 1) return $"od {(int)age.TotalHours} godz.";
        return $"od {(int)age.TotalDays} dni";
    }
}

/// <summary>Wynik zapytania: tekst dla użytkownika + dowód (źródło odczytu).</summary>
public sealed record WeatherOutcome(bool Success, string Message, string Evidence, WeatherReading? Reading)
{
    public static WeatherOutcome Ok(WeatherReading reading, string message, string evidence) => new(true, message, evidence, reading);
    public static WeatherOutcome Bad(string message, string evidence = "") => new(false, message, evidence, null);
}

/// <summary>
/// 0.96 · pogoda z Open-Meteo: bez konta, bez klucza API, bez danych osobowych.
/// Uczciwość wobec sieci: to jedyne polecenie pogodowe w tym produkcie, które DZWONI NA ZEWNĄTRZ —
/// dlatego wymaga włączenia w ustawieniach, cache'uje wynik w folderze danych i przy braku internetu
/// mówi wprost, że pokazuje starszy odczyt (albo że nic nie ma). Nie podajemy się za prognozę naziemną:
/// to model numeryczny, nie czujnik przy oknie.
/// </summary>
public sealed class WeatherService
{
    internal const string ForecastHost = "https://api.open-meteo.com";
    internal const string GeocodingHost = "https://geocoding-api.open-meteo.com";

    private readonly HttpClient http;
    private readonly string cacheDirectory;
    private readonly Func<JarvisSettings> settings;
    private readonly TimeSpan requestTimeout;
    private WeatherReading? lastReading;

    public WeatherService(Func<JarvisSettings>? settings = null, HttpMessageHandler? handler = null, string? cacheDirectory = null,
        TimeSpan? requestTimeout = null)
    {
        this.settings = settings ?? (() => new JarvisSettings());
        this.cacheDirectory = cacheDirectory ?? Path.Combine(AppPaths.CacheDirectory, "weather");
        this.requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(8);
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        { Timeout = this.requestTimeout };
    }

    /// <summary>Ostatni udany odczyt tej sesji (dla karty w interfejsie).</summary>
    public WeatherReading? LastReading => lastReading;
    public string CachePath(string city) => Path.Combine(cacheDirectory, Slug(city) + ".json");

    // ------------------------------------------------------------------ publicznie

    public async Task<WeatherOutcome> GetAsync(string? city, CancellationToken token)
    {
        JarvisSettings config = settings();
        string wanted = (city ?? "").Trim().Trim('"');
        if (wanted.Length == 0) wanted = config.DefaultCity;
        if (wanted.Length == 0)
            return WeatherOutcome.Bad("Nie ustawiłem miasta. Powiedz: pogoda Kraków, albo wpisz miasto w Ustawienia → Jarvis.");

        if (!config.WeatherEnabled)
        {
            WeatherReading? cached = TryReadCache(wanted);
            return cached == null
                ? WeatherOutcome.Bad("Pogoda jest wyłączona w ustawieniach: Ustawienia → Jarvis → „Pobieranie prognozy”. Włącz ją, jeśli chcesz prognozy z Open-Meteo — bez tego nie pytam sieci nawet raz.")
                : WeatherOutcome.Ok(cached, Format(cached, cached.TakenAt, cached.TakenAt, offline: true, disabled: true), "cache lokalny · sieć wyłączona ustawieniem");
        }

        WeatherReading? fresh = TryReadCache(wanted, config.WeatherCacheMinutes);
        if (fresh != null)
        {
            lastReading = fresh;
            return WeatherOutcome.Ok(fresh, Format(fresh, DateTimeOffset.Now, fresh.TakenAt, offline: false, disabled: false),
                $"cache lokalny (świeży) · {CachePath(wanted)}");
        }

        try
        {
            var location = await LocateAsync(wanted, token);
            if (location == null)
                return WeatherOutcome.Bad($"Nie znalazłem miasta „{wanted}” w bazie Open-Meteo. Spróbuj mianownika, np. „pogoda Wrocław” " +
                    "(użyłem trzech form nazwy: podanej, bez polskich znaków i po odcięciu końcówki).",
                    $"{GeocodingHost}/v1/search · brak trafienia");
            var reading = await FetchAsync(location.Value.Name, location.Value.Latitude, location.Value.Longitude, token);
            if (reading == null)
                return await FromCacheOrErrorAsync(wanted, "Open-Meteo odpowiedziało, ale bez pola bieżącej temperatury.", offline: false);
            WriteCache(reading);
            lastReading = reading;
            return WeatherOutcome.Ok(reading, Format(reading, DateTimeOffset.Now, reading.TakenAt, offline: false, disabled: false),
                $"GET {ForecastHost}/v1/forecast · {GeocodeUrl(reading.City, location.Value.Latitude, location.Value.Longitude)}");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return await FromCacheOrErrorAsync(wanted, "Open-Meteo nie odpowiedziało w limicie czasu.", offline: true); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException or UnauthorizedAccessException)
        { return await FromCacheOrErrorAsync(wanted, DescribeNetwork(ex), offline: true); }
    }

    /// <summary>Skan "czy będzie padać" na najbliższe 3 dni.</summary>
    public async Task<WeatherOutcome> RainCheckAsync(string? city, CancellationToken token)
    {
        WeatherOutcome forecast = await GetAsync(city, token);
        if (!forecast.Success || forecast.Reading == null) return forecast;
        WeatherReading reading = forecast.Reading;
        int chance = reading.PrecipitationChance ?? 0;
        string verdict = reading.Code is 61 or 63 or 65 or 80 or 81 or 82 or 95 or 96 or 99 ? "Pada teraz albo jest bardzo prawdopodobne."
            : chance >= 60 ? "Bardzo możliwe opady." : chance >= 30 ? "Może pokropić." : "Raczej sucho.";
        string message = $"Opady — {reading.City}: {verdict}\nSzansa na opad w dzień: {chance}%. " +
            $"Temperatura {reading.TemperatureC.ToString("0.0", CultureInfo.GetCultureInfo("pl-PL"))} °C, " +
            $"zakres dnia {FormatOptional(reading.DayMinC)}–{FormatOptional(reading.DayMaxC)} °C.";
        return WeatherOutcome.Ok(reading, message, forecast.Evidence);
    }

    // ------------------------------------------------------------------ transport

    private async Task<WeatherReading?> FetchAsync(string city, double latitude, double longitude, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(requestTimeout);
        using HttpResponseMessage response = await http.GetAsync(new Uri(ForecastUrl(city, latitude, longitude)), timeout.Token);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(timeout.Token);
        using JsonDocument document = JsonDocument.Parse(json);
        return ParseForecast(document.RootElement, city, DateTimeOffset.Now);
    }

    /// <summary>Polska odmiana miewa postać „we Lwowie”, a geocoder Open-Meteo chce mianownika.
    /// Próbujemy więc do trzech form, w kolejności od najbliższej intencji, i mówimy o tym w odpowiedzi.</summary>
    internal static IReadOnlyList<string> NameCandidates(string city)
    {
        var list = new List<string> { city };
        string folded = ConversationMemoryService.Normalize(city);
        if (folded.Length > 2 && !list.Contains(folded, StringComparer.Ordinal)) list.Add(folded);
        foreach (string suffix in new[] { "owie", "emu", "ach", "ami", "om", "e", "u", "i", "a" })
        {
            if (folded.Length - suffix.Length >= 4 && folded.EndsWith(suffix, StringComparison.Ordinal))
            {
                string stem = folded[..^suffix.Length];
                if (!list.Contains(stem, StringComparer.Ordinal)) list.Add(stem);
                break;
            }
        }
        return list.Take(3).ToList();
    }

    private async Task<(string Name, double Latitude, double Longitude)?> LocateAsync(string city, CancellationToken token)
    {
        foreach (string candidate in NameCandidates(city))
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(requestTimeout);
            Uri uri = new($"{GeocodingHost}/v1/search?name={Uri.EscapeDataString(candidate)}&count=1&language=pl&format=json");
            using HttpResponseMessage response = await http.GetAsync(uri, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException("Geokodowanie: HTTP " + (int)response.StatusCode);
            string json = await response.Content.ReadAsStringAsync(timeout.Token);
            (string Name, double Latitude, double Longitude)? found = ParseGeocoding(json, city);
            if (found != null) return found;
        }
        return null;
    }

    // ------------------------------------------------------------------ parsery (czyste, testowalne)

    /// <summary>Open-Meteo zwraca {name, latitude, longitude}. Brak wyników → null.</summary>
    internal static (string Name, double Latitude, double Longitude)? ParseGeocoding(string json, string fallbackName)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return null;
            foreach (JsonElement item in results.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!TryNumber(item, "latitude", out double lat) || !TryNumber(item, "longitude", out double lon)) continue;
                string name = item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? fallbackName : fallbackName;
                return (name, lat, lon);
            }
            return null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Parser bieżącej pogody + dnia. Nie zna siatki, więc nie zgaduje pól, których nie ma.</summary>
    internal static WeatherReading? ParseForecast(JsonElement root, string city, DateTimeOffset now)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (!root.TryGetProperty("current", out var current) || current.ValueKind != JsonValueKind.Object) return null;
        if (!TryNumber(current, "temperature_2m", out double temperature)) return null;
        int code = TryNumber(current, "weather_code", out double codeValue) ? (int)codeValue : -1;
        double wind = TryNumber(current, "wind_speed_10m", out double windValue) ? windValue : double.NaN;
        double humidity = TryNumber(current, "relative_humidity_2m", out double humidityValue) ? humidityValue : double.NaN;

        double? max = null, min = null;
        int? chance = null;
        if (root.TryGetProperty("daily", out var daily) && daily.ValueKind == JsonValueKind.Object)
        {
            max = FirstNumber(daily, "temperature_2m_max");
            min = FirstNumber(daily, "temperature_2m_min");
            double? probability = FirstNumber(daily, "precipitation_probability_max");
            if (probability is { } p) chance = (int)Math.Round(p);
        }
        // Czas odczytu bierzemy z własnego zegara: „current.time” jest w strefie miejsca, nie nasz czas lokalny.
        return new WeatherReading(city, temperature, code, wind, humidity, max, min, chance, now, FromCache: false);
    }

    /// <summary>Opis kodu WMO po polsku. Tabela jest jawna — bez udawania, że znamy inne kody.</summary>
    internal static string Describe(int code) => code switch
    {
        0 => "bezchmurnie", 1 => "prawie bezchmurnie", 2 => "częściowe zachmurzenie", 3 => "pochmurno",
        45 => "mgła", 48 => "mgła z szadzią", 51 => "lekka mżawka", 53 => "mżawka", 55 => "intensywna mżawka",
        56 => "mżawka zamarzająca", 57 => "silna mżawka zamarzająca", 61 => "słabe opady deszczu", 63 => "opady deszczu",
        65 => "silne opady deszczu", 66 => "deszcz ze śniegiem", 67 => "marznący deszcz", 71 => "słabe opady śniegu",
        73 => "opady śniegu", 75 => "intensywne opady śniegu", 77 => "krupki śnieżne", 80 => "przelotny deszcz",
        81 => "przelotny deszcz (umiarkowany)", 82 => "gwałtowne opady", 85 => "przelotny śnieg", 86 => "intensywny śnieg",
        95 => "burza", 96 => "burza z gradem", 99 => "burza z silnym gradem", _ => "opis niedostępny"
    };

    internal static string IconFor(int code) => code switch
    {
        0 or 1 => "☀", 2 or 3 => "☁", 45 or 48 => "≡", 51 or 53 or 55 or 56 or 57 => "🌦", 66 or 67 or 71 or 73 or 75 or 77 or 85 or 86 => "❄",
        95 or 96 or 99 => "⚡", _ => "☂"
    };

    internal static string ForecastUrl(string city, double latitude, double longitude) =>
        $"{ForecastHost}/v1/forecast?latitude={latitude.ToString("0.0000", CultureInfo.InvariantCulture)}" +
        $"&longitude={longitude.ToString("0.0000", CultureInfo.InvariantCulture)}" +
        "&current=temperature_2m,relative_humidity_2m,weather_code,wind_speed_10m" +
        "&daily=temperature_2m_max,temperature_2m_min,precipitation_probability_max&timezone=auto&forecast_days=3";

    private static string GeocodeUrl(string city, double latitude, double longitude) =>
        $"{GeocodingHost}/v1/search?name={Uri.EscapeDataString(city)} → {latitude.ToString("0.0000", CultureInfo.InvariantCulture)}," +
        longitude.ToString("0.0000", CultureInfo.InvariantCulture);

    private static bool TryNumber(JsonElement element, string name, out double value)
    {
        value = double.NaN;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Number) return false;
        if (!property.TryGetDouble(out value)) return false;
        return double.IsFinite(value);
    }

    private static double? FirstNumber(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array) return null;
        foreach (JsonElement item in array.EnumerateArray())
            if (item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out double value) && double.IsFinite(value)) return value;
        return null;
    }

    // ------------------------------------------------------------------ cache

    private WeatherReading? TryReadCache(string city, int? freshMinutes = null)
    {
        try
        {
            string path = CachePath(city);
            if (!File.Exists(path)) return null;
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("reading", out var stored)) return null;
            WeatherReading? reading = ReadStored(stored);
            if (reading == null) return null;
            if (freshMinutes is int limit && DateTimeOffset.Now - reading.TakenAt.ToLocalTime() > TimeSpan.FromMinutes(limit)) return null;
            return reading;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    private void WriteCache(WeatherReading reading)
    {
        try
        {
            Directory.CreateDirectory(cacheDirectory);
            string payload = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                reading = new
                {
                    reading.City, reading.TemperatureC, reading.Code, reading.WindKmh, reading.HumidityPercent,
                    reading.DayMaxC, reading.DayMinC, reading.PrecipitationChance, TakenAt = reading.TakenAt.ToString("O"), reading.FromCache
                }
            });
            string path = CachePath(reading.City);
            string temp = path + ".tmp";
            File.WriteAllText(temp, payload);
            File.Move(temp, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write(ex); }
    }

    private static WeatherReading? ReadStored(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!TryNumber(element, "TemperatureC", out double temperature)) return null;
        DateTimeOffset takenAt = DateTimeOffset.Now;
        if (element.TryGetProperty("TakenAt", out var stamp) && stamp.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(stamp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed))
            takenAt = parsed;
        int? chance = TryNumber(element, "PrecipitationChance", out double chanceValue) ? (int)chanceValue : null;
        return new WeatherReading(element.TryGetProperty("City", out var city) ? city.GetString() ?? "" : "", temperature,
            TryNumber(element, "Code", out double code) ? (int)code : -1,
            TryNumber(element, "WindKmh", out double wind) ? wind : double.NaN,
            TryNumber(element, "HumidityPercent", out double humidity) ? humidity : double.NaN,
            TryNumber(element, "DayMaxC", out double max) ? max : null, TryNumber(element, "DayMinC", out double min) ? min : null,
            chance, takenAt, FromCache: true);
    }

    private async Task<WeatherOutcome> FromCacheOrErrorAsync(string city, string networkReason, bool offline)
    {
        WeatherReading? cached = TryReadCache(city);
        if (cached != null)
        {
            lastReading = cached;
            return WeatherOutcome.Ok(cached, Format(cached, DateTimeOffset.Now, cached.TakenAt, offline: true, disabled: false),
                "cache lokalny · " + networkReason);
        }
        await Task.CompletedTask;
        string hint = offline
            ? "Sprawdź połączenie. Pogoda wymaga internetu (Open-Meteo) — reszta Sentinel X działa bez sieci."
            : networkReason;
        return WeatherOutcome.Bad(hint, networkReason);
    }

    // ------------------------------------------------------------------ opis

    private static string Format(WeatherReading reading, DateTimeOffset now, DateTimeOffset takenAt, bool offline, bool disabled)
    {
        var culture = CultureInfo.GetCultureInfo("pl-PL");
        var lines = new StringBuilder();
        lines.Append($"{reading.Icon}  Pogoda — {reading.City}");
        lines.Append($"\n{Number(reading.TemperatureC, culture)} °C · {reading.Description}");
        if (double.IsFinite(reading.WindKmh)) lines.Append($"\nWiatr {Number(reading.WindKmh, culture)} km/h");
        if (double.IsFinite(reading.HumidityPercent)) lines.Append($" · wilgotność {Number(reading.HumidityPercent, culture)}%");
        if (reading.DayMaxC is double max && reading.DayMinC is double min)
            lines.Append($"\nDziś: {Number(min, culture)}…{Number(max, culture)} °C");
        if (reading.PrecipitationChance is int chance)
            lines.Append($"\nSzansa opadu: {chance}%");
        lines.Append($"\nOdczyt: {reading.TakenAt.ToLocalTime():HH:mm} ({reading.AgeLabel(now)})");
        if (offline)
            lines.Append($"\n\nUwaga: to ZAPISANY odczyt (sprzed {reading.AgeLabel(now)}), nie aktualny — sieć niedostępna.");
        if (disabled)
            lines.Append("\n\nUwaga: pobieranie prognozy jest WYŁĄCZONE w ustawieniach — pokazuję ostatni zapis lokalny.");
        return lines.ToString();
    }

    private static string Number(double value, CultureInfo culture) => double.IsFinite(value) ? value.ToString("0.0", culture) : "brak odczytu";
    private static string FormatOptional(double? value) => value is double number && double.IsFinite(number) ? number.ToString("0.0", CultureInfo.GetCultureInfo("pl-PL")) : "brak";

    private static string Slug(string city)
    {
        string normalized = ConversationMemoryService.Normalize(city);
        var builder = new StringBuilder(normalized.Length);
        foreach (char c in normalized) builder.Append(char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '-');
        string slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "miasto" : slug.Length > 40 ? slug[..40] : slug;
    }

    private static string DescribeNetwork(Exception ex) => ex switch
    {
        HttpRequestException => "Brak odpowiedzi z Open-Meteo (brak internetu albo serwer nie odpowiada).",
        TaskCanceledException => "Open-Meteo nie odpowiedziała na czas.",
        IOException => "Nie udało się odczytać ani zapisać cache pogody.",
        UnauthorizedAccessException => "Brak dostępu do folderu cache.",
        JsonException => "Open-Meteo odesłała nieprawidłowy JSON.",
        _ => "Pogoda niedostępna: " + ex.Message
    };
}
