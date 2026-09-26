using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace SentinelX.Services.Jarvis;

/// <summary>
/// 0.96 · pamięć semantyczna (pozycja P2 z backloga, w wersji ostrożnej): lokalny indeks wektorów
/// liczony przez Ollamę z modelu osadzeń. Wyszukiwanie tekstowe ZOSTAJE domyślne i nietknięte —
/// ten indeks jest wyłącznie dodatkiem, który trzeba włączyć w ustawieniach.
///
/// Co jest tu uczciwe:
///  • bez modelu osadzeń nie udajemy semantyki: „indeks semantyczny: status” mówi, czego brakuje;
///  • indeks jest tworzony z TEGO, co już jest w pamięci, i nigdy nie zmienia wspomnień;
///  • plik indeksu ma atomowy zapis i kopię uszkodzonego pliku zamiast nadpisania (jak pozostałe magazyny);
///  • usunięcie indeksu nie kasuje ani jednego wspomnienia — to tylko pochodna.
/// </summary>
public sealed class SemanticMemoryIndex
{
    internal const int MaxItems = 400;
    internal const int MaxItemCharacters = 600;
    private const int SchemaVersion = 1;
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(20);

    private readonly HttpClient http;
    private readonly LocalAiService local;
    private readonly ConversationMemoryService memory;
    private readonly Func<JarvisSettings> settings;
    private readonly string indexPath;
    private readonly object gate = new();
    private List<IndexedItem>? cached;

    public sealed record IndexedItem(string Id, string Kind, string Text, double[] Vector, DateTime IndexedAt);

    public SemanticMemoryIndex(LocalAiService local, ConversationMemoryService memory, Func<JarvisSettings>? settings = null,
        HttpMessageHandler? handler = null, string? memoryDirectory = null)
    {
        this.local = local; this.memory = memory;
        this.settings = settings ?? (() => new JarvisSettings());
        indexPath = Path.Combine(memoryDirectory ?? AppPaths.MemoryDirectory, "semantic-index.json");
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        { BaseAddress = new Uri("http://127.0.0.1:11434"), Timeout = CallTimeout };
    }

    public string IndexPath => indexPath;
    public bool IsEnabled => settings().SemanticSearchEnabled;
    public string ModelName => settings().EmbeddingModel.Trim().Length > 0 ? settings().EmbeddingModel.Trim() : "nomic-embed-text";

    public int Count
    {
        get
        {
            List<IndexedItem>? items = Load();
            return items?.Count ?? 0;
        }
    }

    /// <summary>Czy indeks nadaje się do użycia: włączony, niepusty i z bieżącego modelu.</summary>
    public string Status()
    {
        if (!IsEnabled)
            return "Wyszukiwanie semantyczne jest WYŁĄCZONE w ustawieniach (Jarvis → „Indeks semantyczny”). " +
                "Wyszukiwanie tekstowe działa bez niego i nie jest zmieniane.";
        List<IndexedItem>? items = Load();
        if (items == null)
            return File.Exists(indexPath)
                ? "Plik indeksu jest uszkodzony — zostawiliśmy jego kopię i nic nie nadpisaliśmy. Zbuduj indeks od nowa: „indeks semantyczny: zbuduj”."
                : "Indeks nie istnieje. Zbuduj go: „indeks semantyczny: zbuduj”. Do liczenia potrzebny jest model osadzeń, np. ollama pull nomic-embed-text.";
        return $"Indeks: {items.Count} wpisów · model osadzeń {ModelName}\nPlik: {indexPath}\n" +
            "Semantyka jest tylko dodatkiem: „szukaj w zadaniach”, „szukaj w rozmowie” i „co pamiętasz” działają jak dawniej.";
    }

