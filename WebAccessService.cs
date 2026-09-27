using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SentinelX;

/// <summary>
/// SEKCJA 12 — wyłącznik WiFi i jedyny kanał do sieci. Domyślnie WYŁĄCZONY: bez jawnego
/// „wifi on” żadne polecenie nie wychodzi poza komputer. Po włączeniu (niebieska ikona 📶
/// w pasku bocznym) działa szukanie (DuckDuckGo Lite) i pobieranie pojedynczych stron —
/// wyłącznie https, z tarczą SSRF (localhost i sieci prywatne zawsze zablokowane), limitem
/// rozmiaru i czasu. Stan wyłącznika zapisuję w danych aplikacji, więc po restarcie ikona
/// pokazuje stan prawdziwy. Żadnych plików cookie, żadnej telemetrii, żadnego śledzenia.
/// </summary>
public sealed class WebAccessService
{
    /// <summary>Wspólna instancja: router, ViewModel ikony i moduły muszą widzieć ten sam stan.</summary>
    public static readonly WebAccessService Shared = new();

    private readonly HttpClient httpClient;
    private readonly string settingsPath;
    private readonly object sync = new();
    private bool enabled;

    public event Action? StateChanged;

    public WebAccessService(HttpMessageHandler? handler = null, string? settingsDirectory = null)
    {
        httpClient = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = true, UseProxy = false })
        { Timeout = TimeSpan.FromSeconds(12) };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) SentinelX/0.97 (lokalny asystent; wyłącznik WiFi po stronie użytkownika)");
        settingsPath = Path.Combine(settingsDirectory ?? Path.Combine(AppPaths.Root, "Settings"), "network.json");
        try
        {
            if (File.Exists(settingsPath)) enabled = File.ReadAllText(settingsPath).Trim() == "true";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { enabled = false; }
    }

    public bool Enabled { get { lock (sync) return enabled; } }

    public string Status()
    {
        return enabled
            ? "📶 WiFi: WŁĄCZONE (niebieska ikona w pasku). Szukanie w sieci działa: „roblox najlepsze: …”, „szukaj w sieci: …”, „strona: https://…”. Tarcza: tylko https, localhost i sieci prywatne zawsze zablokowane, limit 512 KB na stronę."
            : "📶 WiFi: WYŁĄCZONE (ikona przygaszona). Żadne zapytanie nie wychodzi poza komputer. Włącz: „wifi on”. Polecenia lokalne działają normalnie.";
    }

    public string Toggle(bool on)
    {
        lock (sync) enabled = on;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, on ? "true" : "false");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* stan i tak żyje w sesji */ }
        StateChanged?.Invoke();
        return Status();
    }

    /// <summary>Wpis wyszukiwarki: zwraca tytuł, adres i fragment. Bez WiFi odmawia; po blokadzie
    /// anty-botowej mówi to wprost, nie udaje pustych wyników.</summary>
    public async Task<string> SearchAsync(string query, CancellationToken cancellationToken = default, int limit = 6)
    {
        if (!Enabled) return WifiOffMessage();
        string phrase = (query ?? "").Trim();
        if (phrase.Length < 2) return "Podaj frazę: „szukaj w sieci: prywatność lokalnych modeli”.";
        string url = "https://lite.duckduckgo.com/lite/?q=" + Uri.EscapeDataString(phrase);
        string html;
        try { html = await DownloadAsync(url, maxBytes: 1_500_000, cancellationToken); }
        catch (HttpRequestException ex) { return "Wyszukiwarka odrzuciła połączenie (" + ex.StatusCode + "). Spróbuj za chwilę — nie udaję wyników, których nie ma."; }
        var results = ParseLiteResults(html, limit);
        if (results.Count == 0)
            return "Brak wyników albo wyszukiwarka odrzuciła zapytanie (ochrona anty-botowa). Nie zmyślam linków — spróbuj innej frazy albo za chwilę.";
        var sb = new StringBuilder("WYNIKI SZUKANIA („" + phrase + "”):").AppendLine();
        for (int i = 0; i < results.Count; i++)
        {
            sb.Append(i + 1).Append(". ").Append(results[i].Title).AppendLine();
            sb.Append("   ").Append(results[i].Url).AppendLine();
            if (results[i].Snippet.Length > 0) sb.Append("   ").Append(results[i].Snippet).AppendLine();
        }
        sb.Append("· pobranie treści strony: „strona: <adres https>” · WiFi działa, bo sam to włączyłeś");
        return sb.ToString();
    }

    /// <summary>Pobiera jedną stronę (https, limity, tarcza SSRF) i zdejmuje HTML do czytelnego tekstu.</summary>
    public async Task<string> FetchTextAsync(string address, CancellationToken cancellationToken = default)
    {
        if (!Enabled) return WifiOffMessage();
        string raw = (address ?? "").Trim();
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return "Dozwolony jest wyłącznie adres https:// — http i inne schematy odrzucam.";
        if (IsPrivateHost(uri.Host)) return "Adres wskazuje komputer lokalny albo sieć prywatną — zablokowane (tarcza SSRF działa nawet przy włączonym WiFi).";
        string html;
        try { html = await DownloadAsync(uri.ToString(), maxBytes: 512_000, cancellationToken); }
        catch (HttpRequestException ex) { return "Strona odrzuciła połączenie (" + ex.StatusCode + ") albo nie istnieje. Nie zgaduję treści."; }
        string text = StripHtml(html);
        if (text.Length == 0) return "Strona nie dała czytelnego tekstu (sam skrypt albo obrazy). Nie zmyślam treści.";
        return "TREŚĆ STRONY (" + uri.Host + ", skrócona do 4000 znaków):" + Environment.NewLine +
            (text.Length > 4000 ? text[..4000] + "…" : text);
    }

    private async Task<string> DownloadAsync(string url, int maxBytes, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        using HttpResponseMessage response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        byte[] buffer = new byte[8192];
        var memory = new MemoryStream();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        int read;
        while ((read = await stream.ReadAsync(buffer, timeout.Token)) > 0)
        {
            memory.Write(buffer, 0, read);
            if (memory.Length > maxBytes) break;
        }
        return Encoding.UTF8.GetString(memory.ToArray());
    }

    /// <summary>Tarcza SSRF: nazwy lokalne oraz adresy pętli zwrotnej, prywatne (RFC1918), łącza-lokalne,
    /// UNIK i rezerwacje IPv6. Sprawdzam każdy adres, na jaki rozwiąże się nazwa — nie tylko pierwszy.</summary>
    internal bool IsPrivateHost(string host)
    {
        string h = (host ?? "").Trim().ToLowerInvariant();
        if (h.Length == 0) return true;
        if (h == "localhost" || h.EndsWith(".localhost") || h.EndsWith(".local") || h.EndsWith(".internal")) return true;
        if (IPAddress.TryParse(h, out var literal)) return IsPrivateAddress(literal);
        try
        {
            foreach (IPAddress address in Dns.GetHostAddressesAsync(h).GetAwaiter().GetResult())
                if (IsPrivateAddress(address)) return true;
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException) { return true; }
        return false;
    }

    internal static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            byte[] o = address.GetAddressBytes();
            return o[0] == 10 || (o[0] == 172 && o[1] >= 16 && o[1] <= 31) || (o[0] == 192 && o[1] == 168)
                || (o[0] == 169 && o[1] == 254) || o[0] == 0 || o[0] >= 240;
        }
        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.ToString().StartsWith("fc", StringComparison.OrdinalIgnoreCase)
            || address.ToString().StartsWith("fd", StringComparison.OrdinalIgnoreCase) || address.ToString().StartsWith("fe80", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Parser DuckDuckGo Lite (regresja karmi go przykładowym HTML — bez sieci).</summary>
    internal static System.Collections.Generic.List<(string Title, string Url, string Snippet)> ParseLiteResults(string html, int limit)
    {
        var results = new System.Collections.Generic.List<(string, string, string)>();
        var anchors = Regex.Matches(html, @"<a\b([^>]*class='result-link'[^>]*)>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var snippets = Regex.Matches(html, @"<td[^>]*class='result-snippet'[^>]*>(.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase)
            .Select(m => Clean(m.Groups[1].Value)).ToList();
        int i = 0;
        foreach (Match anchor in anchors)
        {
            var href = Regex.Match(anchor.Groups[1].Value, @"href=""([^""]+)""", RegexOptions.IgnoreCase);
            string title = Clean(anchor.Groups[2].Value);
            if (!href.Success || title.Length == 0) continue;
            string url = DecodeRedirect(href.Groups[1].Value);
            if (url.Length == 0) continue;
            string snippet = i < snippets.Count ? snippets[i] : "";
            results.Add((title, url, snippet));
            i++;
            if (results.Count >= limit) break;
        }
        return results;
    }

    private static string DecodeRedirect(string href)
    {
        string value = System.Net.WebUtility.HtmlDecode(href);
        int marker = value.IndexOf("uddg=", StringComparison.Ordinal);
        if (marker >= 0)
        {
            string encoded = value[(marker + 5)..];
            int amp = encoded.IndexOf('&');
            if (amp >= 0) encoded = encoded[..amp];
            try { return Uri.UnescapeDataString(encoded); }
            catch (UriFormatException) { return ""; }
        }
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return value;
        return "";
    }

    private static string Clean(string html) => System.Net.WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", "")).Trim().Replace("\n", " ");

    private static string StripHtml(string html)
    {
        string s = Regex.Replace(html, "<script\\b.*?</script>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<style\\b.*?</style>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<[^>]+>", " ");
        s = System.Net.WebUtility.HtmlDecode(s);
        return Regex.Replace(s, "\\s{2,}", " ").Trim();
    }

    private static string WifiOffMessage() =>
        "📶 WiFi jest WYŁĄCZONE — żądanie nie wyszło poza komputer. Włącz wyłącznikiem 📶 w pasku bocznym albo poleceniem „wifi on”, a potem powtórz. To Twoja decyzja i widzę ją wprost.";
}
