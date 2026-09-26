using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;

using SentinelX.Services.Actions;

namespace SentinelX.Services.Jarvis;

/// <summary>
/// 0.96 · tryb agenta: lokalny model dostaje katalog narzędzi tylko-do-odczytu i może prosić
/// o ich wykonanie. To NIE jest „model robi, co chce”:
///  • model widzi wyłącznie <see cref="AgentToolCatalog.Tools"/> — żadnego usuwania, zamykania, zmian;
///  • każde żądanie idzie przez <see cref="StepRunner"/>, czyli przez pełny potok z STOP-em,
///    centrum zgód, audytem i korelacją requestId — tak samo jak wpisane polecenie;
///  • liczba kroków jest ograniczona (domyślnie 4), a transkrypcja każdego kroku jest pokazana;
///  • model nie ma dostępu do decyzji „potwierdź” — zgoda pozostaje kliknięciem w oknie.
/// Transport i parsowanie są testowane na wstrzykniętym HTTP (tests/JarvisRegression.cs);
/// z żywym modelem sprawdza to użytkownik, bo CI nie ma Ollamy.
/// </summary>
public sealed class AgentService
{
    private const int MaxToolResultCharacters = 1800;
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(90);

    private readonly HttpClient http;
    private readonly LocalAiService local;
    private readonly StepRunner steps;
    private readonly Func<AiSettings?> aiSettings;
    private readonly Func<JarvisSettings> jarvis;

    public AgentService(StepRunner steps, LocalAiService local, Func<JarvisSettings>? jarvis = null,
        Func<AiSettings?>? aiSettings = null, HttpMessageHandler? handler = null)
    {
        this.steps = steps; this.local = local;
        this.jarvis = jarvis ?? (() => new JarvisSettings());
        this.aiSettings = aiSettings ?? (() => null);
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        { BaseAddress = new Uri("http://127.0.0.1:11434"), Timeout = CallTimeout };
    }

    public bool IsReady => steps.IsReady;

    /// <summary>Uruchamia pętlę. Zwraca gotową odpowiedź z transkrypcją kroków.</summary>
    public async Task<string> RunAsync(string goal, Action<string>? onProgress, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(goal)) return "Powiedz, co mam zrobić, np. „agent: sprawdź, czy dysk ma miejsce i czy karta nie grzeje”.";
        if (!steps.IsReady)
            return "Agent nie jest podłączony do kolejki wykonań (StepRunner). Uruchom Sentinel w normalnym oknie i spróbuj ponownie.";
        JarvisSettings config = jarvis();
        if (!config.AgentEnabled)
            return "Tryb agenta jest WYŁĄCZONY — i tak ma zostać, dopóki nie zdecydujesz inaczej.\n" +
                "Włącz go w Ustawienia → Jarvis → „Tryb agenta”.\n\nBez niego i tak możesz wydawać polecenia po jednym — model nie wykonuje nic sam z siebie.";

        int maxSteps = Math.Clamp(config.AgentMaxSteps, 1, 8);
        string? model = await ResolveModelAsync(config, token);
        if (model == null)
            return "Brak lokalnego modelu do pracy agenta. Wpisz: modele AI (uruchom Ollama).";

        var messages = new List<Dictionary<string, object?>>
        {
            new() { ["role"] = "system", ["content"] = BuildSystemPrompt(maxSteps) },
            new() { ["role"] = "user", ["content"] = goal.Trim() },
        };
        var transcript = new StringBuilder();
        int calls = 0, executed = 0;
        string answer = "";

