using System.Net;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace SentinelX;

/// <summary>Deterministic transport tests: no model download, no real inference and no machine actions.</summary>
internal static class AiReliabilityTestRunner
{
    internal static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<object>();
        void Check(string name, bool passed)
        {
            checks.Add(new { name, passed });
            if (!passed) throw new InvalidOperationException("AI regression failed: " + name);
        }
        string[] models = ["qwen3:4b-instruct", "gemma3:1b", "qwen3:1.7b", "nomic-embed-text:latest", "qwen3:cloud"];
        var config = new AiSettings { GamingModel = "qwen3:4b-instruct", IdleModel = "qwen3:4b-instruct", FallbackModel = "gemma3:1b", Temperature = .47, MaxContextTokens = 3072, MaxResponseTokens = 180 };
        LocalAiService Create(FakeHandler handler, TimeSpan? timeout = null) => new(new GamingModeService(), handler,
            Path.Combine(directory, "settings-" + Guid.NewGuid().ToString("N")), aiSettingsProvider: () => config, requestTimeout: timeout);
        HttpResponseMessage Tags() => Json(new { models = models.Select(x => new { name = x }) });
        async Task<string> RequestModel(HttpRequestMessage request)
        {
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            return doc.RootElement.GetProperty("model").GetString()!;
        }
        try
        {
            Check("routing honors custom idle model", LocalAiService.SelectModel(models, false, "", new AiSettings { IdleModel = "gemma3:1b" }) == "gemma3:1b");
            Check("routing honors custom gaming model", LocalAiService.SelectModel(models, true, "", new AiSettings { GamingModel = "gemma3:1b" }) == "gemma3:1b");
            Check("cloud and embedding excluded", LocalAiService.SelectModels(models, false, "").All(x => !x.Contains("cloud") && !x.Contains("embed")));
            Check("pressure uses configured thresholds", LocalAiService.EvaluatePressure("", 72, 20, 10, new AiSettings { RamPressurePercent = 70 }, out var reason) && reason.Contains("RAM"));
            Check("unavailable sensor is not pressure", !LocalAiService.EvaluatePressure("", double.NaN, double.NaN, double.NaN, config, out _));
            Check("unavailable game detection selects conservative mode", LocalAiService.EvaluatePressure("", double.NaN, double.NaN, double.NaN,
                config, false, out reason) && reason.Contains("niedostępne"));
            Check("game activates light mode", LocalAiService.EvaluatePressure("test-game", 20, 20, 20, config, out reason) && reason.Contains("test-game"));
            Check("unrelated old context removed", !LocalAiService.BuildUserPrompt("Dlaczego kruki zostają w Polsce?", "Stary tekst: Microsoft pomaga w pracy naprawić internet.", false).Contains("Microsoft"));
            Check("follow-up retains context", LocalAiService.BuildUserPrompt("Rozwiń poprzedni temat", "Microsoft pomaga w pracy naprawić internet.", false).Contains("Microsoft"));
            Check("profile survives topic change", LocalAiService.BuildUserPrompt("Dlaczego niebo jest niebieskie?", "name: Paweł\nresponseStyle: krótkie odpowiedzi", false).Contains("Paweł"));
            Check("question is last", LocalAiService.BuildUserPrompt("Jak działa RAM?", "RAM: 55%", false).EndsWith("Jak działa RAM?", StringComparison.Ordinal));
            Check("hidden think text removed", LocalAiService.CleanAnswer("<think>private</think> Gotowe.") == "Gotowe.");

            int optionRequests = 0;
            using (var service = Create(new FakeHandler(async (request, token) =>
            {
                Check("loopback transport", request.RequestUri!.Host == "127.0.0.1");
                if (request.RequestUri.AbsolutePath == "/api/tags") return Tags();
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var options = body.RootElement.GetProperty("options");
                Check("generation parameters from settings " + optionRequests, Math.Abs(options.GetProperty("temperature").GetDouble() - config.Temperature) < .001 &&
                    options.GetProperty("num_ctx").GetInt32() == 3072 && options.GetProperty("num_predict").GetInt32() == 180);
                optionRequests++;
                return Json(new { message = new { content = "Odpowiedź testowa." } });
            })))
            {
                await service.AskAsync("Pierwsza wiadomość");
                config.Temperature = .31;
                await service.AskAsync("Druga wiadomość");
                Check("settings reload without restart", optionRequests == 2 && service.LastResponseSucceeded);
            }

            using (var service = Create(new FakeHandler(async (request, _) =>
            {
                if (request.RequestUri!.AbsolutePath == "/api/tags") return Tags();
                if (request.RequestUri.AbsolutePath == "/api/show") return Json(new { error = "model not found" }, HttpStatusCode.NotFound);
                string model = await RequestModel(request);
                return model == "qwen3:4b-instruct" ? Json(new { error = "model does not support chat" }, HttpStatusCode.BadRequest) : Json(new { message = new { content = "Zapasowy działa." } });
            })))
            {
                string answer = await service.AskAsync("Proste pytanie");
                Check("missing weights recover using fallback", answer == "Zapasowy działa." && service.LastResponseSucceeded && service.LastModel == "gemma3:1b");
                Check("fallback is explained with repair command", service.LastFallbackReason.Contains("napraw AI") && service.LastFallbackReason.Contains("qwen3:4b-instruct") && service.LastAttemptedModels.Count == 2);
            }

            foreach (string failure in new[] { "malformed", "empty", "server500" })
            {
                using var service = Create(new FakeHandler(async (request, _) =>
                {
                    if (request.RequestUri!.AbsolutePath == "/api/tags") return Tags();
                    if (await RequestModel(request) != "qwen3:4b-instruct") return Json(new { message = new { content = "Odzyskano odpowiedź." } });
                    return failure switch
                    {
                        "malformed" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not-json") },
                        "empty" => Json(new { message = new { content = "<think>unfinished" } }),
                        _ => Json(new { error = "out of memory" }, HttpStatusCode.InternalServerError)
                    };
                }));
                await service.AskAsync("Test odporności");
                Check(failure + " recovers with next model", service.LastResponseSucceeded && service.LastModel == "gemma3:1b" && service.LastAttemptedModels.Count == 2);
            }

            using (var service = Create(new FakeHandler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
            {
                "/api/tags" => Tags(),
                "/api/show" => Json(new { capabilities = new[] { "completion" } }),
                "/api/chat" => Json(new { error = "does not support chat" }, HttpStatusCode.BadRequest),
                _ => Json(new { response = "Endpoint generate działa." })
            }))))
            {
                Check("generate endpoint compatibility", await service.AskAsync("Test generate") == "Endpoint generate działa." && service.LastResponseSucceeded);
            }

            using (var service = Create(new FakeHandler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/api/tags" ? Tags() :
                Json(new { error = "model failed" }, HttpStatusCode.InternalServerError)))))
            {
                await service.AskAsync("Wszystkie modele nie działają");
                Check("all model failures never marked success", !service.LastResponseSucceeded && service.LastModel.Length == 0 && service.LastError.Length > 0);
            }

            using (var service = Create(new FakeHandler(async (request, token) =>
            {
                if (request.RequestUri!.AbsolutePath == "/api/tags") return Tags();
                await Task.Delay(Timeout.Infinite, token);
                return Json(new { });
            }), TimeSpan.FromMilliseconds(100)))
            {
                string answer = await service.AskAsync("Timeout test");
                Check("deadline produces accurate timeout", answer.Contains("limit czasu") && !service.LastResponseSucceeded && service.LastResponseTime.TotalSeconds < 3);
            }

            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int cancelRequests = 0;
            using (var service = Create(new FakeHandler(async (request, token) =>
            {
                if (request.RequestUri!.AbsolutePath == "/api/tags") return Tags();
                if (Interlocked.Increment(ref cancelRequests) == 1)
                {
                    entered.TrySetResult();
                    await Task.Delay(Timeout.Infinite, token);
                }
                return Json(new { message = new { content = "Kolejna działa." } });
            })))
            {
                using var cancellation = new CancellationTokenSource();
                Task<string> pending = service.AskAsync("External cancel", cancellationToken: cancellation.Token);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                cancellation.Cancel();
                bool propagated = false;
                try { await pending; } catch (OperationCanceledException) { propagated = true; }
                Check("caller cancellation propagates", propagated && !service.LastResponseSucceeded);
                Check("request gate released after cancel", await service.AskAsync("After cancel") == "Kolejna działa.");
            }

            entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var service = Create(new FakeHandler(async (request, token) =>
            {
                if (request.RequestUri!.AbsolutePath == "/api/tags") return Tags();
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
                return Json(new { });
            })))
            {
                Task<string> pending = service.AskAsync("Internal cancel");
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                service.CancelCurrentRequest();
                Check("stop button cancels active generation", (await pending).Contains("Przerwano") && !service.LastResponseSucceeded);
            }
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "ai-reliability-results.json"), JsonSerializer.Serialize(new
            {
                checks, count = checks.Count, note = "Deterministic injected HTTP transport; no live model quality claim."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request, cancellationToken);
    }
}
