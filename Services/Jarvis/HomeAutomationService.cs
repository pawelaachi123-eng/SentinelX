using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SentinelX.Services.Jarvis;

/// <summary>Jedna encja z Home Assistanta (tylko to, co potrzebne do polecenia).</summary>
public sealed record HomeEntity(string EntityId, string FriendlyName, string Domain, string State)
{
    /// <summary>Stan „włączony” według HA: on/home/playing albo wartość liczbowa &gt; 0.</summary>
    public bool IsOn => State.Equals("on", StringComparison.OrdinalIgnoreCase) || State.Equals("home", StringComparison.OrdinalIgnoreCase)
        || State.Equals("playing", StringComparison.OrdinalIgnoreCase)
        || (double.TryParse(State, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value) && value > 0);
}

/// <summary>Wynik integracji: tekst dla użytkownika + dowód wywołania.</summary>
public sealed record HomeOutcome(bool Success, string Message, string Evidence)
{
    public static HomeOutcome Ok(string message, string evidence) => new(true, message, evidence);
    public static HomeOutcome Bad(string message, string evidence = "") => new(false, message, evidence);
}

/// <summary>
/// 0.96 · inteligentny dom lokalnie: Home Assistant przez REST u Ciebie w sieci — bez chmury i bez
/// kont. Token NIE jest zapisywany w ustawieniach: czytamy go ze zmiennej środowiskowej
/// SENTINEL_HA_TOKEN, więc poświadczenie nie ląduje w jawnym JSON-ie (decyzja z backlogu o poświadczeniach).
/// Dopuszczalne są wyłącznie trzy usługi: light/switch/… turn_on, turn_off oraz scene.turn_on.
/// Żadnej dowolnej usługi z argumentami z polecenia — model językowy tego katalogu nie zobaczy.
/// </summary>
public sealed class HomeAutomationService
{
    internal const string TokenVariable = "SENTINEL_HA_TOKEN";
    internal static readonly string[] AllowedServices = ["light.turn_on", "light.turn_off", "switch.turn_on", "switch.turn_off",
        "input_boolean.turn_on", "input_boolean.turn_off", "fan.turn_on", "fan.turn_off", "media_player.turn_on",
        "media_player.turn_off", "vacuum.turn_on", "vacuum.turn_off", "humidifier.turn_on", "humidifier.turn_off", "scene.turn_on",
        "homeassistant.turn_on", "homeassistant.turn_off"];
    internal static readonly string[] SwitchableDomains = ["light", "switch", "input_boolean", "fan", "media_player", "vacuum", "humidifier", "scene"];

    private readonly HttpClient http;
    private readonly Func<JarvisSettings> settings;