        for (int step = 1; step <= maxSteps; step++)
        {
            token.ThrowIfCancellationRequested();
            ChatTurn turn;
            try { turn = await ChatAsync(model, messages, token); }
            catch (AgentToolNotSupportedException ex) { return ex.Message; }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { return "Ollama nie odpowiedziała w 90 s. Agent zatrzymany — nic więcej nie zostało wysłane."; }
            catch (HttpRequestException) { return "Brak połączenia z Ollama (127.0.0.1:11434). Uruchom Ollama i ponów polecenie."; }

            calls++;
            if (turn.ToolCalls.Count == 0)
            {
                answer = turn.Content.Length > 0 ? turn.Content : "Model nie odpowiedział nic i nie poprosił o narzędzie.";
                transcript.Append(step == 1 ? "" : $"\n· krok {step}: model zakończył bez nowych narzędzi");
                break;
            }

            messages.Add(new Dictionary<string, object?>
            {
                ["role"] = "assistant", ["content"] = turn.Content,
                ["tool_calls"] = turn.RawToolCalls
            });

            foreach (AgentToolCall call in turn.ToolCalls)
            {
                token.ThrowIfCancellationRequested();
                AgentTool? tool = AgentToolCatalog.Find(call.Name);
                string label = tool == null ? "ODRZUCONE" : tool.Name;
                string command = tool?.BuildCommand(call.Argument) ?? "";
                if (tool == null)
                {
                    string rejected = $"Narzędzie odrzucone. Model poprosił o „{call.Name}”, a takiego narzędzia nie ma w katalogu dozwolonym dla agenta. " +
                        "Nic nie zostało wykonane. Jeśli to była operacja zmieniająca dane — tak ma zostać: agent nie ma do nich dostępu.";
                    transcript.Append($"\n⛔ {rejected}");
                    messages.Add(ToolMessage(call, rejected));
                    onProgress?.Invoke("\n\n⛔ odrzucone żądanie narzędzia: " + call.Name);
                    continue;
                }

                transcript.Append($"\n· krok {step}: {label} → „{command}”");
                onProgress?.Invoke("\n\n· wykonuję: " + command);
                StepOutcome outcome = await steps.RunAsync(command, token);
                executed++;
                if (!outcome.Success)
                {
                    string blocked = "Krok zablokowany (" + (outcome.Status.Length > 0 ? outcome.Status : "bez rekordu") + "): " + outcome.Text;
                    transcript.Append("\n⛔ " + blocked);
                    messages.Add(ToolMessage(call, blocked));
                    onProgress?.Invoke("\n\n⛔ " + blocked);
                    continue;
                }
                string result = outcome.Text.Length > MaxToolResultCharacters
                    ? outcome.Text[..MaxToolResultCharacters] + "\n[ucięte dla limitu kontekstu]" : outcome.Text;
                string proof = outcome.Verified ? "dowód: tak" : "dowód: brak (polecenie wysłane, skutek niepotwierdzony)";
                transcript.Append($"\n   ↩ {Shorten(result, 300)}\n   {proof} · {outcome.ActionId}");
                messages.Add(ToolMessage(call, result));
                onProgress?.Invoke("\n   ↩ " + Shorten(result, 300));
            }
        }

        if (answer.Length == 0)
            answer = $"Agent wykorzystał limit {maxSteps} kroków i nie zdążył sformułować odpowiedzi. " +
                "Zwiększ limit w ustawieniach albo rozbij zadanie na dwa polecenia.";

