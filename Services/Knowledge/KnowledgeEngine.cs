using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SentinelX.Models.Knowledge;

namespace SentinelX.Services.Knowledge;

/// <summary>
/// Silnik wiedzy użytkownika. Użytkownik wskazuje pliki/foldery (.txt/.md/.pdf/kod);
/// silnik je indeksuje, dzieli na chunki i udostępnia wyszukiwanie wektorowe (lekkie TF-IDF
/// w tej wersji — bez zewnętrznych wektorów). Źródła można w każdej chwili usunąć.
/// </summary>
public sealed class KnowledgeEngine
{
    private readonly ConcurrentDictionary<string, KnowledgeSource> sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<KnowledgeChunk> chunks = new();
    private readonly object chunksGate = new();

    public IReadOnlyList<KnowledgeSource> Sources => sources.Values.OrderByDescending(s => s.AddedAt).ToList();
    public int ChunkCount { get { lock (chunksGate) return chunks.Count; } }

    public async Task<KnowledgeSource> AddSourceAsync(string path, KnowledgeSourceKind kind, CancellationToken token)
    {
        var id = Guid.NewGuid().ToString("N");
        var src = new KnowledgeSource
        {
            Id = id,
            DisplayName = System.IO.Path.GetFileName(path),
            Path = path,
            Kind = kind
        };
        sources[id] = src;
        try
        {
            await IndexAsync(src, token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Indeksowanie nie powinna crashować — zapisujemy błąd jako zero chunków
            src.ChunkCount = 0; src.Hash = "error:" + ex.Message;
        }
        return src;
    }

    public void RemoveSource(string id)
    {
        if (!sources.TryRemove(id, out _)) return;
        lock (chunksGate) chunks.RemoveAll(c => c.SourceId == id);
    }

    public void RebuildAll(CancellationToken token = default)
    {
        lock (chunksGate) chunks.Clear();
        foreach (var s in sources.Values.ToList())
        {
            try { _ = IndexAsync(s, token).ConfigureAwait(false).GetAwaiter().GetResult(); }
            catch { }
        }
    }

    public IEnumerable<(KnowledgeChunk Chunk, double Score)> Search(string query, int topK = 5)
    {
        var terms = Tokenize(query);
        if (terms.Length == 0) yield break;
        List<(KnowledgeChunk, double)> scores = new();
        lock (chunksGate)
        {
            foreach (var c in chunks)
            {
                double score = 0;
                foreach (var t in terms) if (c.Tokens.Contains(t, StringComparer.OrdinalIgnoreCase)) score += 1;
                if (score > 0) scores.Add((c, score / Math.Max(1, c.Tokens.Length)));
            }
        }
        foreach (var item in scores.OrderByDescending(s => s.Item2).Take(topK))
            yield return item;
    }

    private async Task IndexAsync(KnowledgeSource src, CancellationToken token)
    {
        if (!File.Exists(src.Path) && !Directory.Exists(src.Path)) return;
        var text = await ReadContentAsync(src.Path, src.Kind, token).ConfigureAwait(false);
        src.Hash = ComputeSha256(text);
        var pieces = Chunk(text, 800);
        lock (chunksGate)
        {
            chunks.RemoveAll(c => c.SourceId == src.Id);
            int idx = 0;
            foreach (var p in pieces)
            {
                var toks = Tokenize(p);
                if (toks.Length > 0) chunks.Add(new KnowledgeChunk(src.Id, idx++, p, toks));
            }
            src.ChunkCount = chunks.Count(c => c.SourceId == src.Id);
        }
        src.IndexedAt = DateTimeOffset.UtcNow;
    }

    private static async Task<string> ReadContentAsync(string path, KnowledgeSourceKind kind, CancellationToken token)
    {
        if (Directory.Exists(path))
        {
            var sb = new StringBuilder();
            var ext = kind == KnowledgeSourceKind.SourceCode ? new[] { ".cs", ".xaml", ".xml", ".json", ".md", ".txt", ".py", ".js", ".ts", ".cpp", ".h", ".java", ".kt" }
                      : new[] { ".txt", ".md" };
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Where(f => ext.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)))
            {
                try { sb.AppendLine(await File.ReadAllTextAsync(f, token).ConfigureAwait(false)); }
                catch { /* pomiń nieczytelne */ }
            }
            return sb.ToString();
        }
        if (kind == KnowledgeSourceKind.Pdf)
        {
            // PDF parsing wymaga zewnętrznej biblioteki — zaznaczamy jako unsupported.
            return "[PDF] Parsowanie PDF nie jest wbudowane (wymaga biblioteki PdfPig/iTextSharp). " +
                   "Zaindeksowano tylko nazwę i ścieżkę: " + path;
        }
        try { return await File.ReadAllTextAsync(path, Encoding.UTF8, token).ConfigureAwait(false); }
        catch { try { return File.ReadAllText(path); } catch { return ""; } }
    }

    private static List<string> Chunk(string text, int maxChunkChars)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        var paragraphs = Regex.Split(text, @"\r?\n\s*\r?\n");
        var buf = new StringBuilder();
        foreach (var p in paragraphs)
        {
            if (buf.Length + p.Length > maxChunkChars && buf.Length > 0)
            {
                result.Add(buf.ToString()); buf.Clear();
            }
            buf.AppendLine(p.Trim());
            if (buf.Length >= maxChunkChars)
            {
                result.Add(buf.ToString()); buf.Clear();
            }
        }
        if (buf.Length > 0) result.Add(buf.ToString());
        return result;
    }

    private static string[] Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        var matches = Regex.Matches(text.ToLowerInvariant(), @"[a-ząćęłńóśźż0-9_\-]{2,}");
        var set = new HashSet<string>(StringComparer.Ordinal);
        var stop = new HashSet<string>(StringComparer.Ordinal) { "jest","być","się","oraz","i","a","the","to","w","na","z","do","że","o","for","of","and","to","in" };
        foreach (Match m in matches) if (!stop.Contains(m.Value)) set.Add(m.Value);
        return set.ToArray();
    }

    private static string ComputeSha256(string s)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
