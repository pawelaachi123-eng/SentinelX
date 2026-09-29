using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace SentinelX.Services.Web;

/// <summary>Small read-only web research tool: search snippets and extract readable page text.
/// It never submits forms, clicks purchase buttons, or sends user data.</summary>
public sealed class WebResearchTool : IDisposable
{
    private const int MaxSearchBytes = 1_000_000;
    private const int MaxPageBytes = 2_000_000;
    private const int MaxPageCharacters = 6_000;
    private static readonly HttpClient SharedClient = CreateClient(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
    });
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private List<(string Url, string Title, string Snippet)> lastResults = [];

    private static readonly Regex SearchLink = new(
        """<a\b[^>]*class=["'][^"']*\bresult__a\b[^"']*["'][^>]*href=["'](?<url>[^"']+)["'][^>]*>(?<title>[\s\S]*?)</a>""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SearchSnippet = new(
        """<(?:a|div|td)[^>]*class=["'][^"']*\bresult__snippet\b[^"']*["'][^>]*>(?<text>[\s\S]*?)</(?:a|div|td)>""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RemoveNoise = new(
        """<(script|style|noscript|svg|nav|footer|header|form|iframe)\b[^>]*>[\s\S]*?</\1\s*>|<!--[\s\S]*?-->""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    public WebResearchTool(HttpMessageHandler? handler = null)
    {
        ownsClient = handler is not null;
        client = handler is null ? SharedClient : CreateClient(handler);
    }

    private static HttpClient CreateClient(HttpMessageHandler handler)
    {
        var result = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(12) };
        result.DefaultRequestHeaders.UserAgent.ParseAdd("SentinelX/0.93 (read-only web research)");
        result.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,text/plain;q=0.9,*/*;q=0.2");
        return result;
    }

    /// <returns>null if the input is outside this tool's supported read-only web tasks.</returns>
    public async Task<string?> TryProcessAsync(string command, CancellationToken token = default)
    {
        string raw = (command ?? string.Empty).Trim();
        string normalized = ConversationMemoryService.Normalize(raw).Trim().TrimEnd('.', '?', '!');

        var readPage = Regex.Match(raw,
            @"^(?<verb>czytaj|przeczytaj|podsumuj|streść|stresc)\s+(?:stronę|strone|ten link|tę stronę|te strone)\s+(?<url>https?://\S+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (readPage.Success)
        {
            bool summarize = ConversationMemoryService.Normalize(readPage.Groups["verb"].Value) is "podsumuj" or "stresc";
            return await ReadPageAsync(readPage.Groups["url"].Value.TrimEnd('.', ',', ')', ']'), summarize, token);
        }

        if (normalized.StartsWith("szukaj w rozmowie", StringComparison.Ordinal) ||
            normalized.StartsWith("szukaj w pamieci", StringComparison.Ordinal) ||
            normalized.StartsWith("szukaj w zadaniach", StringComparison.Ordinal) ||
            normalized.StartsWith("znajdz w rozmowie", StringComparison.Ordinal) ||
            normalized.StartsWith("wyszukaj na youtube", StringComparison.Ordinal) ||
            normalized.StartsWith("szukaj na youtube", StringComparison.Ordinal)) return null;

        var research = Regex.Match(normalized, @"^(?:zbadaj(?: temat)?|porownaj wyniki dla|przeanalizuj wyniki dla)\s+(.+)$", RegexOptions.IgnoreCase);
        if (research.Success) return await ResearchTopicAsync(research.Groups[1].Value, token);

        var contact = Regex.Match(normalized,
            @"^(?:znajdz|wyszukaj|podaj)(?: mi)?\s+(?:oficjalny numer(?: telefonu)?|numer telefonu|oficjalne dane kontaktowe|dane kontaktowe|kontakt)\s+(?:firmy\s+)?(.+)$",
            RegexOptions.IgnoreCase);
        string query;
        if (contact.Success)
            query = contact.Groups[1].Value + " oficjalna strona kontakt numer telefonu";
        else
        {
            var search = Regex.Match(normalized,
                @"^(?:wyszukaj|szukaj)(?: w internecie| w google)?\s+(.+)$",
                RegexOptions.IgnoreCase);
            if (search.Success) query = search.Groups[1].Value.Trim();
            else
            {
                var naturalSearch = Regex.Match(normalized,
                    @"^(?:znajdz|poszukaj)(?: mi)?\s+(?:(?:informacje|informacji|wiadomosci) o\s+)?(.+)$|^sprawdz (?:w internecie|online|w google)\s+(.+)$",
                    RegexOptions.IgnoreCase);
                if (!naturalSearch.Success) return null;
                query = naturalSearch.Groups[1].Success ? naturalSearch.Groups[1].Value.Trim() : naturalSearch.Groups[2].Value.Trim();
            }
        }
        if (query.Length is < 2 or > 240) return "Podaj krótszą frazę do wyszukania.";
        return await SearchAsync(query, contact.Success, token);
    }

    public async Task<string> ResearchTopicAsync(string query, CancellationToken token = default)
    {
        string search = await SearchAsync(query, token: token);
        var sources = lastResults.Take(3).ToArray();
        if (sources.Length == 0) return search;
        var summaries = await Task.WhenAll(sources.Select(source => ReadPageAsync(source.Url, summarize: true, token: token)));
        var output = new StringBuilder("Zestawienie do 3 znalezionych stron (skrót ekstrakcyjny, bez niezależnej weryfikacji):\n");
        for (int i = 0; i < sources.Length; i++)
            output.Append("\n").Append(i + 1).Append(". ").Append(sources[i].Title).Append("\n").Append(summaries[i]);
        return output.ToString();
    }

    public bool TryExtractOpenUrl(string command, out string url)
    {
        url = string.Empty;
        Match match = Regex.Match((command ?? string.Empty).Trim(),
            @"^(?:otwórz|otworz|uruchom)(?:\s+(?:mi|proszę|prosze))*\s+(?:stronę|strone|link)\s+(?<url>https?://\S+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return false;
        string candidate = match.Groups["url"].Value.TrimEnd('.', ',', ')', ']');
        if (!IsHttpUrl(candidate)) return false;
        url = candidate;
        return true;
    }

    public bool TryResolveRecentResult(string command, out string url, out bool read, out bool summarize)
    {
        url = string.Empty; read = false; summarize = false;
        string normalized = ConversationMemoryService.Normalize(command).Trim().TrimEnd('.', '!', '?');
        Match match = Regex.Match(normalized,
            @"^(?<verb>otworz|czytaj|przeczytaj|podsumuj|stresc)\s+(?<index>1|2|3|pierwszy|pierwsza|pierwsze|drugi|druga|drugie|trzeci|trzecia|trzecie)\s+(?:strone|wynik|link)$",
            RegexOptions.IgnoreCase);
        if (!match.Success) return false;
        int index = match.Groups["index"].Value switch
        {
            "1" or "pierwszy" or "pierwsza" or "pierwsze" => 0,
            "2" or "drugi" or "druga" or "drugie" => 1,
            "3" or "trzeci" or "trzecia" or "trzecie" => 2,
            _ => -1
        };
        if (index < 0 || index >= lastResults.Count) return false;
        string verb = match.Groups["verb"].Value;
        read = verb is "czytaj" or "przeczytaj" or "podsumuj" or "stresc";
        summarize = verb is "podsumuj" or "stresc";
        url = lastResults[index].Url;
        return true;
    }

    public async Task<string> SearchAsync(string query, bool emphasizeOfficial = false, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        lastResults = [];
        string searchTerms = emphasizeOfficial ? query + " official company contact" : query;
        string searchUrl = "https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(searchTerms);
        try
        {
            string html = await GetTextAsync(searchUrl, MaxSearchBytes, token, allowOnlySearchHost: true);
            var links = SearchLink.Matches(html).Cast<Match>()
                .Select(m => (Url: DecodeSearchUrl(WebUtility.HtmlDecode(m.Groups["url"].Value)), Title: CleanHtml(m.Groups["title"].Value)))
                .Where(x => IsHttpUrl(x.Url) && x.Title.Length > 0)
                .DistinctBy(x => x.Url, StringComparer.OrdinalIgnoreCase)
                .Take(8).ToArray();
            var snippets = SearchSnippet.Matches(html).Cast<Match>().Select(m => CleanHtml(m.Groups["text"].Value)).ToArray();
            if (links.Length == 0) { lastResults = []; return "Wyszukiwarka nie zwróciła czytelnych wyników. Nie otworzyłem ani nie zweryfikowałem żadnej strony."; }
            lastResults = links.Select((item, index) => (Url: item.Url, Title: item.Title, Snippet: index < snippets.Length ? snippets[index] : string.Empty)).Take(5).ToList();

            var output = new StringBuilder(emphasizeOfficial
                ? "Znalazłem kandydatów na oficjalne dane kontaktowe. Sprawdź domenę i treść przed użyciem — to wyniki wyszukiwania, nie potwierdzenie numeru.\n"
                : "Wyniki wyszukiwania (fragmenty ze stron; nie są niezależnie weryfikowane):\n");
            for (int i = 0; i < Math.Min(5, links.Length); i++)
            {
                output.Append('\n').Append(i + 1).Append(". ").Append(links[i].Title).Append("\n").Append(links[i].Url);
                if (i < snippets.Length && snippets[i].Length > 0) output.Append("\n").Append(snippets[i]);
            }
            output.Append("\n\nPolecenia: „czytaj stronę https://…” pobiera tekst strony; „otwórz https://…” otwiera ją w przeglądarce.");
            return output.ToString();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException or UriFormatException)
        { return "Nie udało się pobrać wyników z sieci: " + ex.Message; }
    }

    public Task<string> ReadPageAsync(string url, CancellationToken token = default) => ReadPageAsync(url, summarize: false, token: token);

    public async Task<string> ReadPageAsync(string url, bool summarize, CancellationToken token = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || !IsHttpUrl(uri.AbsoluteUri))
            return "Nieprawidłowy adres. Obsługuję wyłącznie publiczne strony HTTP/HTTPS.";
        try
        {
            string html = await GetTextAsync(uri.AbsoluteUri, MaxPageBytes, token, allowOnlySearchHost: false);
            string title = ExtractTitle(html);
            string text = CleanHtml(html);
            if (text.Length == 0) return "Pobrana strona nie zawiera czytelnego tekstu.";
            if (summarize) text = ExtractiveSummary(text);
            else if (text.Length > MaxPageCharacters) text = text[..MaxPageCharacters] + "… [ucięto; limit odczytu]";
            return (title.Length > 0 ? title + "\n" : "") + "Źródło: " + uri.AbsoluteUri + "\n\n" + text;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException or UriFormatException or SocketException)
        { return "Nie udało się bezpiecznie odczytać strony: " + ex.Message; }
    }

    private async Task<string> GetTextAsync(string address, int limit, CancellationToken token, bool allowOnlySearchHost)
    {
        Uri current = new(address);
        for (int redirect = 0; redirect <= 3; redirect++)
        {
            if (!IsHttpUrl(current.AbsoluteUri)) throw new InvalidOperationException("Dozwolone są tylko publiczne adresy HTTP/HTTPS.");
            if (allowOnlySearchHost && !current.Host.Equals("html.duckduckgo.com", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Odrzucono przekierowanie poza host wyszukiwarki.");
            if (!allowOnlySearchHost && !await IsPublicHostAsync(current.Host, token))
                throw new InvalidOperationException("Odrzucono adres lokalny lub prywatny.");

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                if (redirect == 3 || response.Headers.Location is null) throw new HttpRequestException("Zbyt wiele przekierowań.");
                current = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(current, response.Headers.Location);
                continue;
            }
            response.EnsureSuccessStatusCode();
            string? mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null && !(mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
                mediaType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Odczyt jest ograniczony do stron i dokumentów tekstowych.");
            if (response.Content.Headers.ContentLength is long size && size > limit) throw new IOException("Strona przekracza limit pobierania.");
            using Stream stream = await response.Content.ReadAsStreamAsync(token);
            using var buffer = new MemoryStream(Math.Min(limit, 64 * 1024));
            byte[] chunk = new byte[16 * 1024];
            while (buffer.Length < limit)
            {
                int read = await stream.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, limit - buffer.Length)), token);
                if (read == 0) break;
                await buffer.WriteAsync(chunk.AsMemory(0, read), token);
            }
            if (buffer.Length == limit) throw new IOException("Przekroczono limit rozmiaru odpowiedzi.");
            Encoding encoding = Encoding.UTF8;
            try
            {
                string? charset = response.Content.Headers.ContentType?.CharSet?.Trim('"');
                if (!string.IsNullOrWhiteSpace(charset)) encoding = Encoding.GetEncoding(charset);
            }
            catch (ArgumentException) { }
            return encoding.GetString(buffer.ToArray());
        }
        throw new HttpRequestException("Nie udało się pobrać strony.");
    }

    private static async Task<bool> IsPublicHostAsync(string host, CancellationToken token)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return false;
        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out IPAddress? address)) addresses = [address];
        else addresses = await Dns.GetHostAddressesAsync(host, token);
        return addresses.Length > 0 && addresses.All(IsPublicAddress);
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast) return false;
        if (address.IsIPv4MappedToIPv6) return IsPublicAddress(address.MapToIPv4());
        byte[] b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            if (b[0] is 0 or 10 or 127 or >= 224) return false;
            if (b[0] == 169 && b[1] == 254) return false;
            if (b[0] == 172 && b[1] is >= 16 and <= 31) return false;
            if (b[0] == 192 && (b[1] == 168 || (b[1] == 0 && (b[2] is 0 or 2)))) return false;
            if (b[0] == 100 && b[1] is >= 64 and <= 127) return false;
            if (b[0] == 198 && (b[1] is 18 or 19)) return false;
            if (b[0] == 198 && b[1] == 51 && b[2] == 100) return false;
            if (b[0] == 203 && b[1] == 0 && b[2] == 113) return false;
            return true;
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return (b[0] & 0xe0) == 0x20 && !(b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8);
        return false;
    }

    private static bool IsHttpUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && string.IsNullOrEmpty(uri.UserInfo);

    private static string DecodeSearchUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
            !Uri.TryCreate(new Uri("https://html.duckduckgo.com/"), value, out uri)) return string.Empty;
        if (uri.Host.EndsWith("duckduckgo.com", StringComparison.OrdinalIgnoreCase) && uri.Query.Length > 0)
        {
            foreach (string part in uri.Query.TrimStart('?').Split('&'))
            {
                string[] pair = part.Split('=', 2);
                if (pair.Length == 2 && WebUtility.UrlDecode(pair[0]) == "uddg")
                {
                    string target = WebUtility.UrlDecode(pair[1]) ?? string.Empty;
                    if (Uri.TryCreate(target, UriKind.Absolute, out _)) return target;
                }
            }
        }
        return uri.AbsoluteUri;
    }

    private static string ExtractiveSummary(string text)
    {
        var sentences = Regex.Split(text, @"(?<=[.!?])\s+")
            .Where(x => x.Trim().Length > 25).Take(5).ToArray();
        string summary = sentences.Length > 0 ? string.Join(" ", sentences) : string.Join(" ", text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(110));
        if (summary.Length > 1_200) summary = summary[..1_200] + "…";
        return "Skrót z początku widocznej treści (ekstrakcyjny; bez niezależnej weryfikacji):\n" + summary;
    }

    private static string ExtractTitle(string html)
    {
        Match match = Regex.Match(html, """<title\b[^>]*>(?<title>[\s\S]*?)</title>""", RegexOptions.IgnoreCase);
        return match.Success ? CleanHtml(match.Groups["title"].Value) : string.Empty;
    }

    private static string CleanHtml(string html)
    {
        string text = RemoveNoise.Replace(html ?? string.Empty, " ");
        text = Tags.Replace(text.Replace("<br>", " ", StringComparison.OrdinalIgnoreCase).Replace("</p>", " ", StringComparison.OrdinalIgnoreCase), " ");
        return Spaces.Replace(WebUtility.HtmlDecode(text), " ").Trim();
    }

    public void Dispose() { if (ownsClient) client.Dispose(); }
}