    /// <summary>Liczy wektory dla wspomnień i wypowiedzi z tej sesji. Nie zapisuje treści poza indeks.</summary>
    public async Task<string> BuildAsync(Action<string>? onProgress, CancellationToken token)
    {
        if (!IsEnabled) return Status();
        var sources = CollectSources();
        if (sources.Count == 0) return "Nie mam czego indeksować — pamięć jest pusta (albo tryb prywatny nic nie zapisuje).";
        IReadOnlyList<string> installed;
        try { installed = await local.GetInstalledModelsAsync(token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        { return "Brak Ollamy — nie policzę wektorów. Uruchom Ollama i powtórz. (" + ex.Message + ")"; }
        string model = ModelName;
        if (!installed.Contains(model, StringComparer.OrdinalIgnoreCase))
            return $"Modelu osadzeń „{model}” nie ma wśród zainstalowanych ({(installed.Count == 0 ? "brak modeli" : string.Join(", ", installed))}).\n" +
                $"Pobierz: ollama pull {model} — albo zmień model w Ustawienia → Jarvis.";

        var items = new List<IndexedItem>(sources.Count);
        int failed = 0;
        foreach ((string id, string kind, string text) in sources)
        {
            token.ThrowIfCancellationRequested();
            double[]? vector = await EmbedAsync(model, text, token);
            if (vector == null) { failed++; continue; }
            items.Add(new IndexedItem(id, kind, text, vector, DateTime.Now));
            onProgress?.Invoke($"\n· indeksowanie {items.Count}/{sources.Count} · {kind}");
            if (items.Count >= MaxItems) break;
        }
        if (items.Count == 0)
            return "Model osadzeń nie zwrócił żadnego wektora — indeks NIE został zapisany (stary, jeśli był, zostaje nietknięty).\n" +
                "Sprawdź: ollama list · rozmiar modelu · wolna pamięć.";
        if (!Save(items, model, out string error)) return error;
        lock (gate) cached = items;
        return $"Indeks semantyczny zbudowany: {items.Count} wpisów modelem {model}" +
            (failed > 0 ? $" (pominięto {failed} bez wektora)" : "") +
            (sources.Count > MaxItems ? $" — limit {MaxItems} wpisów" : "") + $".\nPlik: {indexPath}\n" +
            "Szukaj: „szukaj semantycznie: fraza”. Wspomnienia i rozmowy NIE zostały zmienione.";
    }

    /// <summary>Szuka po podobieństwie kosinusowym. Zwraca puste, gdy indeks jest nieaktywny.</summary>
    public async Task<IReadOnlyList<(string Kind, string Text, double Score)>> SearchAsync(string query, int take, CancellationToken token)
    {
        if (!IsEnabled || query.Trim().Length == 0) return [];
        List<IndexedItem>? items = Load();
        if (items == null || items.Count == 0) return [];
        double[]? vector = await EmbedAsync(ModelName, query.Trim(), token);
        if (vector == null) return [];
        var scored = new List<(string Kind, string Text, double Score)>(items.Count);
        foreach (IndexedItem item in items)
        {
            double score = Cosine(vector, item.Vector);
            if (score >= 0.20) scored.Add((item.Kind, item.Text, score));
        }
        scored.Sort((a, b) => b.Score.CompareTo(a.Score));
        return scored.Take(Math.Clamp(take, 1, 20)).ToList();
    }

    /// <summary>Usuwa SAM PLIK INDEKSU (pochodną). Wspomnienia, rozmowy i archiwa zostają nietknięte.</summary>
    public string Clear()
    {
        try
        {
            if (!File.Exists(indexPath)) { lock (gate) cached = []; return "Indeksu nie było — nic nie usunięto. Wspomnienia są nietknięte."; }
            string parked = indexPath + ".removed-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Move(indexPath, parked);   // nigdy kasowanie: zostaje dowód, że to tylko pochodna
            lock (gate) cached = [];
            return $"Usunąłem indeks (plik odłożony bez kasowania: {Path.GetFileName(parked)}).\nWspomnienia i rozmowy NIE zostały ruszone.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return "Nie udało się usunąć indeksu: " + ex.Message; }
    }

    // ------------------------------------------------------------------ czyste funkcje

    /// <summary>Podobieństwo kosinusowe; niezgodne wymiary i zera dają 0, nie NaN.</summary>
    public static double Cosine(double[] a, double[] b)
    {
        if (a.Length == 0 || a.Length != b.Length) return 0;
        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (!double.IsFinite(a[i]) || !double.IsFinite(b[i])) return 0;
            dot += a[i] * b[i]; normA += a[i] * a[i]; normB += b[i] * b[i];
        }
        if (normA <= 0 || normB <= 0) return 0;
        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    public static string EmbeddingsBody(string model, string prompt) =>
        JsonSerializer.Serialize(new Dictionary<string, object> { ["model"] = model, ["prompt"] = prompt });

    public static string EmbedBody(string model, string prompt) =>
        JsonSerializer.Serialize(new Dictionary<string, object> { ["model"] = model, ["input"] = new[] { prompt } });

    /// <summary>Znosi oba kształty odpowiedzi Ollamy: {embedding:[…]} i {embeddings:[[…]]}.</summary>
    public static double[]? ParseEmbedding(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("embedding", out var single) && single.ValueKind == JsonValueKind.Array) return ReadVector(single);
            if (root.TryGetProperty("embeddings", out var many) && many.ValueKind == JsonValueKind.Array)
                foreach (JsonElement row in many.EnumerateArray())
                    if (row.ValueKind == JsonValueKind.Array) { double[]? vector = ReadVector(row); if (vector != null) return vector; }
            return null;
        }
        catch (JsonException) { return null; }
    }

    private static double[]? ReadVector(JsonElement array)
    {
        var values = new List<double>();
        foreach (JsonElement item in array.EnumerateArray())
            if (item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out double value) && double.IsFinite(value)) values.Add(value);
        return values.Count == 0 ? null : values.ToArray();
    }

