using System.Text.RegularExpressions;

namespace SentinelX;

/// <summary>
/// 0.96 · warstwa JARVIS: pogoda, multimedia, dom, agent, wizja i pamięć semantyczna.
/// Wszystkie wartości są sprawdzane w <see cref="SentinelSettings.Validate"/> — plik ustawień
/// może być edytowany ręcznie, więc nic tu nie może zaufać własnemu treśćowi.
/// Hasła i tokeny celowo NIE są tu zapisywane: Home Assistant czyta token ze zmiennej
/// środowiskowej <c>SENTINEL_HA_TOKEN</c>, a nie z settings.json.
/// </summary>
public sealed class JarvisSettings
{
    /// <summary>Miasto używane, gdy polecenie nie podaje własnego ("pogoda" bez argumentu).</summary>
    public string DefaultCity { get; set; } = "Kraków";

    /// <summary>Pobieranie prognozy wymaga internetu. Wyłączone = Sentinel nie łączy się
    /// z Open-Meteo wcale i odpowiada wprost, że pogoda jest wyłączona.</summary>
    public bool WeatherEnabled { get; set; } = true;

    /// <summary>Jak długo świeży jest odczyt z cache. Po przekroczeniu odświeżenie jest ponawiane,
    /// a przy braku sieci zwracana jest kopia z jawnym oznaczeniem wieku.</summary>
    public int WeatherCacheMinutes { get; set; } = 30;

    /// <summary>Tryb agenta: model może prosić o wykonanie WYŁĄCZNIE narzędzi z katalogu
    /// tylko-do-odczytu. Domyślnie wyłączone, bo to model decyduje o kolejności kroków.</summary>
    public bool AgentEnabled { get; set; }
    public int AgentMaxSteps { get; set; } = 4;

    /// <summary>Model z obsługą narzędzi (function calling). Pusto = bieżący model rozmowy.</summary>
    public string AgentModel { get; set; } = "";

    /// <summary>Model wizyjny do "co jest na ekranie". Pusto = szukany po nazwie wśród zainstalowanych.</summary>
    public string VisionModel { get; set; } = "";

    /// <summary>Model osadzeń dla indeksu semantycznego (np. nomic-embed-text).</summary>
    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>Wyszukiwanie semantyczne jest opcjonalne i nie zmienia wyszukiwania tekstowego.</summary>
    public bool SemanticSearchEnabled { get; set; }

    /// <summary>Integracja z lokalnym Home Assistantem (bez chmury). Wymaga włączenia i tokena.</summary>
    public bool HomeEnabled { get; set; }
    public string HomeBaseUrl { get; set; } = "";
}

/// <summary>Walidacja pól Jarvis — wywołana z SentinelSettings.Validate.</summary>
public static class JarvisSettingsGuard
{
    public static void Validate(JarvisSettings settings)
    {
        settings.DefaultCity = Clamp(settings.DefaultCity.Trim(), 64);
        settings.WeatherCacheMinutes = Math.Clamp(settings.WeatherCacheMinutes, 5, 720);
        settings.AgentMaxSteps = Math.Clamp(settings.AgentMaxSteps, 1, 8);
        settings.AgentModel = Model(settings.AgentModel);
        settings.VisionModel = Model(settings.VisionModel);
        settings.EmbeddingModel = Model(settings.EmbeddingModel);
        settings.HomeBaseUrl = NormalizeUrl(settings.HomeBaseUrl);
    }

    /// <summary>Nazwa modelu musi przeżyć tę samą kontrolę co pozostałe modele.</summary>
    private static string Model(string value) => value.Trim().Length == 0 || LocalAiService.IsLocalModelName(value) ? value.Trim() : "";

    private static string Clamp(string value, int max) => value.Length <= max ? value : value[..max];

    /// <summary>Adres lokalny http(s)://host:port — bez ścieżki, bez spacji, bez uwierzytelnienia w URL.
    /// Cokolwiek podejrzanego zostaje wyczyszczone do pustego stringu, czyli „nie skonfigurowano”.</summary>
    internal static string NormalizeUrl(string value)
    {
        value = (value ?? "").Trim().TrimEnd('/');
        if (value.Length == 0) return "";
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)) return "";
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return "";
        if (uri.UserInfo.Length > 0) return "";
        if (uri.PathAndQuery.Trim('/').Length > 0) return "";
        if (!Regex.IsMatch(uri.Host, @"^[A-Za-z0-9.\-_]+$")) return "";
        int port = uri.IsDefaultPort ? -1 : uri.Port;
        return port < 0 ? $"{uri.Scheme}://{uri.Host}" : $"{uri.Scheme}://{uri.Host}:{port}";
    }
}
