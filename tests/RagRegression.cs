using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SentinelX.Core;

namespace SentinelX.Tests;

/// <summary>0.97 · SEKCJA 8 (RAG) — test bez prawdziwej Ollamy: handler zwraca deterministyczne
/// wektory worka słów (16 kubełków), więc ranking kosinusowy da się policzyć na papierze.
/// Sprawdzam cięcie, kosinus, gotowiec promptu, budowę i szukanie przez REALNY router
/// oraz uczciwe komunikaty, gdy Ollamy nie ma.</summary>
internal static class RagRegression
{
    /// <summary>Wektor = liczność słów w 16 kubełkach (suma kodów znaków % 16), znormalizowana.</summary>
    internal static double[] FakeEmbed(string text)
    {
        var buckets = new double[16];
        foreach (Match m in Regex.Matches((text ?? "").ToLowerInvariant(), "[a-ząćęłńóśźż]{3,}"))
            buckets[m.Value.Sum(x => x) % 16]++;
        double norm = Math.Sqrt(buckets.Sum(x => x * x));
        return norm > 0 ? buckets.Select(x => x / norm).ToArray() : buckets;
    }

    private sealed class FakeOllamaHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content == null ? "" : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            var match = Regex.Match(body, "\"prompt\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!match.Success) return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest));
            string prompt = System.Text.Json.JsonDocument.Parse("\"" + match.Groups[1].Value + "\"").RootElement.GetString() ?? "";
            string json = "{\"embedding\":[" + string.Join(",", FakeEmbed(prompt).Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture))) + "]}";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class AlwaysOfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(new HttpRequestException("regresja: symulowany brak Ollamy"));
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TEST FAILED: " + message);
    }

    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);

        // ————— czysta logika —————
        var chunks = KnowledgeRagService.Chunk(new string('a', 1900) + "\n\nDrugi akapit.", 800, 120);
        Check(chunks.Count >= 3, "długi tekst tnie się na kilka fragmentów: " + chunks.Count);
        Check(chunks.All(x => x.Length <= 800), "każdy fragment mieści się w limicie");
        Check(KnowledgeRagService.Chunk("").Count == 0, "pusty tekst = zero fragmentów");
        Check(Math.Abs(KnowledgeRagService.Cosine(new[] { 1.0, 0.0 }, new[] { 1.0, 0.0 }) - 1.0) < 1e-9, "kosinus identycznych = 1");
        Check(Math.Abs(KnowledgeRagService.Cosine(new[] { 1.0, 0.0 }, new[] { 0.0, 1.0 })) < 1e-9, "ortogonalne = 0");
        Check(KnowledgeRagService.Cosine(new[] { 0.0, 0.0 }, new[] { 1.0, 1.0 }) == 0, "zerowy wektor = 0, nie NaN");

        string prompt = KnowledgeRagService.Prompt("limity?", new[] { ("a.md", "treść A"), ("b.md", "treść B") });
        Check(prompt.Contains("WYŁĄCZNIE") && prompt.Contains("PYTANIE: limity?") && prompt.Contains("ŹRÓDŁA: a.md, b.md"),
            "prompt ma pytanie, fragmenty i źródła");

        // ————— baza na fejkowej Ollamie, przez realny router —————
        string docs = Path.Combine(directory, "docs");
        Directory.CreateDirectory(docs);
        File.WriteAllText(Path.Combine(docs, "kuchnia.md"), "Rosół gotuje się długo. Kurczak, marchew i natka pietruszki. rosół rosół rosół rosół rosół rosół");
        File.WriteAllText(Path.Combine(docs, "modele.md"), "Modele lokalne chronią prywatność. prywatność prywatność prywatność prywatność prywatność prywatność Dane zostają na dysku.");
        using var ai = new LocalAiService(new GamingModeService(), new FakeOllamaHandler(), settingsDirectory: Path.Combine(directory, "settings"));
        var memory = new ConversationMemoryService(Path.Combine(directory, "memory"));
        var router = new CommandRouter(new SystemMonitor(), new SystemInfoService(), ai, memory);

        string build = await router.ProcessAsync("rag zbuduj: " + docs);
        Check(build.Contains("RAG ZBUDOWANY") && build.Contains("fragmentów") && build.Contains("TYLKO w RAM"),
            "budowa przez router: " + build.Split('\n')[0]);

        string status = await router.ProcessAsync("rag status");
        Check(status.Contains("RAG:") && status.Contains("2 plików"), "status przez router: " + status.Split('\n')[0]);

        string hits = await router.ProcessAsync("rag szukaj: prywatność prywatność prywatność prywatność prywatność prywatność");
        Check(hits.Contains("modele.md"), "szukanie trafia w modele.md (największy kosinus): " + hits.Split('\n')[0]);
        Check(hits.Contains("podobieństwo"), "wynik pokazuje procent podobieństwa");

        string food = await router.ProcessAsync("rag szukaj: rosół");
        Check(food.Contains("kuchnia.md"), "rosół trafia w kuchnia.md");

        string ready = await router.ProcessAsync("rag prompt: gdzie zostają dane?");
        Check(ready.Contains("GOTOWY PROMPT") && ready.Contains("modele.md") && ready.Contains("PYTANIE: gdzie zostają dane?"),
            "prompt składa się z trafionych fragmentów");

        // ————— uczciwość: budowa bez Ollamy nie niszczy bazy —————
        using var offlineAi = new LocalAiService(new GamingModeService(), new AlwaysOfflineHandler(), settingsDirectory: Path.Combine(directory, "settings2"));
        var offlineRouter = new CommandRouter(new SystemMonitor(), new SystemInfoService(), offlineAi, memory);
        string offlineBuild = await offlineRouter.ProcessAsync("rag zbuduj: " + docs);
        Check(offlineBuild.Contains("Ollam") && offlineBuild.Contains("127.0.0.1:11434"),
            "budowa bez Ollamy mówi wprost: " + offlineBuild.Split('\n')[0]);
        string afterFail = await router.ProcessAsync("rag status");
        Check(afterFail.Contains("2 plików"), "nieudana budowa nie czyści istniejącej bazy");

        // ————— reset i pusta baza —————
        string reset = await router.ProcessAsync("rag reset");
        Check(reset.Contains("Wyczyszczono"), "reset czyści bazę: " + reset);
        string emptySearch = await router.ProcessAsync("rag szukaj: cokolwiek");
        Check(emptySearch.Contains("Baza RAG jest pusta"), "szukanie na pustej bazie jest uczciwe");

        Check(KnowledgeRagService.TryHandleCommand("rozmowa o ragtime", CommandText.Normalize("rozmowa o ragtime")) is null,
            "zdanie nie jest poleceniem RAG");
        Check(KnowledgeRagService.TryHandleCommand("rag model", CommandText.Normalize("rag model"))!.Contains("Podaj nazwę"),
            "goły „rag model” podaje użycie");

        File.WriteAllText(Path.Combine(directory, "rag.txt"),
            "PASS\nchunking, cosine math, prompt builder, fake-embedding build/search/rank, offline honesty, reset verified\n");
    }
}
