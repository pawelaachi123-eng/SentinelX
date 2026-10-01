using System.Globalization;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SentinelX.Services.Engine;

/// <summary>An in-process stand-in for the small part of the Ollama HTTP API that LocalAiService speaks (/api/tags, /api/show, /api/chat,
/// /api/generate). Every call is translated to the built-in llama-server. Nothing listens on port 11434 and Ollama is not needed:
/// the existing, tested routing (light model while gaming, stronger when idle, fallbacks, streaming, reasoning filter) stays untouched.</summary>
public sealed class EngineOllamaFacade : HttpMessageHandler
{
    private readonly IEngineRuntime runtime;
    private readonly HttpClient inner;

    public EngineOllamaFacade(IEngineRuntime runtime, HttpMessageHandler? innerHandler = null)
    {
        this.runtime = runtime;
        inner = new HttpClient(innerHandler ?? new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string path = request.RequestUri?.AbsolutePath ?? "";
        try
        {
            return path switch
            {
                "/api/tags" => Json(HttpStatusCode.OK, Tags()),
                "/api/ps" => Json(HttpStatusCode.OK, new { models = Array.Empty<object>() }),
                "/api/show" => await ShowAsync(request, cancellationToken).ConfigureAwait(false),
                "/api/chat" => await ChatAsync(request, cancellationToken, asGenerate: false).ConfigureAwait(false),
                "/api/generate" => await ChatAsync(request, cancellationToken, asGenerate: true).ConfigureAwait(false),
                _ => Json(HttpStatusCode.NotFound, new { error = "not found" })
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (EngineUnavailableException ex) { return Json(HttpStatusCode.ServiceUnavailable, new { error = ex.Message }); }
        catch (HttpRequestException ex) { return Json(HttpStatusCode.ServiceUnavailable, new { error = "silnik AI nie odpowiada: " + ex.Message }); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        { return Json(HttpStatusCode.BadRequest, new { error = "nieprawidłowe żądanie: " + ex.Message }); }
    }

    // ------------------------------------------------------------------ /api/tags and /api/show

    private object Tags() => new
    {
        models = runtime.InstalledModels().Select(m => new
        {
            name = m.Name,
            model = m.Name,
            modified_at = m.Modified.ToString("O", CultureInfo.InvariantCulture),
            size = m.Size,
            digest = m.Digest,
            details = Details(m)
        }).ToArray()
    };

    private static object Details(EngineModelInfo m) => new
    {
        parent_model = "",
        format = "gguf",
        family = m.Family,
        families = new[] { m.Family },
        parameter_size = m.ParameterSize,
        quantization_level = m.Quantization
    };

    private async Task<HttpResponseMessage> ShowAsync(HttpRequestMessage request, CancellationToken token)
    {
        string text = request.Content == null ? "" : await request.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        string model = JsonNode.Parse(text)?["model"]?.GetValue<string>() ?? "";
        EngineModelInfo? info = Find(model);
        if (info == null) return Json(HttpStatusCode.NotFound, new { error = $"model '{model}' not found" });
        return Json(HttpStatusCode.OK, new
        {
            license = "", modelfile = "", parameters = "", template = "",
            details = Details(info), model_info = new { }, capabilities = new[] { "completion" }
        });
    }

    private EngineModelInfo? Find(string model) =>
        runtime.InstalledModels().FirstOrDefault(m => m.Name.Equals(model, StringComparison.OrdinalIgnoreCase));

    // ------------------------------------------------------------------ /api/chat and /api/generate

    private async Task<HttpResponseMessage> ChatAsync(HttpRequestMessage request, CancellationToken token, bool asGenerate)
    {
        string text = request.Content == null ? "" : await request.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        if (JsonNode.Parse(text) is not JsonObject body) return Json(HttpStatusCode.BadRequest, new { error = "brak treści żądania" });
        string model = body["model"]?.GetValue<string>() ?? "";
        bool stream = body["stream"] is JsonValue streamValue && streamValue.TryGetValue<bool>(out bool streamFlag) && streamFlag;
        TimeSpan keepAlive = ParseKeepAlive(body["keep_alive"]);
        JsonObject? options = body["options"] as JsonObject;

        JsonNode? messages = asGenerate ? BuildMessages(body) : body["messages"];
        if (messages is not JsonArray list || list.Count == 0)
        {
            // Ollama's "unload" call: no messages, keep_alive 0.
            if (keepAlive == TimeSpan.Zero) runtime.Unload();
            return Json(HttpStatusCode.OK, new { model, created_at = Now(), response = "", message = new { role = "assistant", content = "" }, done = true, done_reason = "unload" });
        }

        EngineModelInfo? info = Find(model);
        if (info == null) return Json(HttpStatusCode.NotFound, new { error = $"model '{model}' not found" });

        var payload = new JsonObject
        {
            ["model"] = info.Name,
            ["messages"] = list.DeepClone(),
            ["stream"] = stream,
            ["cache_prompt"] = true
        };
        if (Number(options?["temperature"]) is double temperature) payload["temperature"] = temperature;
        if (Number(options?["top_p"]) is double topP) payload["top_p"] = topP;
        if (Number(options?["num_predict"]) is double predict && predict > 0) payload["max_tokens"] = (int)predict;
        if (Number(options?["repeat_penalty"]) is double repeat) payload["repeat_penalty"] = repeat;
        if (body["think"] is JsonValue thinkValue && thinkValue.TryGetValue<bool>(out bool think) && !think)
            payload["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false };
        int context = (int)Math.Clamp(Number(options?["num_ctx"]) ?? 4096, 1024, 32768);

        EngineEndpoint endpoint = await runtime.AcquireAsync(info.Name, context, token).ConfigureAwait(false);
        using var innerRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint.BaseAddress, "/v1/chat/completions"))
        { Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json") };
        innerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.ApiKey);

        HttpResponseMessage response;
        try { response = await inner.SendAsync(innerRequest, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false); }
        catch
        {
            runtime.Release(info.Name, keepAlive);
            throw;
        }

        if (!response.IsSuccessStatusCode)
        {
            string errorText = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            HttpStatusCode status = response.StatusCode;
            response.Dispose();
            runtime.Release(info.Name, keepAlive);
            return Json(status, new { error = ExtractError(errorText) });
        }

        if (!stream)
        {
            try
            {
                string json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                string answer = ExtractMessage(json);
                return Json(HttpStatusCode.OK, asGenerate
                    ? (object)new { model = info.Name, created_at = Now(), response = answer, done = true, done_reason = "stop" }
                    : new { model = info.Name, created_at = Now(), message = new { role = "assistant", content = answer }, done = true, done_reason = "stop" });
            }
            finally
            {
                response.Dispose();
                runtime.Release(info.Name, keepAlive);
            }
        }

        string modelName = info.Name;
        var content = new NdjsonContent(async (writer, ct) =>
        {
            try
            {
                await using Stream source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var reader = new StreamReader(source, Encoding.UTF8);
                string? line;
                while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
                {
                    if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                    string data = line[5..].Trim();
                    if (data.Length == 0) continue;
                    if (data == "[DONE]") break;
                    string delta = ExtractDelta(data, out string? error);
                    if (error != null) { await WriteLineAsync(writer, new { error }, ct).ConfigureAwait(false); return; }
                    if (delta.Length == 0) continue;
                    await WriteLineAsync(writer, asGenerate
                        ? (object)new { model = modelName, created_at = Now(), response = delta, done = false }
                        : new { model = modelName, created_at = Now(), message = new { role = "assistant", content = delta }, done = false }, ct).ConfigureAwait(false);
                }
                await WriteLineAsync(writer, asGenerate
                    ? (object)new { model = modelName, created_at = Now(), response = "", done = true, done_reason = "stop" }
                    : new { model = modelName, created_at = Now(), message = new { role = "assistant", content = "" }, done = true, done_reason = "stop" }, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is IOException or HttpRequestException or JsonException)
            {
                // a dying engine must surface as a normal error line, not as an exception inside the consumer's read loop
                try { await WriteLineAsync(writer, new { error = "silnik AI przerwał odpowiedź: " + ex.Message }, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception writeFailure) when (writeFailure is InvalidOperationException or OperationCanceledException) { }
            }
            finally
            {
                response.Dispose();
                runtime.Release(modelName, keepAlive);
            }
        });
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    /// <summary>/api/generate carries "system" + "prompt"; the engine only has a chat endpoint.</summary>
    private static JsonArray BuildMessages(JsonObject body)
    {
        var messages = new JsonArray();
        string system = body["system"]?.GetValue<string>() ?? "";
        string prompt = body["prompt"]?.GetValue<string>() ?? "";
        if (system.Length > 0) messages.Add(new JsonObject { ["role"] = "system", ["content"] = system });
        if (prompt.Length > 0) messages.Add(new JsonObject { ["role"] = "user", ["content"] = prompt });
        return messages;
    }

    // ------------------------------------------------------------------ helpers

    internal static TimeSpan ParseKeepAlive(JsonNode? node)
    {
        TimeSpan fallback = TimeSpan.FromMinutes(5);
        if (node is not JsonValue value) return fallback;
        if (value.TryGetValue<string>(out string? raw))
        {
            string text = (raw ?? "").Trim().ToLowerInvariant();
            if (text.Length == 0) return fallback;
            if (text == "0") return TimeSpan.Zero;
            char unit = text[^1];
            string digits = char.IsDigit(unit) ? text : text[..^1];
            if (!double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out double amount)) return fallback;
            if (amount < 0) return TimeSpan.FromHours(24);
            TimeSpan span = unit switch { 'm' => TimeSpan.FromMinutes(amount), 'h' => TimeSpan.FromHours(amount), _ => TimeSpan.FromSeconds(amount) };
            return span > TimeSpan.FromHours(24) ? TimeSpan.FromHours(24) : span;
        }
        if (value.TryGetValue<double>(out double seconds)) return seconds < 0 ? TimeSpan.FromHours(24) : TimeSpan.FromSeconds(Math.Min(seconds, 86400));
        return fallback;
    }

    private static double? Number(JsonNode? node) => node is JsonValue value && value.TryGetValue<double>(out double number) ? number : null;

    private static string Now() => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    private static HttpResponseMessage Json(HttpStatusCode status, object value) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

    private static async Task WriteLineAsync(PipeWriter writer, object value, CancellationToken token) =>
        await writer.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value) + "\n"), token).ConfigureAwait(false);

    internal static string ExtractMessage(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("choices", out JsonElement choices) && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0 && choices[0].TryGetProperty("message", out JsonElement message) && message.ValueKind == JsonValueKind.Object
            && message.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.String)
            return content.GetString() ?? "";
        return "";
    }

    internal static string ExtractDelta(string json, out string? error)
    {
        error = null;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return "";
            if (root.TryGetProperty("error", out JsonElement failure))
            {
                if (failure.ValueKind == JsonValueKind.String) error = failure.GetString();
                else if (failure.ValueKind == JsonValueKind.Object && failure.TryGetProperty("message", out JsonElement m) && m.ValueKind == JsonValueKind.String) error = m.GetString();
                error ??= "błąd silnika AI";
                return "";
            }
            if (root.TryGetProperty("choices", out JsonElement choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0
                && choices[0].TryGetProperty("delta", out JsonElement delta) && delta.ValueKind == JsonValueKind.Object
                && delta.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.String)
                return content.GetString() ?? "";
            return "";
        }
        catch (JsonException) { return ""; }
    }

    private static string ExtractError(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out JsonElement error))
            {
                if (error.ValueKind == JsonValueKind.String) return error.GetString() ?? "błąd silnika AI";
                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out JsonElement message) && message.ValueKind == JsonValueKind.String)
                    return message.GetString() ?? "błąd silnika AI";
            }
        }
        catch (JsonException) { }
        return string.IsNullOrWhiteSpace(json) ? "pusta odpowiedź silnika AI" : json.Length <= 240 ? json : json[..240];
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>A response body that is produced while the consumer is already reading it (live model output).
    /// HttpContent normally buffers everything first; overriding CreateContentReadStreamAsync keeps it streaming.</summary>
    private sealed class NdjsonContent : HttpContent
    {
        private readonly Pipe pipe = new();
        private readonly CancellationTokenSource cancel = new();

        public NdjsonContent(Func<PipeWriter, CancellationToken, Task> produce)
        {
            Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson");
            _ = Task.Run(async () =>
            {
                try { await produce(pipe.Writer, cancel.Token).ConfigureAwait(false); }
                catch (Exception ex) { AppLog.Write(ex); }
                finally { await pipe.Writer.CompleteAsync().ConfigureAwait(false); }
            });
        }

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult(pipe.Reader.AsStream());

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => pipe.Reader.AsStream().CopyToAsync(stream);

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { cancel.Cancel(); }
                catch (ObjectDisposedException) { }
                pipe.Reader.Complete();
            }
            base.Dispose(disposing);
        }
    }
}
