using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SentinelX.Tests;

internal static class RagArchiveRegression
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("TEST FAILED: " + message); }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or CryptographicException or JsonException) { return; }
        throw new InvalidOperationException("TEST FAILED: " + message);
    }
    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        string archives = Path.Combine(directory, "archives");
        var store = new RagArchiveStore(archives);
        var snapshot = new KnowledgeRagService.Archive(1, "nomic-embed-text", "source", 1, DateTime.UtcNow,
            [new("folder/notatka.md", "TAJNA_TRESC_REGRESJI", [1.0, 0.0])]);
        store.Save("kopia", snapshot);
        byte[] bytes = File.ReadAllBytes(Path.Combine(archives, "kopia.sxrag"));
        Check(!Encoding.UTF8.GetString(bytes).Contains("TAJNA_TRESC_REGRESJI"), "plaintext absent from archive");
        var loaded = new RagArchiveStore(archives).Load("kopia");
        Check(loaded.Model == snapshot.Model && loaded.Items[0].Chunk == snapshot.Items[0].Chunk
            && loaded.Items[0].Vector.SequenceEqual(new[] { 1.0, 0.0 }), "fresh store roundtrip restores metadata, text and vectors");
        Reject(() => store.Save("kopia", snapshot), "existing archive must not be overwritten");
        Check(File.ReadAllBytes(Path.Combine(archives, "kopia.sxrag")).SequenceEqual(bytes), "existing bytes intact");
        Reject(() => store.Load("../kopia"), "path traversal rejected");
        Check(!RagArchiveStore.ValidName("CON") && !RagArchiveStore.ValidName("LPT1"), "Windows device names rejected");
        Reject(() => KnowledgeRagService.Validate(snapshot with { Version = 2 }), "unknown schema rejected");
        Reject(() => KnowledgeRagService.Validate(snapshot with { Items = [new("a", "b", [double.NaN])] }), "nonfinite vector rejected");
        Reject(() => KnowledgeRagService.Validate(snapshot with { Items = [new("a", "b", [1.0]), new("c", "d", [1.0, 0.0])] }), "mixed dimensions rejected");
        File.WriteAllBytes(Path.Combine(archives, "broken.sxrag"), bytes[..^1]);
        Reject(() => store.Load("broken"), "truncated DPAPI ciphertext rejected");
        var restored = KnowledgeRagService.Restore(snapshot, KnowledgeRagService.Revision);
        Check(restored.Contains("Wczytano"), "baseline restored");
        using var ai = new LocalAiService(new GamingModeService(), new RagAnswerRegression.Handler(), Path.Combine(directory, "settings"));
        var memory = new ConversationMemoryService(Path.Combine(directory, "memory"));
        DateTime now = DateTime.UtcNow;
        var router = new CommandRouter(new SystemMonitor(), new SystemInfoService(), ai, memory,
            ragArchiveDirectory: archives, ragClock: () => now);
        Check((await router.ProcessAsync("rag zapisz: nowa potwierdzam")).Contains("Zgoda RAG nieaktualna"), "no confirmation without plan");
        await router.ProcessAsync("rag zapisz: nowa");
        Check(!File.Exists(Path.Combine(archives, "nowa.sxrag")), "plan does not persist anything");
        now = now.AddMinutes(11);
        Check((await router.ProcessAsync("rag zapisz: nowa potwierdzam")).Contains("Zgoda RAG nieaktualna"), "consent expires");
        await router.ProcessAsync("rag zapisz: nowa");
        Check((await router.ProcessAsync("rag zapisz: inna potwierdzam")).Contains("Zgoda RAG nieaktualna"), "consent bound to name");
        await router.ProcessAsync("rag zapisz: nowa");
        Check((await router.ProcessAsync("rag zapisz: nowa potwierdzam")).Contains("Zapisano zaszyfrowane"), "save confirmed");
        Check((await router.ProcessAsync("rag zapisz: nowa potwierdzam")).Contains("Zgoda RAG nieaktualna"), "consent single use");
        await router.ProcessAsync("rag wczytaj: kopia");
        KnowledgeRagService.Reset();
        Check((await router.ProcessAsync("rag wczytaj: kopia potwierdzam")).Contains("Zgoda RAG nieaktualna"), "changed RAM invalidates plan");
        await router.ProcessAsync("rag wczytaj: kopia");
        Check((await router.ProcessAsync("rag wczytaj: kopia potwierdzam")).Contains("Wczytano archiwum"), "load via router");
        string before = KnowledgeRagService.Status();
        await router.ProcessAsync("rag wczytaj: broken");
        Check((await router.ProcessAsync("rag wczytaj: broken potwierdzam")).Contains("nie powiodła"), "bad archive reported");
        Check(KnowledgeRagService.Status() == before, "bad archive leaves RAM intact");
        memory.SetPrivateMode(true);
        Check((await router.ProcessAsync("rag zapisz: prywatne")).Contains("Tryb prywatny"), "private mode refuses persistence");
        Check(!File.Exists(Path.Combine(archives, "prywatne.sxrag")), "private mode has no archive");
        KnowledgeRagService.Reset();
        KnowledgeRagService.SetModel("nomic-embed-text");
        File.WriteAllText(Path.Combine(directory, "rag-archive.txt"), "PASS: DPAPI roundtrip, corruption, schema, dimensions, no overwrite, consent name/time/replay/revision, private mode\n");
    }
}
