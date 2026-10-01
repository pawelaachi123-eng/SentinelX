using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SentinelX.Services.Engine;

namespace SentinelX.Tests;

/// <summary>The built-in AI engine (no Ollama): pinned catalogue, verified resumable downloads, the Ollama-compatible facade over a fake
/// llama-server (streaming, reasoning switch, keep-alive, unavailable engine) and the safe launch arguments. No real model is started.</summary>
internal static class EngineRegression
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException("EngineRegression: " + message); }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request, cancellationToken);
    }

    private sealed class FakeRuntime : IEngineRuntime
    {
        public List<(string Model, TimeSpan KeepAlive)> Released { get; } = [];
        public int Unloads { get; private set; }
        public int LastContext { get; private set; }
        public string? Unavailable { get; set; }

        public IReadOnlyList<EngineModelInfo> InstalledModels() =>
        [
            new("qwen3:1.7b", 1000, DateTimeOffset.UtcNow, "sha256:aa", "qwen3", "1.7B", "Q4_K_M"),
            new("qwen3:4b-instruct", 2000, DateTimeOffset.UtcNow, "sha256:bb", "qwen3", "4B", "Q4_K_M")
        ];

        public Task<EngineEndpoint> AcquireAsync(string model, int contextSize, CancellationToken token)
        {
            if (Unavailable != null) throw new EngineUnavailableException(Unavailable);
            LastContext = contextSize;
            return Task.FromResult(new EngineEndpoint(new Uri("http://127.0.0.1:1/"), "test-key"));
        }

        public void Release(string model, TimeSpan keepAlive) => Released.Add((model, keepAlive));
        public void Unload() => Unloads++;
    }

    private static HttpResponseMessage Sse(params string[] deltas)
    {
        var body = new StringBuilder();
        foreach (string delta in deltas)
            body.Append("data: ").Append(JsonSerializer.Serialize(new { choices = new[] { new { index = 0, delta = new { content = delta } } } })).Append("\n\n");
        body.Append("data: [DONE]\n\n");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body.ToString(), Encoding.UTF8, "text/event-stream") };
    }

    private static HttpResponseMessage Completion(string text) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content = text }, finish_reason = "stop" } } }), Encoding.UTF8, "application/json") };

    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);

        // ---- the catalogue pins everything that is downloaded
        foreach (EngineModelSpec model in EngineCatalog.Models)
        {
            Check(model.Sha256.Length == 64 && model.Sha256.All(Uri.IsHexDigit), "model hash is a SHA-256: " + model.Name);
            Check(model.SizeBytes > 500_000_000, "model size is pinned: " + model.Name);
            Check(model.Url.StartsWith("https://huggingface.co/", StringComparison.Ordinal) && Regex.IsMatch(model.Url, "/resolve/[0-9a-f]{40}/"), "model URL pins an immutable revision: " + model.Name);
            Check(model.Url.EndsWith(model.FileName, StringComparison.Ordinal), "file name matches the URL: " + model.Name);
            Check(LocalAiService.IsLocalModelName(model.Name), "model name is accepted by the AI layer: " + model.Name);
        }
        Check(EngineCatalog.Find("QWEN3:1.7B") == EngineCatalog.Lite, "model lookup ignores case");
        Check(EngineCatalog.LlamaZipSha256.Length == 64 && EngineCatalog.LlamaZipUrl.Contains(EngineCatalog.LlamaBuild) && EngineCatalog.LlamaZipFile.Contains(EngineCatalog.LlamaBuild),
            "the engine runtime is pinned to one build");

        // ---- verified, resumable downloads
        byte[] payload = RandomNumberGenerator.GetBytes(300_000);
        string hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        int calls = 0;
        var flaky = new Handler((request, _) =>
        {
            calls++;
            long start = request.Headers.Range?.Ranges.FirstOrDefault()?.From ?? 0;
            byte[] slice = payload[(int)start..];
            if (calls == 1) slice = slice[..(slice.Length / 2)]; // the connection "drops" halfway through the first attempt
            return Task.FromResult(new HttpResponseMessage(start > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = new ByteArrayContent(slice) });
        });
        using (var downloader = new EngineDownloader(flaky, 4, TimeSpan.Zero))
        {
            string destination = Path.Combine(directory, "dl", "model.bin");
            long reported = 0;
            await downloader.DownloadAsync("https://example.invalid/model.bin", destination, payload.Length, hash, new Progress<long>(value => reported = Math.Max(reported, value)), default);
            Check(File.ReadAllBytes(destination).SequenceEqual(payload), "an interrupted download resumes and ends byte-identical");
            Check(calls == 2 && !File.Exists(destination + ".part"), "exactly one resume was needed and no partial file is left");

            string bad = Path.Combine(directory, "dl", "bad.bin");
            bool rejected = false;
            try { await downloader.DownloadAsync("https://example.invalid/bad.bin", bad, payload.Length, new string('0', 64), null, default); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && !File.Exists(bad) && !File.Exists(bad + ".part"), "a file with the wrong SHA-256 is deleted and never used");
        }

        // ---- the store recognises a complete model only by its exact size, and finds a bundled runtime
        var store = new EngineStore(Path.Combine(directory, "store"), Path.Combine(directory, "bundled"));
        var probe = new EngineModelSpec("probe:1", "Probe", "probe.gguf", "https://example.invalid/probe.gguf", 10, new string('a', 64), 1, "x", "1B", "Q");
        Directory.CreateDirectory(store.ModelsDirectory);
        File.WriteAllBytes(store.ModelPath(probe), new byte[9]);
        Check(!store.IsModelInstalled(probe), "a truncated model file is not installed");
        File.WriteAllBytes(store.ModelPath(probe), new byte[10]);
        Check(store.IsModelInstalled(probe), "a complete model file is installed");
        Check(store.FindServerExecutable() == null, "no runtime yet");
        Directory.CreateDirectory(Path.Combine(directory, "bundled", "nested"));
        File.WriteAllBytes(Path.Combine(directory, "bundled", "nested", "llama-server.exe"), [1]);
        Check(store.FindServerExecutable() is { } exe && exe.EndsWith("llama-server.exe", StringComparison.OrdinalIgnoreCase), "the bundled runtime is found");

        // ---- what a PC gets depends on its memory
        using (var small = new EngineService(store, new EngineDownloader(flaky), () => false, () => 8))
            Check(small.WantedModels().Count == 1 && small.WantedModels()[0] == EngineCatalog.Lite, "an 8 GB PC gets the light model only");
        using (var big = new EngineService(store, new EngineDownloader(flaky), () => false, () => 32))
        {
            int expected = store.FreeBytes() > EngineCatalog.Standard.SizeBytes + 3L * 1073741824 ? 2 : 1;
            Check(big.WantedModels().Count == expected, "a 32 GB PC with free disk space also gets the strong model");
        }
        Check(EngineService.LooksLikeModelProblem("gguf_init_from_file: invalid magic characters") && !EngineService.LooksLikeModelProblem("kod -1073741515 brakuje bibliotek"),
            "a missing runtime library is never mistaken for a damaged model");

        // ---- launch arguments: loopback only, key never on the command line
        IReadOnlyList<string> args = LlamaServerHost.FullArguments("C:\\m\\model.gguf", 5555, 4096, 6);
        Check(args.Zip(args.Skip(1)).Any(pair => pair is ("--host", "127.0.0.1")), "the engine listens on loopback only");
        Check(!args.Contains("--api-key") && args.Contains("--no-webui") && args.Contains("5555"), "no key or web UI on the command line");
        Check(LlamaServerHost.MinimalArguments("m.gguf", 1, 2048).Count == 8 && !LlamaServerHost.MinimalArguments("m.gguf", 1, 2048).Contains("--jinja"), "the fallback profile is the bare minimum (model, loopback host, port, context)");

        // ---- keep-alive parsing (the AI layer sends "10m" normally and "0" while a game runs)
        Check(EngineOllamaFacade.ParseKeepAlive(JsonNode.Parse("\"0\"")) == TimeSpan.Zero, "keep_alive \"0\" frees memory at once");
        Check(EngineOllamaFacade.ParseKeepAlive(JsonNode.Parse("\"10m\"")) == TimeSpan.FromMinutes(10), "keep_alive 10m");
        Check(EngineOllamaFacade.ParseKeepAlive(JsonNode.Parse("\"30s\"")) == TimeSpan.FromSeconds(30), "keep_alive 30s");
        Check(EngineOllamaFacade.ParseKeepAlive(JsonNode.Parse("0")) == TimeSpan.Zero, "numeric keep_alive 0");
        Check(EngineOllamaFacade.ParseKeepAlive(JsonNode.Parse("-1")) == TimeSpan.FromHours(24), "negative keep_alive means long");
        Check(EngineOllamaFacade.ParseKeepAlive(null) == TimeSpan.FromMinutes(5), "missing keep_alive uses the default");
        Check(EngineOllamaFacade.ExtractDelta("{\"choices\":[{\"delta\":{\"content\":\"zażółć\"}}]}", out string? noError) == "zażółć" && noError == null, "delta text is extracted");
        Check(EngineOllamaFacade.ExtractDelta("{\"error\":{\"message\":\"boom\"}}", out string? boom) == "" && boom == "boom", "an engine error line is reported");

        // ---- the facade, end to end through the real AI layer
        var runtime = new FakeRuntime();
        string innerBody = "";
        var engine = new Handler(async (request, token) =>
        {
            innerBody = await request.Content!.ReadAsStringAsync(token);
            Check(request.RequestUri!.AbsolutePath == "/v1/chat/completions", "the engine is called on its OpenAI-compatible chat endpoint");
            Check(request.Headers.Authorization?.Parameter == "test-key", "every engine call carries the per-run key");
            using JsonDocument doc = JsonDocument.Parse(innerBody);
            return doc.RootElement.GetProperty("stream").GetBoolean() ? Sse("Pierwsza ", "część ", "odpowiedzi.") : Completion("Jasne.");
        });
        using var facade = new EngineOllamaFacade(runtime, engine);
        using var ai = new LocalAiService(new GamingModeService(), facade, Path.Combine(directory, "ai"), aiSettingsProvider: () => new AiSettings());

        IReadOnlyList<string> installed = await ai.GetInstalledModelsAsync();
        Check(installed.SequenceEqual(["qwen3:1.7b", "qwen3:4b-instruct"]), "the AI layer lists the engine's models");
        string statusText = await ai.GetStatusAsync();
        Check(statusText.Contains("Silnik AI") && !statusText.Contains("Ollama lokalnie"), "status speaks about the built-in engine");

        var chunks = new List<string>();
        string answer = await ai.AskAsync("Czy to działa?", "", default, chunks.Add);
        Check(answer == "Pierwsza część odpowiedzi." && string.Concat(chunks) == answer && chunks.Count >= 2, "the answer streams chunk by chunk and assembles correctly");
        Check(ai.LastResponseSucceeded && ai.LastModel == "qwen3:4b-instruct", "an idle PC uses the stronger model");
        Check(innerBody.Contains("enable_thinking") && innerBody.Contains("max_tokens") && innerBody.Contains("AKTUALNE PYTANIE"),
            "reasoning is switched off for Qwen3, limits and the question reach the engine");
        Check(runtime.Released.Count >= 1 && runtime.Released[^1].KeepAlive == TimeSpan.FromMinutes(10), "the model stays warm for ten minutes after an answer");

        string plain = await ai.AskAsync("Druga wiadomość");
        Check(plain == "Jasne.", "a non-streaming answer works too");

        using var http = new HttpClient(facade, disposeHandler: false) { BaseAddress = new Uri("http://127.0.0.1:11434") };
        using (var gaming = await http.PostAsync("/api/chat", new StringContent("{\"model\":\"qwen3:1.7b\",\"messages\":[{\"role\":\"user\",\"content\":\"x\"}],\"stream\":false,\"keep_alive\":\"0\"}", Encoding.UTF8, "application/json")))
            Check(gaming.IsSuccessStatusCode && runtime.Released[^1].KeepAlive == TimeSpan.Zero, "keep_alive 0 (game running) releases the memory immediately");
        int unloadsBefore = runtime.Unloads;
        using (var unload = await http.PostAsync("/api/chat", new StringContent("{\"model\":\"qwen3:1.7b\",\"messages\":[],\"keep_alive\":0}", Encoding.UTF8, "application/json")))
            Check(unload.IsSuccessStatusCode && runtime.Unloads == unloadsBefore + 1, "the unload call stops the engine");
        using (var missing = await http.PostAsync("/api/show", new StringContent("{\"model\":\"nie-ma:1b\"}", Encoding.UTF8, "application/json")))
            Check(missing.StatusCode == HttpStatusCode.NotFound, "an unknown model is reported as missing");
        using (var known = await http.PostAsync("/api/show", new StringContent("{\"model\":\"qwen3:1.7b\"}", Encoding.UTF8, "application/json")))
            Check(known.IsSuccessStatusCode && (await known.Content.ReadAsStringAsync()).Contains("completion"), "a known model reports the completion capability");

        runtime.Unavailable = "Silnik AI: pobieram model Qwen3 4B: 37%.";
        string whileInstalling = await ai.AskAsync("Jeszcze raz");
        Check(whileInstalling.Contains("37%") && !ai.LastResponseSucceeded, "while the engine installs, the user is told the progress — not a cryptic error");
    }
}
