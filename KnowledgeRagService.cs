using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SentinelX;

/// <summary>
/// SEKCJA 8 — RAG bez magii i bez chmury: pliki .txt/.md z podanego folderu są cięte na fragmenty,
/// każdy fragment dostaje wektor z LOKALNEJ Ollamy (nomic-embed-text, 127.0.0.1:11434), a szukanie
/// to kosinus po wektorach. Cała baza żyje TYLKO w RAM procesu — na dysk nie zapisuję nic,
/// poza ten komputer nie wysyłam nic. Bez Ollamy każda operacja kończy się jawnym komunikatem.
/// </summary>
public static class KnowledgeRagService
{
    private sealed record Fragment(string File, string Chunk, double[] Vector);

    private static readonly object Sync = new();
    private static readonly List<Fragment> Fragments = [];
    private static string sourceRoot = "";
    private static string embeddingModel = "nomic-embed-text";
    private static int sourceFiles;
    private static DateTime builtAt;

    private const int MaxFiles = 200;
    private const int MaxChunkChars = 800;
    private const int ChunkOverlapChars = 120;
    private const int MaxFragments = 400;

    public static string EmbeddingModel => embeddingModel;

    public static string? TryHandleCommand(string command, string text)
    {
        if (text is "rag status") return Status();
        if (text is "rag reset") return Reset();
        if (text is "rag zbuduj") return "Podaj folder: „rag zbuduj: C:\\dokumenty”. Tnę pliki na fragmenty i liczę wektory lokalną Ollamą — baza powstaje tylko w RAM.";
        if (text is "rag szukaj") return "Podaj frazę: „rag szukaj: prywatność”. Najpierw zbuduj bazę: „rag zbuduj: folder”.";
        if (text is "rag prompt") return "Podaj pytanie: „rag prompt: jakie są limity?”. Zbuduję prompt z fragmentów bazy — gotowiec do czatu.";
        if (text is "rag model") return "Podaj nazwę: „rag model: nomic-embed-text” (domyślny).";
        var model = Regex.Match(command, @"^rag model[:\s]+(.+)$", RegexOptions.IgnoreCase);
        if (model.Success) return SetModel(model.Groups[1].Value);
        return null;
    }

    public static string SetModel(string model)
    {
        string? name = LocalAiService.NormalizePullModelName(model);
        if (name == null) return "Niepoprawna nazwa modelu. Przykład: „rag model: nomic-embed-text”.";
        embeddingModel = name;
        return "Model embeddingów: " + name + ". Uwaga: zmiana modelu NIE przelicza istniejącej bazy — zbuduj ponownie („rag zbuduj: folder”).";
    }

    // ————— czysta logika (regresja sprawdza bez sieci) —————

