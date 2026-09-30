using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SentinelX;

public sealed record PairedDevice(string Alias, string EntityId, string FriendlyName, string Domain);

/// <summary>
/// Authorized Home Assistant device adapter. It discovers only entities exposed by the paired
/// Home Assistant instance and permits a narrow allowlist of media/light actions. It excludes
/// generic switches and scenes because they may control hazardous or multiple devices. It never
/// scans the LAN, bypasses pairing, or claims success without reading state back.
/// </summary>
public sealed class DeviceControlTool : IDisposable
{
    private const int MaxRegistryBytes = 64 * 1024;
    private const int MaxDiscoveryEntities = 1_000;
    private static readonly HashSet<string> AllowedDomains = new(StringComparer.Ordinal)
        { "light", "media_player", "remote" };
    private readonly HttpClient client;
    private readonly Func<bool> externalNetworkAllowed;
    private readonly Func<bool> persistenceAllowed;
    private readonly string registryPath;
    private readonly ActionHistoryService? history;
    private readonly object gate = new();
    private readonly List<PairedDevice> devices = [];
    private string? lastDeviceId;

    public DeviceControlTool(Func<bool>? externalNetworkAllowed = null, Func<bool>? persistenceAllowed = null,
        ActionHistoryService? history = null, string? baseUrl = null, string? accessToken = null,
        string? registryPath = null, HttpMessageHandler? handler = null)
    {
        this.externalNetworkAllowed = externalNetworkAllowed ?? (() => false);
        this.persistenceAllowed = persistenceAllowed ?? (() => true);
        this.registryPath = registryPath ?? Path.Combine(AppPaths.Root, "Devices", "paired.json");
        this.history = history;
        BaseUri = ValidateBaseUrl(baseUrl ?? Environment.GetEnvironmentVariable("SENTINELX_HOME_ASSISTANT_URL"));
        AccessToken = (accessToken ?? Environment.GetEnvironmentVariable("SENTINELX_HOME_ASSISTANT_TOKEN") ?? "").Trim();
        if (AccessToken.Length > 4096) AccessToken = "";
        client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }, disposeHandler: true)
        { Timeout = TimeSpan.FromSeconds(8) };
        LoadRegistry();
    }

    private Uri? BaseUri { get; }
    private string AccessToken { get; }
    public bool IsConfigured => BaseUri != null && AccessToken.Length >= 20;

    public async Task<string?> TryProcessAsync(string command, CancellationToken token = default)
    {
        string raw = (command ?? "").Trim();
        string query = ConversationMemoryService.Normalize(raw).Trim().TrimEnd('.', '!', '?', ',');
        if (query is "urzadzenia" or "lista urzadzen" or "moje urzadzenia" or "paired devices") return FormatPairedDevices();
        if (query is "wykryj urzadzenia" or "odkryj urzadzenia" or "znajdz urzadzenia" or "device discovery")
            return await DiscoverSummaryAsync(token).ConfigureAwait(false);
        if (query.StartsWith("sparuj ", StringComparison.Ordinal) || query.StartsWith("sparuj urzadzenie ", StringComparison.Ordinal))
            return await PairFromDiscoveryAsync(query, token).ConfigureAwait(false);
        Match forget = Regex.Match(query, @"^(?:zapomnij|usun) urzadzenie (?<alias>.+)$", RegexOptions.CultureInvariant);
        if (forget.Success) return Forget(forget.Groups["alias"].Value);

        DeviceIntent? intent = ParseIntent(query);
        if (intent == null || !LooksLikeDeviceIntent(intent, query)) return null;
        if (!IsConfigured) return "Sterowanie urządzeniami nie jest skonfigurowane. Nie poprosiłem o token na czacie i niczego nie wysłałem. Skonfiguruj autoryzowaną integrację Home Assistant poza rozmową, a potem użyj „wykryj urządzenia”.";
        if (!IsLocalHost(BaseUri!) && !externalNetworkAllowed())
            return "Tryb tylko lokalnie zablokował połączenie z zewnętrznym Home Assistant. Nie wysłałem polecenia.";

        PairedDevice? device = ResolveDevice(intent.Target);
        if (device == null)
        {
            string target = intent.Target.Length == 0 ? "ostatniego urządzenia" : "„" + intent.Target + "”";
            return "Nie mam jednoznacznie sparowanego urządzenia " + target + ". Użyj „wykryj urządzenia”, a potem „sparuj <alias> jako <entity_id>”. Niczego nie wysłałem.";
        }
        return await ExecuteAsync(device, intent, raw, token).ConfigureAwait(false);
    }

    public IReadOnlyList<PairedDevice> GetPairedDevices()
    { lock (gate) return devices.ToArray(); }

    private async Task<string> DiscoverSummaryAsync(CancellationToken token)
    {
        if (!IsConfigured) return "Nie wykrywam urządzeń bez wcześniej skonfigurowanego Home Assistant i jego autoryzowanego tokenu. Sentinel nie skanuje sieci ani nie obchodzi parowania.";
        if (!IsLocalHost(BaseUri!) && !externalNetworkAllowed()) return "Tryb tylko lokalnie zablokował zewnętrzny Home Assistant; discovery nie zostało wykonane.";
        try
        {
            IReadOnlyList<Entity> entities = await GetEntitiesAsync(token).ConfigureAwait(false);
            if (entities.Count == 0) return "Home Assistant nie udostępnił obsługiwanych świateł, odtwarzaczy ani pilotów. Przełączniki i sceny nie są włączone, bo mogą sterować wieloma lub ryzykownymi urządzeniami.";
            return "Urządzenia udostępnione przez sparowany Home Assistant (nie zostały automatycznie sparowane):\n" +
                string.Join("\n", entities.Take(40).Select(x => $"· {x.FriendlyName} — {x.EntityId} ({x.Domain})")) +
                (entities.Count > 40 ? "\n… ograniczono listę do 40 pozycji." : "") +
                "\nSparuj jednoznacznie: „sparuj telewizor jako media_player.salon_tv”.";
        }
        catch (Exception ex) when (IsNetworkFailure(ex))
        { return "Nie udało się odczytać sparowanego Home Assistant; nie wykonano żadnego polecenia. " + SafeError(ex); }
    }

    private async Task<string> PairFromDiscoveryAsync(string query, CancellationToken token)
    {
        if (!IsConfigured) return "Parowanie wymaga oficjalnej integracji Home Assistant i autoryzacji właściciela. Nie zapisuję tokenu w rozmowie.";
        if (!IsLocalHost(BaseUri!) && !externalNetworkAllowed()) return "Tryb tylko lokalnie zablokował parowanie z zewnętrznym Home Assistant.";
        Match manual = Regex.Match(query, @"^sparuj(?: urzadzenie)? (?<alias>.+?) jako (?<entity>[a-z_]+\.[a-z0-9_]+)$", RegexOptions.CultureInvariant);
        string alias;
        string? requestedEntity = null;
        if (manual.Success) { alias = manual.Groups["alias"].Value.Trim(); requestedEntity = manual.Groups["entity"].Value; }
        else alias = query.Replace("sparuj urzadzenie ", "", StringComparison.Ordinal).Replace("sparuj ", "", StringComparison.Ordinal).Trim();
        if (alias.Length is < 2 or > 48 || !Regex.IsMatch(alias, @"^[a-z0-9 _-]+$", RegexOptions.CultureInvariant))
            return "Podaj krótki alias bez danych wrażliwych, np. „sparuj telewizor jako media_player.salon_tv”.";
        try
        {
            IReadOnlyList<Entity> entities = await GetEntitiesAsync(token).ConfigureAwait(false);
            Entity[] matches = requestedEntity != null
                ? entities.Where(x => x.EntityId.Equals(requestedEntity, StringComparison.OrdinalIgnoreCase)).ToArray()
                : entities.Where(x => x.FriendlyName.Contains(alias, StringComparison.OrdinalIgnoreCase) || x.EntityId.Contains(alias, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 0) return "Nie znalazłem takiego encji w stanie udostępnionym przez Home Assistant. Użyj „wykryj urządzenia”; nie dodałem parowania.";
            if (matches.Length > 1) return "Znalazłem kilka pasujących encji. Niczego nie sparowałem; wskaż dokładny entity_id:\n" +
                string.Join("\n", matches.Take(8).Select(x => $"· {x.FriendlyName} — {x.EntityId}"));
            Entity selected = matches[0];
            bool persistenceEnabled = CanPersist();
            bool persisted;
            lock (gate)
            {
                devices.RemoveAll(x => x.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase));
                devices.Add(new PairedDevice(alias, selected.EntityId, selected.FriendlyName, selected.Domain));
                if (devices.Count > 100) devices.RemoveAt(0);
                persisted = SaveRegistryLocked();
                lastDeviceId = selected.EntityId;
            }
            string persistenceNote = persisted
                ? "Trwale zapisano wyłącznie mapowanie aliasu/entity; token pozostaje poza plikiem i rozmową."
                : persistenceEnabled
                    ? "Mapowanie działa w tej sesji, ale trwały zapis się nie powiódł; ponowne sparowanie może być potrzebne. Token nie trafił do pliku ani rozmowy."
                    : "Mapowanie działa tylko w tej sesji (zapis wyłączony); token nie trafił do pliku ani rozmowy.";
            return $"Sparowano alias „{alias}” z {selected.FriendlyName} ({selected.EntityId}). {persistenceNote}";
        }
        catch (Exception ex) when (IsNetworkFailure(ex))
        { return "Nie udało się zweryfikować urządzenia w Home Assistant; nie zmieniłem mapowania. " + SafeError(ex); }
    }

    private async Task<string> ExecuteAsync(PairedDevice device, DeviceIntent intent, string command, CancellationToken token)
    {
        string? actionId = null;
        try
        {
            Entity? before = await GetEntityAsync(device.EntityId, token).ConfigureAwait(false);
            if (before == null) return "Urządzenie nie jest obecnie dostępne w Home Assistant. Nie wysłałem polecenia.";
            string? validation = ValidateIntent(device, before, intent);
            if (validation != null) return validation;
            if (intent.Operation == "VOLUME_DELTA")
            {
                if (!TryNumber(before.Attributes, "volume_level", out double current)) return "Nie mogę odczytać bieżącej głośności; nie zgaduję poziomu i nie zmieniłem urządzenia.";
                intent = intent with { Value = Math.Clamp(current * 100 + intent.Value, 0, 100), Operation = "VOLUME" };
            }
            string service = intent.Operation switch
            {
                "ON" => "turn_on", "OFF" => "turn_off", "MUTE" => "volume_mute", "VOLUME" => "volume_set",
                "SOURCE" => "select_source", "BRIGHTNESS" => "turn_on", _ => ""
            };
            string domain = device.Domain;
            if (service.Length == 0) return "To urządzenie nie ma obsługiwanego, zweryfikowanego polecenia.";
            var data = new Dictionary<string, object?> { ["entity_id"] = device.EntityId };
            switch (intent.Operation)
            {
                case "MUTE": data["is_volume_muted"] = true; break;
                case "VOLUME": data["volume_level"] = Math.Clamp(intent.Value / 100d, 0, 1); break;
                case "SOURCE": data["source"] = intent.Text; break;
                case "BRIGHTNESS": data["brightness_pct"] = (int)Math.Round(Math.Clamp(intent.Value, 0, 100)); break;
            }
            actionId = history?.CreateActionId();
            if (actionId != null) history!.AddRunning(actionId, "DEVICE_CONTROL", SensitiveDataRedactor.Redact(command));
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUri!, $"api/services/{domain}/{service}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
            request.Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                bool serverError = (int)response.StatusCode >= 500;
                string text = serverError
                    ? $"Home Assistant zwrócił błąd serwera (HTTP {(int)response.StatusCode}); polecenie mogło zostać częściowo wykonane. Nie mam potwierdzenia stanu — sprawdź urządzenie przed ponowieniem."
                    : $"Home Assistant odrzucił polecenie (HTTP {(int)response.StatusCode}). Nie zgłaszam wykonania.";
                ActionExecutionResult responseResult = serverError
                    ? ActionExecutionResult.UnverifiedSuccess(text, device.EntityId)
                    : ActionExecutionResult.Failure(text);
                AddHistory(actionId, command, responseResult); return text;
            }
            Entity? after = null;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(attempt == 0 ? 100 : 250, token).ConfigureAwait(false);
                after = await GetEntityAsync(device.EntityId, token).ConfigureAwait(false);
                if (after != null && Verify(intent, after)) break;
            }
            if (after == null || !Verify(intent, after))
            {
                string unverified = "Home Assistant przyjął żądanie, ale odczyt zwrotny nie potwierdził stanu. Nie oznaczam działania jako wykonanego.";
                AddHistory(actionId, command, ActionExecutionResult.UnverifiedSuccess(unverified, device.EntityId));
                return unverified;
            }
            lock (gate) lastDeviceId = device.EntityId;
            string result = $"Zweryfikowano: {device.FriendlyName} — {VerificationText(intent, after)}.";
            AddHistory(actionId, command, ActionExecutionResult.VerifiedSuccess(result, $"{device.EntityId}; stan={after.State}; usługa={domain}.{service}"));
            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (actionId == null) throw;
            string uncertain = "Oczekiwanie przerwano. Żądanie mogło już dotrzeć do urządzenia, ale nie mam odczytu zwrotnego; sprawdź jego stan przed ponowieniem.";
            AddHistory(actionId, command, ActionExecutionResult.UnverifiedSuccess(uncertain, device.EntityId));
            return uncertain;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (IsNetworkFailure(ex))
        {
            string error = actionId == null
                ? "Nie udało się odczytać stanu urządzenia; nie wysłałem polecenia. " + SafeError(ex)
                : "Po wysłaniu żądania utracono połączenie. Polecenie mogło dotrzeć do urządzenia, ale stan nie został potwierdzony; sprawdź go przed ponowieniem. " + SafeError(ex);
            ActionExecutionResult result = actionId == null
                ? ActionExecutionResult.Failure(error)
                : ActionExecutionResult.UnverifiedSuccess(error, device.EntityId);
            AddHistory(actionId, command, result);
            return error;
        }
    }

    private string? ValidateIntent(PairedDevice device, Entity current, DeviceIntent intent)
    {
        if (!AllowedDomains.Contains(device.Domain) || current.Domain != device.Domain)
            return "Ten typ encji nie jest na bezpiecznej liście sterowania.";
        if (intent.Operation is "VOLUME" or "VOLUME_DELTA" or "MUTE" or "SOURCE")
        {
            if (device.Domain != "media_player") return "Głośność, wyciszenie i źródło są obsługiwane tylko dla odtwarzacza multimediów.";
            if (intent.Operation == "SOURCE")
            {
                if (!current.Attributes.TryGetValue("source_list", out JsonElement sources) || sources.ValueKind != JsonValueKind.Array ||
                    !sources.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && string.Equals(x.GetString(), intent.Text, StringComparison.OrdinalIgnoreCase)))
                    return $"Źródło „{intent.Text}” nie występuje na liście wejść urządzenia; niczego nie przełączyłem.";
            }
        }
        if (intent.Operation == "BRIGHTNESS" && device.Domain != "light") return "Jasność można ustawić tylko na sparowanym świetle.";
        return null;
    }

    private static bool Verify(DeviceIntent intent, Entity state) => intent.Operation switch
    {
        "ON" => state.State is not ("off" or "unavailable" or "unknown"),
        "OFF" => state.State == "off",
        "MUTE" => state.Attributes.TryGetValue("is_volume_muted", out JsonElement mute) && mute.ValueKind == JsonValueKind.True,
        "VOLUME" => TryNumber(state.Attributes, "volume_level", out double actual) && Math.Abs(actual * 100 - intent.Value) <= 3,
        "SOURCE" => state.Attributes.TryGetValue("source", out JsonElement source) && source.ValueKind == JsonValueKind.String && string.Equals(source.GetString(), intent.Text, StringComparison.OrdinalIgnoreCase),
        "BRIGHTNESS" => TryNumber(state.Attributes, "brightness", out double brightness) && Math.Abs(brightness / 255 * 100 - intent.Value) <= 4,
        _ => false
    };

    private static string VerificationText(DeviceIntent intent, Entity state) => intent.Operation switch
    {
        "ON" => "włączone (stan „" + state.State + "”) ", "OFF" => "wyłączone",
        "MUTE" => "wyciszone", "VOLUME" => "głośność " + Math.Round(intent.Value) + "%",
        "SOURCE" => "źródło „" + intent.Text + "”", "BRIGHTNESS" => "jasność " + Math.Round(intent.Value) + "%",
        _ => "stan odczytany"
    };

    private async Task<IReadOnlyList<Entity>> GetEntitiesAsync(CancellationToken token)
    {
        using var response = await SendGetAsync("api/states", token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string json = await ReadBoundedAsync(response, 1_000_000, token).ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return [];
        return document.RootElement.EnumerateArray().Take(MaxDiscoveryEntities)
            .Select(ParseEntity).Where(x => x != null && AllowedDomains.Contains(x.Domain)).Cast<Entity>().ToArray();
    }

    private async Task<Entity?> GetEntityAsync(string entityId, CancellationToken token)
    {
        using var response = await SendGetAsync("api/states/" + Uri.EscapeDataString(entityId), token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        string json = await ReadBoundedAsync(response, 64 * 1024, token).ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(json);
        return ParseEntity(document.RootElement);
    }

    private async Task<HttpResponseMessage> SendGetAsync(string path, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseUri!, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
    }

    private static async Task<string> ReadBoundedAsync(HttpResponseMessage response, int maxBytes, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength is long length && length > maxBytes) throw new IOException("Odpowiedź Home Assistant przekracza limit rozmiaru.");
        await using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var buffer = new MemoryStream(Math.Min(maxBytes, 16 * 1024));
        byte[] chunk = new byte[8192]; int read;
        while ((read = await stream.ReadAsync(chunk.AsMemory(), token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes) throw new IOException("Odpowiedź Home Assistant przekracza limit rozmiaru.");
            buffer.Write(chunk, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static Entity? ParseEntity(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("entity_id", out JsonElement idNode) || idNode.ValueKind != JsonValueKind.String) return null;
        string id = idNode.GetString() ?? ""; int dot = id.IndexOf('.');
        if (dot <= 0 || dot == id.Length - 1 || !Regex.IsMatch(id, @"^[a-z_]+\.[a-z0-9_]+$", RegexOptions.CultureInvariant)) return null;
        string domain = id[..dot];
        if (!item.TryGetProperty("state", out JsonElement stateNode) || stateNode.ValueKind != JsonValueKind.String) return null;
        var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (item.TryGetProperty("attributes", out JsonElement attrs) && attrs.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty property in attrs.EnumerateObject().Take(100)) attributes[property.Name] = property.Value.Clone();
        string name = attributes.TryGetValue("friendly_name", out JsonElement friendly) && friendly.ValueKind == JsonValueKind.String
            ? SensitiveDataRedactor.Redact(friendly.GetString() ?? id) : id;
        return new Entity(id, domain, name, stateNode.GetString() ?? "unknown", attributes);
    }

    private bool LooksLikeDeviceIntent(DeviceIntent intent, string query)
    {
        string target = ConversationMemoryService.Normalize(intent.Target);
        lock (gate)
        {
            if (devices.Any(x =>
            {
                string alias = ConversationMemoryService.Normalize(x.Alias);
                return alias.Length >= 2 && (target.Contains(alias, StringComparison.Ordinal) || alias.Contains(target, StringComparison.Ordinal) || query.Contains(alias, StringComparison.Ordinal));
            })) return true;
            if (lastDeviceId != null && (target is "go" or "ja" or "to" or "nim" or "niej" or "teraz")) return true;
        }
        return new[] { "telewizor", "telewizora", "telewizorze", "tv", "swiatlo", "swiatla", "swiatle", "lampa", "lampe", "lampie", "hdmi", "glosnosc", "glosniej", "ciszej", "wycisz", "monitor", "urzadzenie" }
            .Any(word => query.Contains(word, StringComparison.Ordinal));
    }

    private DeviceIntent? ParseIntent(string query)
    {
        Match volume = Regex.Match(query, @"^(?:ustaw )?glosnosc (?<target>.+?) na (?<value>\d{1,3})%?$", RegexOptions.CultureInvariant);
        if (volume.Success && double.TryParse(volume.Groups["value"].Value, out double volumeValue) && volumeValue is >= 0 and <= 100)
            return new("VOLUME", volume.Groups["target"].Value, volumeValue, "");
        Match brightness = Regex.Match(query, @"^(?:ustaw )?(?:jasnosc|swiatlo) (?<target>.*?) na (?<value>\d{1,3})%?$", RegexOptions.CultureInvariant);
        if (brightness.Success && double.TryParse(brightness.Groups["value"].Value, out double lightValue) && lightValue is >= 0 and <= 100)
            return new("BRIGHTNESS", brightness.Groups["target"].Value.Trim(), lightValue, "");
        Match youtube = Regex.Match(query, @"^(?:otworz|uruchom) youtube na (?<target>.+)$", RegexOptions.CultureInvariant);
        if (youtube.Success) return new("SOURCE", youtube.Groups["target"].Value, 0, "YouTube");
        Match source = Regex.Match(query, @"^(?:przelacz|wybierz) (?<target>.+?) na (?<source>hdmi ?\d+|youtube)$", RegexOptions.CultureInvariant);
        if (source.Success) return new("SOURCE", source.Groups["target"].Value, 0, NormalizeSource(source.Groups["source"].Value));
        Match on = Regex.Match(query, @"^(?:wlacz|uruchom|odpal|turn on) (?<target>.+)$", RegexOptions.CultureInvariant);
        if (on.Success) return new("ON", on.Groups["target"].Value, 0, "");
        Match off = Regex.Match(query, @"^(?:wylacz|zgas|turn off) (?<target>.+)$", RegexOptions.CultureInvariant);
        if (off.Success) return new("OFF", off.Groups["target"].Value, 0, "");
        Match mute = Regex.Match(query, @"^(?:wycisz|mute)(?: (?<target>.+))?$", RegexOptions.CultureInvariant);
        if (mute.Success) return new("MUTE", mute.Groups["target"].Success ? mute.Groups["target"].Value : "go", 0, "");
        Match louder = Regex.Match(query, @"^(?:daj )?glosniej(?: (?<target>.+))?$", RegexOptions.CultureInvariant);
        if (louder.Success || query == "turn it up") return new("VOLUME_DELTA", louder.Groups["target"].Success ? louder.Groups["target"].Value : "go", 10, "");
        Match quieter = Regex.Match(query, @"^(?:daj )?ciszej(?: (?<target>.+))?$", RegexOptions.CultureInvariant);
        if (quieter.Success || query == "turn it down") return new("VOLUME_DELTA", quieter.Groups["target"].Success ? quieter.Groups["target"].Value : "go", -10, "");
        return null;
    }

    private static string NormalizeDeviceWord(string target) => target switch
    {
        "telewizora" or "telewizorze" or "telewizorem" => "telewizor",
        "swiatla" or "swiatle" => "swiatlo",
        "lampe" or "lampie" or "lampy" => "lampa",
        "komputerze" => "komputer",
        _ => target
    };

    private PairedDevice? ResolveDevice(string requested)
    {
        lock (gate)
        {
            string target = NormalizeDeviceWord(ConversationMemoryService.Normalize(requested).Trim());
            if (target is "go" or "ja" or "to" or "nim" or "niej" or "teraz" or "")
                return devices.FirstOrDefault(x => x.EntityId == lastDeviceId) ?? (devices.Count == 1 ? devices[0] : null);
            string[] synonyms = target switch
            {
                "tv" or "telewizor" or "telewizorze" => ["tv", "telewizor", "telewizorze"],
                "swiatlo" or "lampa" or "lampy" => ["swiatlo", "lampa", "lampy"],
                "komputer" or "pc" => ["komputer", "pc", "moj pc"],
                _ => [target]
            };
            PairedDevice[] exact = devices.Where(x => synonyms.Contains(ConversationMemoryService.Normalize(x.Alias), StringComparer.Ordinal)).ToArray();
            if (exact.Length == 1) return exact[0];
            PairedDevice[] contained = devices.Where(x => synonyms.Any(term => term.Length > 0 &&
                (term.Contains(ConversationMemoryService.Normalize(x.Alias), StringComparison.Ordinal) ||
                 ConversationMemoryService.Normalize(x.Alias).Contains(term, StringComparison.Ordinal)))).ToArray();
            return contained.Length == 1 ? contained[0] : null;
        }
    }

    private string FormatPairedDevices()
    {
        lock (gate) return devices.Count == 0 ? "Brak sparowanych urządzeń. Użyj „wykryj urządzenia” po skonfigurowaniu oficjalnego Home Assistant."
            : "Sparowane urządzenia:\n" + string.Join("\n", devices.Select(x => $"· {x.Alias} → {x.FriendlyName} ({x.EntityId})"));
    }

    private string Forget(string alias)
    {
        lock (gate)
        {
            int removed = devices.RemoveAll(x => x.Alias.Equals(alias.Trim(), StringComparison.OrdinalIgnoreCase));
            if (removed == 0) return "Nie znalazłem takiego sparowanego aliasu.";
            if (!devices.Any(x => x.EntityId == lastDeviceId)) lastDeviceId = null;
            bool persistenceEnabled = CanPersist();
            bool persisted = SaveRegistryLocked();
            string note = persisted ? "Mapowanie usunięto także z rejestru." : persistenceEnabled
                ? "Nie udało się zaktualizować rejestru; alias może wrócić po restarcie."
                : "Usunięto alias tylko z bieżącej sesji; zapisy są wyłączone.";
            return "Usunięto mapowanie „" + alias.Trim() + "”. " + note + " Nie zmieniłem konfiguracji samego urządzenia.";
        }
    }

    private void LoadRegistry()
    {
        if (!CanPersist()) return;
        try
        {
            var info = new FileInfo(registryPath);
            if (!info.Exists || info.Length > MaxRegistryBytes) return;
            var loaded = JsonSerializer.Deserialize<List<DeviceRegistryEntry>>(File.ReadAllText(registryPath)) ?? [];
            foreach (DeviceRegistryEntry item in loaded.Take(100))
                if (item != null && item.Alias.Length is >= 2 and <= 48 && Regex.IsMatch(item.Alias, @"^[a-z0-9 _-]+$", RegexOptions.CultureInvariant) &&
                    AllowedDomains.Contains(item.Domain) && Regex.IsMatch(item.EntityId, @"^[a-z_]+\.[a-z0-9_]+$", RegexOptions.CultureInvariant))
                    devices.Add(new PairedDevice(item.Alias, item.EntityId, item.Alias, item.Domain));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
    }

    private bool SaveRegistryLocked()
    {
        if (!CanPersist()) return false;
        string temp = registryPath + ".tmp";
        try
        {
            string? directory = Path.GetDirectoryName(registryPath);
            if (string.IsNullOrWhiteSpace(directory)) return false;
            Directory.CreateDirectory(directory);
            string json = JsonSerializer.Serialize(devices.Select(x => new DeviceRegistryEntry { Alias = x.Alias, EntityId = x.EntityId, Domain = x.Domain }));
            if (Encoding.UTF8.GetByteCount(json) > MaxRegistryBytes) return false;
            File.WriteAllText(temp, json);
            File.Move(temp, registryPath, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { try { File.Delete(temp); } catch { } return false; }
    }

    private bool CanPersist() { try { return persistenceAllowed(); } catch { return false; } }

    private void AddHistory(string? id, string command, ActionExecutionResult result)
    {
        if (id != null) history?.AddResult(id, "DEVICE_CONTROL", SensitiveDataRedactor.Redact(command), result);
    }

    private static bool TryNumber(IReadOnlyDictionary<string, JsonElement> attributes, string name, out double value)
    {
        value = 0;
        return attributes.TryGetValue(name, out JsonElement node) && node.ValueKind == JsonValueKind.Number && node.TryGetDouble(out value) && double.IsFinite(value);
    }

    private static string NormalizeSource(string source)
    {
        Match hdmi = Regex.Match(source, @"hdmi ?(\d+)", RegexOptions.CultureInvariant);
        return hdmi.Success ? "HDMI " + hdmi.Groups[1].Value : "YouTube";
    }

    private static Uri? ValidateBaseUrl(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            return null;
        if (uri.Scheme == "http" && !IsLocalHost(uri)) return null;
        string baseText = uri.AbsoluteUri.EndsWith('/') ? uri.AbsoluteUri : uri.AbsoluteUri + "/";
        return new Uri(baseText, UriKind.Absolute);
    }

    private static bool IsLocalHost(Uri uri)
    {
        string host = uri.Host;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            new[] { ".local", ".home", ".lan" }.Any(suffix => host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) ||
            !host.Contains('.')) return true;
        if (!IPAddress.TryParse(host, out IPAddress? ip)) return false;
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            byte[] v6 = ip.GetAddressBytes();
            return ip.IsIPv6LinkLocal || (v6[0] & 0xfe) == 0xfc;
        }
        byte[] b = ip.GetAddressBytes();
        return b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 169 && b[1] == 254;
    }

    private static bool IsNetworkFailure(Exception ex) => ex is HttpRequestException or IOException or JsonException or TaskCanceledException or UriFormatException;
    private static string SafeError(Exception ex) => SensitiveDataRedactor.Redact(ex.Message.Length > 180 ? ex.Message[..180] : ex.Message);

    public void Dispose() => client.Dispose();

    private sealed record Entity(string EntityId, string Domain, string FriendlyName, string State,
        IReadOnlyDictionary<string, JsonElement> Attributes);
    private sealed class DeviceRegistryEntry
    {
        public DeviceRegistryEntry() { }
        public string Alias { get; set; } = "";
        public string EntityId { get; set; } = "";
        public string Domain { get; set; } = "";
    }
    private sealed record DeviceIntent(string Operation, string Target, double Value, string Text);
}