    /// <summary>Źródła indeksu: trwałe wspomnienia + wypowiedzi bieżącej rozmowy. Bez treści z dysku.</summary>
    private List<(string Id, string Kind, string Text)> CollectSources()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var sources = new List<(string, string, string)>();
        foreach (ConversationMemoryEntry note in memory.GetNotes())
        {
            string text = Clip(note.Text);
            if (text.Length == 0 || !seen.Add("n:" + note.Id)) continue;
            sources.Add((note.Id.Length > 0 ? note.Id : "wspomnienie", "wspomnienie", text));
        }
        foreach (ConversationMemoryEntry entry in memory.GetRecentEntries(200))
        {
            string text = Clip(entry.Text);
            if (text.Length == 0 || !seen.Add("e:" + entry.Id + text)) continue;
            sources.Add((entry.Id, entry.Role == "user" ? "twoja wypowiedź" : "odpowiedź", text));
        }
        return sources.Take(MaxItems).ToList();
    }

    private static string Clip(string text)
    {
        string value = (text ?? "").Trim();
        if (value.Length == 0) return "";
        return value.Length <= MaxItemCharacters ? value : value[..MaxItemCharacters] + "…";
    }

    // ------------------------------------------------------------------ zapis i odczyt

    private List<IndexedItem>? Load()
    {
        lock (gate) if (cached != null) return cached;
        try
        {
            if (!File.Exists(indexPath)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(indexPath));
            List<IndexedItem> items = ReadIndex(document.RootElement);
            lock (gate) cached = items;
            return items;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        { ParkCorrupt(); return null; }
    }

    public static List<IndexedItem> ReadIndex(JsonElement root)
    {
        var items = new List<IndexedItem>();
        if (root.ValueKind != JsonValueKind.Object) return items;
        if (!root.TryGetProperty("items", out var list) || list.ValueKind != JsonValueKind.Array) return items;
        foreach (JsonElement item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            string id = Text(item, "id"), kind = Text(item, "kind"), content = Text(item, "text");
            if (content.Length == 0 || !item.TryGetProperty("vector", out var vector) || vector.ValueKind != JsonValueKind.Array) continue;
            double[] values = ParseEmbedding(JsonSerializer.Serialize(new { embedding = vector })) ?? [];
            if (values.Length == 0) continue;
            DateTime at = DateTime.TryParse(Text(item, "indexedAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed) ? parsed : DateTime.Now;
            items.Add(new IndexedItem(id, kind, content, values, at));
        }
        return items;
    }

    private bool Save(List<IndexedItem> items, string model, out string error)
    {
        error = "";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
            string payload = JsonSerializer.Serialize(new
            {
                schemaVersion = SchemaVersion,
                model,
                builtAt = DateTime.Now.ToString("O", CultureInfo.InvariantCulture),
                items = items.Select(x => new { id = x.Id, kind = x.Kind, text = x.Text, vector = x.Vector, indexedAt = x.IndexedAt.ToString("O", CultureInfo.InvariantCulture) }),
            }, new JsonSerializerOptions { WriteIndented = true });
            string temp = indexPath + ".tmp";
            File.WriteAllText(temp, payload);
            // Odczyt zwrotny ZANIM plik zastąpi poprzedni — bez tego indeks mógłby być pusty i tak wyglądać na udany.
            using (var verify = JsonDocument.Parse(File.ReadAllText(temp)))
                if (ReadIndex(verify.RootElement).Count != items.Count)
                { File.Delete(temp); error = "Zapis indeksu nie przeżył odczytu zwrotnego — stary indeks zostaje nietknięty."; return false; }
            if (File.Exists(indexPath)) File.Copy(indexPath, indexPath + ".previous", true);
            File.Move(temp, indexPath, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { error = "Nie udało się zapisać indeksu: " + ex.Message; return false; }
    }

    /// <summary>Uszkodzony plik jest odkładany z dopiskiem, nigdy nadpisany ani skasowany.</summary>
    private void ParkCorrupt()
    {
        try
        {
            if (!File.Exists(indexPath)) return;
            string parked = indexPath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            int suffix = 1;
            while (File.Exists(parked)) parked = indexPath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + suffix++;
            File.Move(indexPath, parked, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write(ex); }
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private async Task<double[]?> EmbedAsync(string model, string prompt, CancellationToken token)
    {
        foreach ((string path, string body) in new[] { ("/api/embeddings", EmbeddingsBody(model, prompt)), ("/api/embed", EmbedBody(model, prompt)) })
        {
            try
            {
                using var content = new StringContent(body, Encoding.UTF8, "application/json");
                using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
                using HttpResponseMessage response = await http.SendAsync(request, token);
                if (!response.IsSuccessStatusCode) continue;
                double[]? vector = ParseEmbedding(await response.Content.ReadAsStringAsync(token));
                if (vector != null) return vector;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException) { /* next endpoint */ }
        }
        return null;
    }
}
