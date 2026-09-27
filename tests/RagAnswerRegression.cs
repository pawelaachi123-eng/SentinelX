using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace SentinelX.Tests;

internal static class RagAnswerRegression
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("TEST FAILED: " + message); }
    internal sealed class Handler : HttpMessageHandler
    {
        internal int Chats;
        internal string Model = "";
        internal string Answer = "Dane pozostają lokalnie [1].";
        internal double[] Vector = [1.0, 0.0];
        internal bool Offline;
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Check(request.RequestUri!.Host == "127.0.0.1", "all traffic stays on loopback");
            if (Offline) throw new HttpRequestException("offline regression");
            if (request.RequestUri.AbsolutePath == "/api/tags") return Json(new { models = new[] { new { name = "qwen3:4b" } } });
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            if (request.RequestUri.AbsolutePath == "/api/embeddings")
            { Model = body.RootElement.GetProperty("model").GetString()!; return Json(new { embedding = Vector }); }
            Chats++;
            string payload = string.Join("\n", body.RootElement.GetProperty("messages").EnumerateArray().Select(x => x.GetProperty("content").GetString()));
            Check(payload.Contains("WYŁĄCZNIE") && payload.Contains("[1]") && payload.Contains("niezaufanymi"), "generation contains source-only prompt and instruction boundary");
            return Json(new { message = new { content = Answer } });
        }
    }
    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var snapshot = new KnowledgeRagService.Archive(1, "nomic-embed-text", "source", 1, DateTime.UtcNow,
            [new("folder/notatka.md", "Dane pozostają lokalnie.", [1.0, 0.0])]);
        KnowledgeRagService.Restore(snapshot, KnowledgeRagService.Revision);
        var handler = new Handler();
        using var ai = new LocalAiService(new GamingModeService(), handler, Path.Combine(directory, "settings"));
        var router = new CommandRouter(new SystemMonitor(), new SystemInfoService(), ai,
            new ConversationMemoryService(Path.Combine(directory, "memory")));
        KnowledgeRagService.SetModel("inny-model");
        string answer = await router.ProcessAsync("rag pytaj: gdzie są dane?");
        Check(answer.Contains("ODPOWIEDŹ RAG") && answer.Contains("[1] folder/notatka.md") && answer.Contains("„Dane pozostają lokalnie.”"), "answer includes literal evidence and source identity");
        Check(handler.Model == "nomic-embed-text", "query uses indexed model even after changing future build model");
        Check(handler.Chats == 1, "one generation via real router");
        handler.Answer = "Zmyślone źródło [99].";
        Check((await router.ProcessAsync("rag pytaj: gdzie?")).Contains("nie podał poprawnych odsyłaczy"), "out-of-range citation rejected");
        handler.Answer = "Bez przypisu.";
        Check((await router.ProcessAsync("rag pytaj: gdzie?")).Contains("nie podał poprawnych odsyłaczy"), "missing citations rejected");
        handler.Vector = [0.0, 1.0];
        int calls = handler.Chats;
        Check((await router.ProcessAsync("rag pytaj: gdzie?")).Contains("Brak fragmentów"), "orthogonal cosine is zero");
        Check(handler.Chats == calls, "no evidence means no generation");
        handler.Vector = [1.0];
        Check((await router.ProcessAsync("rag pytaj: gdzie?")).Contains("nie pasuje"), "dimension mismatch rejected");
        handler.Offline = true;
        Check((await router.ProcessAsync("rag pytaj: gdzie?")).Contains("127.0.0.1:11434"), "offline is explicit");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        bool threw = false;
        try { await router.ProcessAsync("rag pytaj: gdzie?", cancelled.Token); } catch (OperationCanceledException) { threw = true; }
        Check(threw, "cancellation propagates");
        KnowledgeRagService.Reset();
        Check((await router.ProcessAsync("rag pytaj: gdzie?")).Contains("Baza RAG jest pusta"), "empty base prevents network");
        Check((await router.ProcessAsync("rag pytaj:")).Contains("Użycie"), "missing argument has usage");
        KnowledgeRagService.SetModel("nomic-embed-text");
        File.WriteAllText(Path.Combine(directory, "rag-answer.txt"), "PASS: router, citations, snapshots, model identity, no hits, dimensions, offline, cancellation\n");
    }
}
