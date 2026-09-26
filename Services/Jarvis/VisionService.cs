using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;

using SentinelX.Core;

namespace SentinelX.Services.Jarvis;

/// <summary>
/// 0.96 · wizja lokalna: zrzut ekranu wędruje do LOKALNEGO modelu wizyjnego (Ollama), a odpowiedź
/// wraca do okna. Trzy granice, których nie przesuwamy:
///  • obraz nie jest zapisywany na dysk przy tej ścieżce (BufferedVector → base64 w żądaniu),
///  • żadna chmura: tylko 127.0.0.1:11434,
///  • bez modelu wizyjnego nie udajemy, że „widzimy” — odpowiadamy, czego brakuje i jak to naprawić.
/// </summary>
public sealed class VisionService
{
    internal const long MaxImageBytes = 3_500_000;
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(120);

    /// <summary>Modele, które w Ollamie obsługują obrazy (lista nazw, nie obietnica — sprawdzamy /api/tags).</summary>
    internal static readonly string[] VisionMarkers =
    [
        "llava", "llama3.2-vision", "llama4-scout", "minicpm", "moondream", "qwen2.5vl", "qwen2-vl", "qwen2.5-vl",
        "gemma3", "granite-vision", "phi3.5vision", "phi4-multimodal", "glm-4v", "pixtral",
    ];

    private readonly HttpClient http;
    private readonly LocalAiService local;
    private readonly Func<JarvisSettings> settings;

    public VisionService(LocalAiService local, Func<JarvisSettings>? settings = null, HttpMessageHandler? handler = null)
    {
        this.local = local;
        this.settings = settings ?? (() => new JarvisSettings());
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        { BaseAddress = new Uri("http://127.0.0.1:11434"), Timeout = CallTimeout };
    }

    public static bool IsVisionModel(string name) =>
        VisionMarkers.Any(marker => (name ?? "").Contains(marker, StringComparison.OrdinalIgnoreCase));

    /// <summary>Wybór modelu: wola użytkownika, potem pierwszy pasujący zainstalowany. null = brak.</summary>
    public static string? SelectVisionModel(IReadOnlyList<string> installed, string preferred)
    {
        string want = (preferred ?? "").Trim();
        if (want.Length > 0 && installed.Contains(want, StringComparer.OrdinalIgnoreCase)) return want;
        return installed.FirstOrDefault(IsVisionModel);
    }

    /// <summary>Buduje żądanie /api/chat z jednym obrazem. Oddzielne od transportu, żeby dało się
    /// sprawdzić w teście bez modelu i bez pulpitu.</summary>
    public static string BuildBody(string model, string instruction, string imageBase64) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["role"] = "user",
                    ["content"] = instruction,
                    ["images"] = new[] { imageBase64 },
                }
            },
            ["stream"] = false,
            ["keep_alive"] = "2m",
            ["options"] = new Dictionary<string, object> { ["temperature"] = 0.1, ["num_predict"] = 512 },
        });

    public async Task<string> DescribeScreenAsync(string instruction, CancellationToken token)
    {
        (byte[] Png, int Width, int Height)? capture = ScreenCapture.CaptureForVision();
        if (capture == null)
            return "Nie udało się przechwycić ekranu — sesja bez pulpitu albo blokada systemowa.\nZrzut do pliku: „zrzut ekranu” (on jest zapisywany, ten nie jest).";
        if (capture.Value.Png.LongLength > MaxImageBytes)
            return $"Zrzut ma {capture.Value.Png.LongLength / 1024 / 1024:0.#} MB i przekracza limit {MaxImageBytes / 1024 / 1024:0.#} MB dla modelu wizyjnego.\n" +
                "Zmniejsz okna albo wyłącz część monitorów i powtórz.";

        IReadOnlyList<string> installed;
        try { installed = await local.GetInstalledModelsAsync(token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        { return "Brak połączenia z Ollama (127.0.0.1:11434) — wizja działa tylko z lokalnym modelem. Uruchom Ollama. (" + ex.Message + ")"; }

        string? model = SelectVisionModel(installed, settings().VisionModel);
        if (model == null)
            return "Nie mam modelu wizyjnego, więc NIE udaję, że widzę ekran.\nZainstaluj któryś z obsługujących obrazy, np.:\n" +
                "· ollama pull llama3.2-vision:11b\n· ollama pull qwen2.5vl:3b\n· ollama pull minicpm-v\n" +
                "Albo wskaż istniejący w Ustawienia → Jarvis → „Model wizyjny”.\n" +
                $"Zainstalowane modele do opisu: {(installed.Count == 0 ? "brak" : string.Join(", ", installed))}.";

        string image = Convert.ToBase64String(capture.Value.Png);
        try
        {
            using var content = new StringContent(BuildBody(model, instruction, image), Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat") { Content = content };
            using HttpResponseMessage response = await http.SendAsync(request, token);
            string json = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode)
                return "Model wizyjny nie odpowiedział (HTTP " + (int)response.StatusCode + "): " + Error(json);
            string answer = ReadAnswer(json);
            if (answer.Length == 0) return $"Model {model} odesłał pustą odpowiedź. Obraz miał {capture.Value.Png.LongLength / 1024:0} kB.";
            return $"👁 Wizja lokalna · {model} · kadr {capture.Value.Width}×{capture.Value.Height} px (nie zapisany na dysku)\n\n{answer}";
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return "Model wizyjny nie zdążył odpowiedzieć w 120 s. Mniejszy obraz albo lżejszy model zwykle pomaga."; }
        catch (HttpRequestException) { return "Połączenie z Ollama padło w trakcie analizy obrazu."; }
    }

    /// <summary>Treść odpowiedzi /api/chat (message.content) bez zakładania, że dokument jest poprawny.</summary>
    public static string ReadAnswer(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return "";
            if (!document.RootElement.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object) return "";
            return message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String ? content.GetString() ?? "" : "";
        }
        catch (JsonException) { return ""; }
    }

    private static string Error(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String) return error.GetString() ?? "";
        }
        catch (JsonException) { }
        return json.Length > 200 ? json[..200] : json;
    }

    /// <summary>Tekst dla strony AI: co jest gotowe, a czego brakuje.</summary>
    public async Task<string> StatusAsync(CancellationToken token)
    {
        try
        {
            IReadOnlyList<string> installed = await local.GetInstalledModelsAsync(token);
            string? model = SelectVisionModel(installed, settings().VisionModel);
            return model == null
                ? $"Brak modelu wizyjnego pośród {installed.Count} zainstalowanych. Pobierz np. qwen2.5vl:3b albo minicpm-v."
                : $"Model wizyjny: {model}. Kadr nie jest zapisywany na dysk; analiza idzie wyłącznie do lokalnej Ollamy.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or OperationCanceledException)
        { return "Ollama niedostępna — wizja wymaga lokalnego modelu. (" + ex.Message + ")"; }
    }

    /// <summary>Propozycje pytań do ekranu — dla karty w interfejsie i podpowiedzi w czacie.</summary>
    public static IReadOnlyList<string> Suggestions { get; } =
    [
        "co jest na ekranie", "przeczytaj, co jest na ekranie", "opisz okno, które mam otwarte",
        "czy na ekranie jest błąd", "podsumuj tekst, który widzę",
    ];

    public static string FormatSize(long bytes) => (bytes / 1024d).ToString("0", CultureInfo.GetCultureInfo("pl-PL")) + " kB";
}