    /// <summary>Tnie tekst na fragmenty po akapitach; za długi akapit tnie twardo z zakładką.</summary>
    public static List<string> Chunk(string content, int maxChars = MaxChunkChars, int overlap = ChunkOverlapChars)
    {
        var chunks = new List<string>();
        string text = (content ?? "").Replace("\r\n", "\n").Trim();
        if (text.Length == 0) return chunks;
        var paragraphs = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => x.Length > 0).ToList();
        var current = new StringBuilder();
        foreach (string paragraph in paragraphs)
        {
            if (paragraph.Length > maxChars)
            {
                if (current.Length > 0) { chunks.Add(current.ToString().Trim()); current.Clear(); }
                int start = 0;
                while (start < paragraph.Length)
                {
                    int end = Math.Min(start + maxChars, paragraph.Length);
                    chunks.Add(paragraph[start..end].Trim());
                    if (end == paragraph.Length) break;
                    start = Math.Max(end - overlap, start + 1);
                }
                continue;
            }
            if (current.Length > 0 && current.Length + paragraph.Length + 2 > maxChars)
            {
                chunks.Add(current.ToString().Trim());
                current.Clear();
            }
            if (current.Length > 0) current.Append("\n\n");
            current.Append(paragraph);
        }
        if (current.Length > 0) chunks.Add(current.ToString().Trim());
        return chunks.Where(x => x.Length > 0).ToList();
    }

    public static double Cosine(double[] a, double[] b)
    {
        if (a == null || b == null || a.Length != b.Length || a.Length == 0) return 0;
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        if (na <= 0 || nb <= 0) return 0;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    /// <summary>Gotowiec do modelu: pytanie + fragmenty + obowiązek źródeł. Bez zmyślania.</summary>
    public static string Prompt(string question, IReadOnlyList<(string File, string Chunk)> hits)
    {
        var sb = new StringBuilder();
        sb.Append("Odpowiedz WYŁĄCZNIE na podstawie poniższych fragmentów. Jeśli odpowiedzi w nich nie ma — napisz to wprost.").AppendLine();
        sb.Append("PYTANIE: ").Append((question ?? "").Trim()).AppendLine().AppendLine();
        sb.Append("FRAGMENTY:").AppendLine();
        for (int i = 0; i < hits.Count; i++)
        {
            sb.Append("[").Append(i + 1).Append("] ").Append(hits[i].File).AppendLine();
            sb.Append(hits[i].Chunk).AppendLine().AppendLine();
        }
        sb.Append("ŹRÓDŁA: ").Append(string.Join(", ", hits.Select(x => x.File).Distinct()));
        return sb.ToString();
    }

    private static string OllamaMessage() =>
        "Nie mogę połączyć się z Ollamą (127.0.0.1:11434). Uruchom Ollamę, sprawdź „model pobierz: " + embeddingModel + "” i spróbuj ponownie — poza ten komputer nic nie wysyłam.";

    // ————— operacje (sieć wyłącznie przez LocalAiService → loopback) —————

    public static async Task<string> BuildAsync(string directory, LocalAiService ai, CancellationToken cancellationToken)
    {
        string dir = (directory ?? "").Trim().Trim('"');
        if (dir.Length == 0) return "Podaj folder, np. „rag zbuduj: C:\\dokumenty”.";
        if (!Directory.Exists(dir)) return "Folder nie istnieje: " + dir + " — sprawdzam dokładnie tę ścieżkę, nie zgaduję.";
        List<string> files;
        try { files = KnowledgeIndexService.CollectTextFiles(dir, MaxFiles).ToList(); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        { return "Nie mogę czytać folderu („" + ex.Message + "”) — bazy nie buduję na częściowych danych."; }
        if (files.Count == 0)
            return "W folderze nie ma plików .txt ani .md — baza zostaje bez zmian.";

        var chunks = new List<(string File, string Chunk)>();
        foreach (string file in files)
        {
            try
            {
                if (new FileInfo(file).Length > 2_000_000) continue;
                foreach (string chunk in Chunk(File.ReadAllText(file)))
                {
                    chunks.Add((Path.GetFileName(file), chunk));
                    if (chunks.Count >= MaxFragments) break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* pojedynczy plik pomijam */ }
            if (chunks.Count >= MaxFragments) break;
        }
        if (chunks.Count == 0)
            return "Pliki są puste albo za duże — nie ma czego wbudowywać. Baza zostaje bez zmian.";

        var fresh = new List<Fragment>();
        int failed = 0;
        foreach (var (file, chunk) in chunks)
        {
            double[] vector;
            try { vector = await ai.GetEmbeddingAsync(embeddingModel, chunk, cancellationToken); }
            catch (HttpRequestException) { return OllamaMessage(); }
            catch (InvalidOperationException) { failed++; continue; }
            fresh.Add(new Fragment(file, chunk, vector));
        }
        if (fresh.Count == 0)
            return "Ollama nie zwróciła żadnego wektora (model „" + embeddingModel + "”). Sprawdź: „model info: " + embeddingModel + "”.";
        lock (Sync)
        {
            Fragments.Clear();
            Fragments.AddRange(fresh);
            sourceRoot = dir;
            sourceFiles = files.Count;
            builtAt = DateTime.Now;
        }
        return "RAG ZBUDOWANY: " + files.Count + " plików → " + fresh.Count + " fragmentów z wektorami" +
            (failed > 0 ? " (" + failed + " pominiętych — Ollama odmówiła)" : "") + " · model: " + embeddingModel + Environment.NewLine +
            "· baza żyje TYLKO w RAM tej sesji — po zamknięciu aplikacji znika, na dysk nie zapisuję nic" + Environment.NewLine +
            "· szukaj: „rag szukaj: fraza” · gotowy prompt: „rag prompt: pytanie” · stan: „rag status”";
    }

    public static async Task<string> SearchAsync(string query, LocalAiService ai, int top = 5)
    {
        lock (Sync)
        {
            if (Fragments.Count == 0)
                return "Baza RAG jest pusta — najpierw „rag zbuduj: folder”. Bez wektorów niczego nie zgaduję.";
        }
        string trimmed = (query ?? "").Trim();
        if (trimmed.Length == 0) return "Podaj frazę: „rag szukaj: prywatność”.";
        double[] queryVector;
        try { queryVector = await ai.GetEmbeddingAsync(embeddingModel, trimmed, default); }
        catch (HttpRequestException) { return OllamaMessage(); }
        catch (InvalidOperationException) { return "Ollama nie zwróciła wektora zapytania (model „" + embeddingModel + "”). Sprawdź: „model info: " + embeddingModel + "”."; }
        List<(string File, string Chunk, double Score)> hits;
        lock (Sync)
        {
            hits = Fragments.Select(f => (f.File, f.Chunk, Score: Cosine(queryVector, f.Vector)))
                .OrderByDescending(x => x.Score).Take(top).ToList();
        }
        if (hits.Count == 0 || hits[0].Score <= 0)
            return "Brak trafień (" + Fragments.Count + " fragmentów, źródło „" + sourceRoot + "”) — zapytanie nie styka się z bazą.";
        CultureInfo pl = CultureInfo.GetCultureInfo("pl-PL");
        var sb = new StringBuilder("WYNIKI RAG (zapytanie: „" + trimmed + "”, model: " + embeddingModel + "):").AppendLine();
        for (int i = 0; i < hits.Count; i++)
        {
            if (hits[i].Score <= 0) break;
            string preview = hits[i].Chunk.Length > 160 ? hits[i].Chunk[..160] + "…" : hits[i].Chunk;
            sb.Append("[").Append(i + 1).Append("] ").Append(hits[i].File)
              .Append(" (podobieństwo: ").Append((100 * hits[i].Score).ToString("0.0", pl)).Append("%)").AppendLine();
            sb.Append("    ").Append(preview.Replace("\n", " ")).AppendLine();
        }
        sb.Append("· gotowy prompt z fragmentów: „rag prompt: ").Append(trimmed).Append("” · baza tylko w RAM");
        return sb.ToString();
    }

    public static async Task<string> PromptAsync(string question, LocalAiService ai)
    {
        lock (Sync)
        {
            if (Fragments.Count == 0)
                return "Baza RAG jest pusta — najpierw „rag zbuduj: folder”. Promptu nie składam z powietrza.";
        }
        string trimmed = (question ?? "").Trim();
        if (trimmed.Length == 0) return "Podaj pytanie: „rag prompt: jakie są limity?”.";
        double[] queryVector;
        try { queryVector = await ai.GetEmbeddingAsync(embeddingModel, trimmed, default); }
        catch (HttpRequestException) { return OllamaMessage(); }
        catch (InvalidOperationException) { return "Ollama nie zwróciła wektora zapytania (model „" + embeddingModel + "”)."; }
        List<(string File, string Chunk)> top;
        lock (Sync)
        {
            top = Fragments.Select(f => (f.File, f.Chunk, Score: Cosine(queryVector, f.Vector)))
                .OrderByDescending(x => x.Score).Take(3).Where(x => x.Score > 0)
                .Select(x => (x.File, x.Chunk)).ToList();
        }
        if (top.Count == 0)
            return "Brak fragmentów pasujących do pytania — promptu nie składam z powietrza.";
        return "GOTOWY PROMPT (skopiuj do czatu; Sentinel wyśle go tylko do lokalnego modelu):" + Environment.NewLine +
            Prompt(trimmed, top);
    }

    public static string Status()
    {
        lock (Sync)
        {
            if (Fragments.Count == 0)
                return "Baza RAG jest pusta (nic nie zbudowano w tej sesji). Budowa: „rag zbuduj: folder” — tylko RAM, model: " + embeddingModel + ".";
            return "RAG: " + sourceFiles + " plików → " + Fragments.Count + " fragmentów, źródło: " + sourceRoot +
                ", model: " + embeddingModel + ", zbudowano " + builtAt.ToString("HH:mm:ss", CultureInfo.GetCultureInfo("pl-PL")) + Environment.NewLine +
                "· tylko w RAM tej sesji · szukaj: „rag szukaj: fraza”";
        }
    }

    public static string Reset()
    {
        lock (Sync)
        {
            int count = Fragments.Count;
            Fragments.Clear();
            sourceRoot = "";
            sourceFiles = 0;
            return count == 0
                ? "Baza RAG i tak była pusta."
                : "Wyczyszczono bazę RAG (" + count + " fragmentów) — była tylko w RAM, dysk był czysty.";
        }
    }
}