        string header = $"🤖 AGENT · model {model}\nCel: {goal.Trim()}\nWywołania: {calls}, wykonane kroki: {executed}, limit: {maxSteps}";
        string body = transcript.Length == 0 ? "\nModel nie poprosił o żadne narzędzie — odpowiedział od razu." : transcript.ToString();
        return header + body + "\n\n" + answer +
            "\n\nKażdy krok poszedł przez kolejkę z STOP-em i audytem. Agent nie ma narzędzi zmieniających dane ani dostępu do zgody „potwierdź”.";
    }

    /// <summary>Statystyki do karty w interfejsie.</summary>
    public string StatusText()
    {
        JarvisSettings config = jarvis();
        if (!config.AgentEnabled) return "Tryb agenta wyłączony w ustawieniach.";
        if (!steps.IsReady) return "Tryb agenta włączony, ale kolejka wykonań nie jest podłączona.";
        return $"Tryb agenta gotowy · {AgentToolCatalog.Tools.Count} narzędzi tylko-do-odczytu · limit {Math.Clamp(config.AgentMaxSteps, 1, 8)} kroków" +
            (config.AgentModel.Length > 0 ? $" · model {config.AgentModel}" : " · model bieżący");
    }

    // ------------------------------------------------------------------ HTTP + parsowanie

    internal sealed record ChatTurn(string Content, IReadOnlyList<AgentToolCall> ToolCalls, JsonElement RawToolCalls);

    private async Task<ChatTurn> ChatAsync(string model, List<Dictionary<string, object?>> messages, CancellationToken token)
    {
        using var content = new StringContent(BuildBody(model, messages, AgentToolCatalog.Tools, aiSettings()), Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat") { Content = content };
        using HttpResponseMessage response = await http.SendAsync(request, token);
        string json = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
        {
            string error = ExtractError(json);
            if (error.Contains("tool", StringComparison.OrdinalIgnoreCase) || error.Contains("does not support", StringComparison.OrdinalIgnoreCase))
                throw new AgentToolNotSupportedException("Model " + model + " nie obsługuje narzędzi (function calling): " + error +
                    "\nWybierz model z obsługą narzędzi, np. qwen3:4b albo llama3.1:8b, i wpisz: agent: … ponownie.");
            throw new AgentToolNotSupportedException("Ollama odrzuciła żądanie agenta (HTTP " + (int)response.StatusCode + "): " + error);
        }
        return ParseTurn(json);
    }

    /// <summary>Ciało żądania /api/chat z polem tools. Czyste — testy porównują dokładny JSON.</summary>
    public static string BuildBody(string model, List<Dictionary<string, object?>> messages, IReadOnlyList<AgentTool> tools, AiSettings? config)
    {
        var body = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = messages,
            ["stream"] = false,
            ["keep_alive"] = "5m",
            ["tools"] = tools.Select(x => x.ToDefinition()).ToArray(),
            ["options"] = new Dictionary<string, object>
            {
                ["temperature"] = config?.Temperature ?? 0.1,
                ["num_predict"] = Math.Clamp(config?.MaxResponseTokens ?? 700, 128, 2048),
                ["num_ctx"] = Math.Clamp(config?.MaxContextTokens ?? 4096, 1024, 32768),
                ["top_p"] = 0.9,
            }
        };
        if (model.StartsWith("qwen3", StringComparison.OrdinalIgnoreCase)) body["think"] = false;
        return JsonSerializer.Serialize(body);
    }

    /// <summary>Wyciąga message.content i message.tool_calls. Znosi zarówno `arguments` jako obiekt,
    /// jak i jako string (OpenAI-owy kształt, który część modeli zwraca).</summary>
    internal static ChatTurn ParseTurnForTest(string json) => ParseTurn(json);

    private static ChatTurn ParseTurn(string json)
    {
        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return new ChatTurn("", [], default);
        if (root.TryGetProperty("error", out var failure) && failure.ValueKind == JsonValueKind.String)
            throw new AgentToolNotSupportedException("Ollama: " + (failure.GetString() ?? "błąd"));
        if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
            return new ChatTurn("", [], default);
        string content = message.TryGetProperty("content", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() ?? "" : "";
        var calls = new List<AgentToolCall>();
        JsonElement raw = default;
        if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
        {
            raw = toolCalls.Clone();
            int index = 0;
            foreach (JsonElement call in toolCalls.EnumerateArray())
            {
                index++;
                if (call.ValueKind != JsonValueKind.Object) continue;
                string id = call.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String ? idElement.GetString() ?? "" : "";
                if (id.Length == 0) id = "call_" + index.ToString(CultureInfo.InvariantCulture);
                if (!call.TryGetProperty("function", out var function) || function.ValueKind != JsonValueKind.Object) continue;
                string name = function.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String ? nameElement.GetString() ?? "" : "";
                string arguments = "";
                if (function.TryGetProperty("arguments", out var args))
                    arguments = args.ValueKind == JsonValueKind.String ? args.GetString() ?? "" : args.GetRawText();
                calls.Add(new AgentToolCall(id, name, arguments));
            }
        }
        return new ChatTurn(content, calls, raw);
    }

    private static Dictionary<string, object?> ToolMessage(AgentToolCall call, string content) => new()
    {
        ["role"] = "tool",
        ["tool_call_id"] = call.Id,
        ["content"] = content,
    };

    private async Task<string?> ResolveModelAsync(JarvisSettings config, CancellationToken token)
    {
        string preferred = config.AgentModel.Trim();
        IReadOnlyList<string> installed;
        try { installed = await local.GetInstalledModelsAsync(token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        { return preferred.Length > 0 ? preferred : null; }
        if (preferred.Length > 0 && installed.Contains(preferred, StringComparer.OrdinalIgnoreCase)) return preferred;
        string last = local.LastModel;
        if (last.Length > 0 && installed.Contains(last, StringComparer.OrdinalIgnoreCase)) return last;
        return installed.FirstOrDefault(x => x.StartsWith("qwen3", StringComparison.OrdinalIgnoreCase)) ?? installed.FirstOrDefault();
    }

    private static string ExtractError(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                return error.GetString() ?? "";
        }
        catch (JsonException) { }
        return json.Length > 240 ? json[..240] : json;
    }

    private static string Shorten(string text, int max)
    {
        string single = text.Replace("\n", " ", StringComparison.Ordinal);
        return single.Length <= max ? single : single[..max] + "…";
    }

    private static string BuildSystemPrompt(int maxSteps) =>
        """
        Jesteś asystentem-systemowym SENTINEL X w trybie agenta. Zasady, których nie wolno ci obejść:
        1. Masz DOZWOLONE narzędzia z listy tools. Nie wymyślasz żadnych innych i nie prosisz o nie.
        2. Narzędzia są tylko do odczytu. Nie usuwasz, nie nadpisujesz, nie zamykasz aplikacji, nie zmieniasz ustawień.
        3. Nie masz dostępu do zgody „potwierdź”. Jeśli zadanie wymaga zmiany danych, odpowiedz to zdaniem: „Ta operacja wymaga Twojej decyzji w oknie Sentinel — podaję, co trzeba kliknąć.”
        4. Najpierw zbierz dane narzędziami, potem odpowiedz. Maksymalnie %MAXSTEPS% wywołań narzędzi.
        5. Odpowiadaj po polsku, krótko, liczbami z narzędzi — nie zgaduj wartości, których nie odczytałeś.
        """.Replace("%MAXSTEPS%", maxSteps.ToString(CultureInfo.InvariantCulture));
}

/// <summary>Model nie umie narzędzi albo Ollama odmówiła — komunikat idzie prosto do użytkownika.</summary>
public sealed class AgentToolNotSupportedException(string message) : Exception(message);