    public HomeAutomationService(Func<JarvisSettings>? settings = null, HttpMessageHandler? handler = null)
    {
        this.settings = settings ?? (() => new JarvisSettings());
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(4) };
    }

    public static string Token => Environment.GetEnvironmentVariable(TokenVariable) ?? "";

    /// <summary>Konfiguracja kompletna: włączone + adres + token w zmiennym środowiskowym.</summary>
    public bool IsConfigured
    {
        get
        {
            JarvisSettings config = settings();
            return config.HomeEnabled && config.HomeBaseUrl.Trim().Length > 0 && Token.Length > 0;
        }
    }

    public HomeOutcome ConfigurationProblem()
    {
        JarvisSettings config = settings();
        if (!config.HomeEnabled)
            return HomeOutcome.Bad("Integracja z domem jest WYŁĄCZONA w ustawieniach (Jarvis → „Integracja z domem”).\n" +
                "Nie włączam jej sam, bo steruje fizycznymi urządzeniami.");
        if (config.HomeBaseUrl.Trim().Length == 0)
            return HomeOutcome.Bad("Brak adresu Home Assistanta. Ustaw np. http://192.168.1.20:8123 w Ustawienia → Jarvis.");
        if (Token.Length == 0)
            return HomeOutcome.Bad($"Adres jest, ale brakuje tokenu. Ustaw zmienną środowiskową {TokenVariable} " +
                "(Long-Lived Access Token z profilu HA). Sentinel celowo nie zapisuje tokenów w pliku ustawień.");
        return HomeOutcome.Ok("Konfiguracja kompletna.", "");
    }

    public async Task<HomeOutcome> StatusAsync(CancellationToken token)
    {
        HomeOutcome problem = ConfigurationProblem();
        if (!problem.Success) return problem;
        string baseUrl = settings().HomeBaseUrl.TrimEnd('/');
        try
        {
            using JsonDocument document = await GetJsonAsync(baseUrl + "/api/config", token);
            string location = Read(document.RootElement, "location_name");
            string version = Read(document.RootElement, "version");
            IReadOnlyList<HomeEntity> entities = await ListAsync(token);
            int on = entities.Count(x => x.IsOn);
            return HomeOutcome.Ok($"Dom: połączono · {(location.Length > 0 ? location : "bez nazwy lokacji")} · HA {version}\n" +
                $"Encji: {entities.Count} (włączonych: {on}). Dozwolone akcje: turn_on / turn_off / scene.turn_on.",
                $"GET {baseUrl}/api/config · {entities.Count} encji z /api/states · {DateTimeOffset.Now:O}");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return HomeOutcome.Bad($"Brak odpowiedzi z {baseUrl} w 4 s. Sprawdź, czy Home Assistant działa w sieci lokalnej."); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or UnauthorizedAccessException)
        { return HomeOutcome.Bad("Nie udało się połączyć z Home Assistantem: " + ex.Message); }
    }

    public async Task<IReadOnlyList<HomeEntity>> ListAsync(CancellationToken token)
    {
        if (!IsConfigured) return [];
        try
        {
            using JsonDocument document = await GetJsonAsync(settings().HomeBaseUrl.TrimEnd('/') + "/api/states", token);
            return ParseStates(document.RootElement);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return []; }
    }

    /// <summary>Wyszukuje encję po nazwie i wykonuje jedną dozwoloną usługę. Niejednoznaczność = odmowa.</summary>
    public async Task<HomeOutcome> SetAsync(string what, bool on, CancellationToken token)
    {
        HomeOutcome problem = ConfigurationProblem();
        if (!problem.Success) return problem;
        if (what.Trim().Length == 0) return HomeOutcome.Bad("Powiedz, co mam przełączyć, np. „dom: włącz światło salon”.");

        IReadOnlyList<HomeEntity> entities = await ListAsync(token);
        if (entities.Count == 0) return HomeOutcome.Bad("Nie udało się pobrać listy encji. Sprawdź token i adres w Ustawienia → Jarvis.");
        IReadOnlyList<HomeEntity> matches = Find(entities, what);
        if (matches.Count == 0)
            return HomeOutcome.Bad($"Nie znalazłem encji „{what}”. Wpisz „dom: lista”, aby zobaczyć dostępne encje.");
        if (matches.Count > 1)
            return HomeOutcome.Bad($"„{what}” pasuje do {matches.Count} encji: {string.Join(", ", matches.Take(5).Select(x => "„" + x.FriendlyName + "”"))}.\n" +
                "Podaj dokładniejszą nazwę — nie zgaduję, bo to fizyczne urządzenie.");

        HomeEntity target = matches[0];
        if (!ServiceFor(target, on, out string service, out string reason)) return HomeOutcome.Bad(reason);
        string url = ServiceUrl(settings().HomeBaseUrl, service);
        try
        {
            using var content = new StringContent(JsonSerializer.Serialize(new { entity_id = target.EntityId }), Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            using HttpResponseMessage response = await http.SendAsync(request, token);
            string body = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode)
                return HomeOutcome.Bad($"Home Assistant odrzucił polecenie (HTTP {(int)response.StatusCode}).", DescribeError(body));
            // Uczciwie: HA potwierdza przyjęcie żądania. Stan odczytujemy osobno i mówimy, co zobaczyliśmy.
            HomeEntity? readBack = (await ListAsync(token)).FirstOrDefault(x => x.EntityId == target.EntityId);
            string evidence = $"POST {url} · encja {target.EntityId} · HTTP {(int)response.StatusCode} · {DateTimeOffset.Now:O}";
            if (readBack == null) return new HomeOutcome(true, $"{Verb(on)} {target.FriendlyName} — polecenie wysłane, ale nie udało się odczytać stanu zwrotnie.", evidence + " · brak odczytu zwrotnego");
            string verdict = on && readBack.IsOn || !on && !readBack.IsOn ? "potwierdzone odczytem stanu" : "stan po wywołaniu: „" + readBack.State + "” — może się jeszcze zmieniać";
            return HomeOutcome.Ok($"{Verb(on)} {target.FriendlyName} ({readBack.State}) — {verdict}.", evidence + " · GET /api/states → " + readBack.State);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return HomeOutcome.Bad("Home Assistant nie odpowiedział w 4 s. Polecenie mogło nie dotrzeć — sprawdź w aplikacji HA."); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or UnauthorizedAccessException)
        { return HomeOutcome.Bad("Błąd połączenia z Home Assistantem: " + ex.Message); }
    }

    public async Task<HomeOutcome> DescribeEntitiesAsync(int limit, CancellationToken token)
    {
        HomeOutcome problem = ConfigurationProblem();
        if (!problem.Success) return problem;
        IReadOnlyList<HomeEntity> entities = await ListAsync(token);
        if (entities.Count == 0) return HomeOutcome.Bad("Nie udało się pobrać encji (HA nie odpowiada albo brak uprawnień tokenu).");
        string body = string.Join("\n", entities.Take(Math.Clamp(limit, 1, 40)).Select(x => $"· {x.FriendlyName} — {x.State} [{x.EntityId}]"));
        return HomeOutcome.Ok($"Encje ({entities.Count} łącznie, pokazuję {Math.Min(entities.Count, limit)}):\n{body}",
            $"GET /api/states · {entities.Count} encji · {DateTimeOffset.Now:O}");
    }

    // ------------------------------------------------------------------ czyste funkcje (testowalne bez sieci)

    /// <summary>Parser /api/states: [{entity_id, state, attributes:{friendly_name}}].</summary>
    public static IReadOnlyList<HomeEntity> ParseStates(JsonElement root)
    {
        var list = new List<HomeEntity>();
        if (root.ValueKind != JsonValueKind.Array) return list;
        foreach (JsonElement item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            string id = Read(item, "entity_id");
            if (id.Length == 0 || !id.Contains('.')) continue;
            string state = item.TryGetProperty("state", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
            string friendly = id;
            if (item.TryGetProperty("attributes", out var attributes) && attributes.ValueKind == JsonValueKind.Object)
            {
                string name = Read(attributes, "friendly_name");
                if (name.Length > 0) friendly = name;
            }
            list.Add(new HomeEntity(id, friendly, id[..id.IndexOf('.')], state));
        }
        return list;
    }

    /// <summary>Dopasowanie po nazwie (znormalizowanej). Bez zgadywania przy kilku trafieniach.</summary>
    public static IReadOnlyList<HomeEntity> Find(IReadOnlyList<HomeEntity> entities, string what)
    {
        string needle = ConversationMemoryService.Normalize(what);
        if (needle.Length == 0) return [];
        var direct = new List<HomeEntity>();
        var loose = new List<HomeEntity>();
        foreach (HomeEntity entity in entities)
        {
            string name = ConversationMemoryService.Normalize(entity.FriendlyName);
            string id = ConversationMemoryService.Normalize(entity.EntityId);
            if (name == needle || id == needle || name.Contains(needle, StringComparison.Ordinal) || id.Contains(needle, StringComparison.Ordinal)) direct.Add(entity);
            else if (needle.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 1 && needle.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .All(word => name.Contains(word, StringComparison.Ordinal) || id.Contains(word, StringComparison.Ordinal))) loose.Add(entity);
        }
        return direct.Count > 0 ? direct : loose;
    }

    /// <summary>Wybiera usługę z dozwolonego zestawu. Fałsz = jasny powód odmowy, nigdy cicha zmiana.</summary>
    public static bool ServiceFor(HomeEntity entity, bool on, out string service, out string reason)
    {
        service = ""; reason = "";
        if (!SwitchableDomains.Contains(entity.Domain, StringComparer.Ordinal))
        {
            reason = $"Encja „{entity.FriendlyName}” jest domeny „{entity.Domain}” — Sentinel przełącza tylko: {string.Join(", ", SwitchableDomains)}.\n" +
                "Sterowanie roletami, klimatyzacją czy scenami z parametrami jest poza dozwolonym zestawem.";
            return false;
        }
        if (entity.Domain == "scene" && !on)
        {
            reason = $"Sceny „{entity.FriendlyName}” nie da się „wyłączyć” — scenę się uruchamia. Powiedz: dom: włącz scenę {entity.FriendlyName}.";
            return false;
        }
        service = entity.Domain == "scene" ? "scene.turn_on" : entity.Domain + (on ? ".turn_on" : ".turn_off");
        if (!AllowedServices.Contains(service, StringComparer.Ordinal))
        {
            reason = $"Usługa „{service}” jest poza dozwolonym zestawem.";
            return false;
        }
        return true;
    }

    /// <summary>Adres REST usługi. Walidacja, żeby z ustawień nie dało się wysłać żądania gdziekolwiek.</summary>
    public static string ServiceUrl(string baseUrl, string service)
    {
        string[] parts = service.Split(new[] { '.' }, 2);
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0) throw new ArgumentException("Usługa musi mieć format domena.usluga.", nameof(service));
        return baseUrl.TrimEnd('/') + "/api/services/" + parts[0] + "/" + parts[1];
    }

    private static string Verb(bool on) => on ? "Włączam" : "Wyłączam";

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        using HttpResponseMessage response = await http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)response.StatusCode}");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
    }

    private static string Read(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static string DescribeError(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            string message = Read(document.RootElement, "message");
            if (message.Length > 0) return message;
            return json.Length > 200 ? json[..200] : json;
        }
        catch (JsonException) { return json.Length > 200 ? json[..200] : json; }
    }
}
