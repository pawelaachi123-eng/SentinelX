using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace SentinelX.Tests;

/// <summary>Streaming answers over an injected transport: chunk order, hidden-reasoning filtering, stop semantics, fallback.</summary>
internal static class AiStreamRegression
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static HttpResponseMessage Json(object value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Ndjson(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/x-ndjson") };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request, cancellationToken);
    }

    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        string[] models = ["qwen3:4b", "gemma3:1b"];
        var config = new AiSettings
        {
            IdleModel = "qwen3:4b", GamingModel = "qwen3:4b", FallbackModel = "gemma3:1b",
            Temperature = 0.3, MaxContextTokens = 3072, MaxResponseTokens = 220
        };
        LocalAiService Create(HttpMessageHandler handler) => new(new GamingModeService(), handler,
            Path.Combine(directory, "settings-" + Guid.NewGuid().ToString("N")), aiSettingsProvider: () => config);
        HttpResponseMessage Tags() => Json(new { models = models.Select(x => new { name = x }) });

        // --- stream line parser ---
        Check(LocalAiService.ExtractStreamDelta("{\"message\":{\"content\":\"cze\"}}", out _) == "cze", "chat delta is extracted");
        Check(LocalAiService.ExtractStreamDelta("{\"response\":\"ść\"}", out _) == "ść", "generate delta is extracted");
        Check(LocalAiService.ExtractStreamDelta("{\"done\":true}", out _) == "", "a terminal line carries no text");
        Check(LocalAiService.ExtractStreamDelta("{\"error\":\"model not found\"}", out string streamError) == "" && streamError == "model not found",
            "a stream error line is reported instead of ignored");
        Check(LocalAiService.ExtractStreamDelta("to nie jest json", out _) == "", "a malformed line does not crash the stream");

        // --- hidden reasoning never reaches the UI, even split across chunks ---
        var filter = new StreamThinkFilter();
        Check(filter.Push("Widoczny początek. ") == "Widoczny początek. ", "plain text passes through");
        Check(filter.Push("<thi") == "", "half of an opening tag is held back");
        Check(filter.Push("nk>ukryte rozumowanie ") == "", "hidden reasoning is filtered out");
        Check(filter.Push("nadal ukryte</thi") == "", "hidden reasoning continues to be filtered");
        Check(filter.Push("nk> teraz widoczne") == " teraz widoczne", "text after the closing tag is visible again");
        Check(filter.Flush() == "", "flush does not leak held-back fragments");
        var tail = new StreamThinkFilter();
        Check(tail.Push("końcówka<th") == "końcówka", "a possible tag prefix is held until it is resolved");
        Check(tail.Flush() == "<th", "flush releases text that turned out not to be a tag");
        Check(StreamThinkFilter.HeldBackLength("abc<thi", "<think>") == 4, "held-back length is the longest tag prefix at the end");
        Check(StreamThinkFilter.HeldBackLength("x<thin", "<think>") == 5, "a longer tag prefix is held back in full");
        Check(StreamThinkFilter.HeldBackLength("abc", "<think>") == 0, "plain text holds nothing back");

        // --- end to end: chunks arrive in order and assemble into the final answer ---
        var received = new List<string>();
        using (var service = Create(new Handler(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/tags") return Tags();
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Check(body.RootElement.GetProperty("stream").GetBoolean(), "a streaming request asks Ollama for a stream");
            return Ndjson("{\"message\":{\"content\":\"Pierwsza \"}}\n{\"message\":{\"content\":\"część \"}}\n{\"message\":{\"content\":\"odpowiedzi.\"}}\n{\"done\":true}\n");
        })))
        {
            string answer = await service.AskAsync("test strumienia", "", default, received.Add);
            Check(answer == "Pierwsza część odpowiedzi.", "streamed chunks assemble into the final answer");
            Check(received.Count == 3 && string.Concat(received) == answer, "every chunk reached the consumer in order");
            Check(service.LastResponseSucceeded && service.LastModel == "qwen3:4b", "a streamed answer counts as a real response");
            Check(!service.IsStreaming, "streaming state is cleared when the answer is complete");
        }

        // --- a non-streaming caller still gets one whole answer ---
        using (var service = Create(new Handler(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/tags") return Tags();
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Check(!body.RootElement.GetProperty("stream").GetBoolean(), "without a consumer the request stays non-streaming");
            return Json(new { message = new { content = "Zwykła odpowiedź." } });
        })))
        {
            Check(await service.AskAsync("zwykłe pytanie") == "Zwykła odpowiedź.", "the plain path is unchanged");
        }

        // --- stop mid-generation keeps the partial answer and is not marked successful ---
        using (var service = Create(new Handler(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/tags") return Tags();
            await request.Content!.ReadAsStringAsync(token);
            return Ndjson("{\"message\":{\"content\":\"Zaczęte zdanie\"}}\n{\"message\":{\"content\":\" i jego dalszy ciąg\"}}\n{\"done\":true}\n");
        })))
        {
            var seen = new List<string>();
            bool cancelled = false;
            using var stop = new CancellationTokenSource();
            try { await service.AskAsync("długie pytanie", "", stop.Token, chunk => { seen.Add(chunk); stop.Cancel(); }); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "cancelling inside the stream surfaces as a cancellation");
            Check(seen.Count == 1 && seen[0] == "Zaczęte zdanie", "only the chunks produced before the stop were published");
            Check(service.LastPartialAnswer == "Zaczęte zdanie", "the partial answer is kept instead of silently dropped");
            Check(!service.LastResponseSucceeded, "a stopped generation is never reported as a successful answer");
            Check(!service.IsStreaming, "streaming state is cleared after a stop");
        }

        // --- a broken stream falls back to the next model instead of inventing an answer ---
        int chatCalls = 0;
        using (var service = Create(new Handler(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/tags") return Tags();
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            string model = body.RootElement.GetProperty("model").GetString()!;
            bool streaming = body.RootElement.GetProperty("stream").GetBoolean();
            if (model == "qwen3:4b")
            {
                Check(streaming, "the first attempt must stream when a consumer is attached");
                return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{\"error\":\"przeciążony\"}", Encoding.UTF8) };
            }
            chatCalls++;
            return Json(new { message = new { content = "Odpowiedź zapasowa." } });
        })))
        {
            var fallbackChunks = new List<string>();
            string answer = await service.AskAsync("pytanie z awarią", "", default, fallbackChunks.Add);
            Check(answer == "Odpowiedź zapasowa.", "a failing stream falls back to the next local model");
            Check(fallbackChunks.Count == 0, "a retry does not append a second answer to the live bubble");
            Check(chatCalls == 1 && service.LastModel == "gemma3:1b", "the fallback model produced the answer");
            Check(service.LastAttemptedModels.Contains("qwen3:4b"), "the attempted models are recorded honestly");
        }

        // --- an error line inside a stream is not presented as an answer ---
        using (var service = Create(new Handler(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/tags") return Tags();
            await request.Content!.ReadAsStringAsync(token);
            return Ndjson("{\"error\":\"model wymaga pobrania\"}\n");
        })))
        {
            string answer = await service.AskAsync("pytanie", "", default, _ => { });
            Check(!service.LastResponseSucceeded && answer.Contains("Nie udało się"), "a stream that only reports an error is not shown as an answer");
            Check(service.LastFallbackReason.Contains("model wymaga pobrania"), "the model's own error text is preserved for the user");
        }
    }
}
