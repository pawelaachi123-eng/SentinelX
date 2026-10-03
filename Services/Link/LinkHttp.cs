using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SentinelX.Services.Link;

internal static class LinkJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };
}

/// <summary>One parsed HTTP/1.1 request. Only what the link needs: no keep-alive, no chunked uploads, tiny limits.</summary>
internal sealed class LinkRequest
{
    public string Method { get; init; } = "GET";
    public string Path { get; init; } = "/";
    public IReadOnlyDictionary<string, string> Query { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public byte[] Body { get; init; } = [];
    public IPAddress Remote { get; init; } = IPAddress.None;

    public string Bearer
    {
        get
        {
            return Headers.TryGetValue("Authorization", out string? value) && value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? value[7..].Trim() : "";
        }
    }

    public string Q(string name) => Query.TryGetValue(name, out string? value) ? value : "";
    public string RequestId => Headers.TryGetValue("X-Sentinel-Request-Id", out string? value) ? value.Trim() : "";

    /// <summary>Deserializes a v1 request contract; malformed or empty bodies are handled as client input errors by the endpoint.</summary>
    public T? Deserialize<T>() where T : class
    {
        if (Body.Length == 0) return null;
        try { return JsonSerializer.Deserialize<T>(Body, LinkJson.Options); }
        catch (JsonException) { return null; }
    }
}

internal static class LinkHttp
{
    private const int MaxHead = 16 * 1024;
    private const int MaxBody = 64 * 1024;

    /// <summary>Reads one request. Returns null when the client closed the connection without sending anything.</summary>
    public static async Task<LinkRequest?> ReadAsync(Stream stream, IPAddress remote, CancellationToken token)
    {
        byte[] buffer = new byte[MaxHead];
        int filled = 0, headEnd = -1;
        while (headEnd < 0)
        {
            if (filled == buffer.Length) throw new InvalidDataException("Nagłówki żądania są za duże.");
            int read = await stream.ReadAsync(buffer.AsMemory(filled), token).ConfigureAwait(false);
            if (read == 0) return null;
            filled += read;
            headEnd = IndexOfHeadEnd(buffer, filled);
        }
        string head = Encoding.Latin1.GetString(buffer, 0, headEnd);
        string[] lines = head.Split("\r\n");
        string[] first = lines[0].Split(' ');
        if (first.Length != 3 || !first[2].StartsWith("HTTP/1.", StringComparison.Ordinal) || first[1].Length == 0 || first[1][0] != '/')
            throw new InvalidDataException("Niepoprawna linia żądania.");
        string method = first[0].ToUpperInvariant();
        if (method is not ("GET" or "POST" or "HEAD" or "OPTIONS" or "DELETE" or "PUT")) throw new InvalidDataException("Nieobsługiwana metoda.");

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length && headers.Count < 64; i++)
        {
            int colon = lines[i].IndexOf(':');
            if (colon <= 0) continue;
            headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
        }
        if (headers.ContainsKey("Transfer-Encoding")) throw new InvalidDataException("Przesyłanie porcjami nie jest obsługiwane.");

        long length = 0;
        if (headers.TryGetValue("Content-Length", out string? rawLength) && (!long.TryParse(rawLength, out length) || length < 0))
            throw new InvalidDataException("Niepoprawna długość treści.");
        if (length > MaxBody) throw new InvalidDataException("Treść żądania jest za duża.");

        byte[] body = new byte[length];
        int bodyStart = headEnd + 4;
        int already = Math.Min(filled - bodyStart, body.Length);
        if (already > 0) Buffer.BlockCopy(buffer, bodyStart, body, 0, already);
        int got = Math.Max(already, 0);
        while (got < body.Length)
        {
            int read = await stream.ReadAsync(body.AsMemory(got), token).ConfigureAwait(false);
            if (read == 0) throw new IOException("Połączenie zamknięte w trakcie odbierania treści.");
            got += read;
        }

        string target = first[1];
        int question = target.IndexOf('?');
        string rawPath = question < 0 ? target : target[..question];
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        if (question >= 0)
        {
            foreach (string pair in target[(question + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                string key = Decode(eq < 0 ? pair : pair[..eq]);
                query[key] = eq < 0 ? "" : Decode(pair[(eq + 1)..]);
            }
        }
        return new LinkRequest { Method = method, Path = Decode(rawPath), Query = query, Headers = headers, Body = body, Remote = remote };
    }

    private static string Decode(string value)
    {
        try { return Uri.UnescapeDataString(value.Replace('+', ' ')); }
        catch (UriFormatException) { return value; }
    }

    private static int IndexOfHeadEnd(byte[] buffer, int count)
    {
        for (int i = 0; i + 3 < count; i++)
            if (buffer[i] == '\r' && buffer[i + 1] == '\n' && buffer[i + 2] == '\r' && buffer[i + 3] == '\n') return i;
        return -1;
    }
}

internal sealed class LinkResponse
{
    private static readonly Dictionary<int, string> Reasons = new()
    {
        [200] = "OK", [202] = "Accepted", [204] = "No Content", [304] = "Not Modified", [400] = "Bad Request", [401] = "Unauthorized", [403] = "Forbidden",
        [404] = "Not Found", [405] = "Method Not Allowed", [408] = "Request Timeout", [409] = "Conflict", [411] = "Length Required",
        [413] = "Payload Too Large", [422] = "Unprocessable Content", [429] = "Too Many Requests", [500] = "Internal Server Error", [503] = "Service Unavailable"
    };
    private const string HtmlPolicy = "default-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    private readonly Stream stream;
    private readonly bool headOnly;

    public LinkResponse(Stream stream, bool headOnly = false)
    {
        this.stream = stream;
        this.headOnly = headOnly;
    }

    public bool Started { get; private set; }

    public async Task WriteAsync(int status, string contentType, byte[] body, CancellationToken token, string? cacheControl = null, string? etag = null)
    {
        var head = new StringBuilder();
        head.Append("HTTP/1.1 ").Append(status).Append(' ').Append(Reasons.GetValueOrDefault(status, "OK")).Append("\r\n");
        head.Append("Content-Type: ").Append(contentType).Append("\r\n");
        head.Append("Content-Length: ").Append(body.Length).Append("\r\n");
        head.Append("Cache-Control: ").Append(cacheControl ?? "no-store").Append("\r\n");
        if (etag != null) head.Append("ETag: ").Append(etag).Append("\r\n");
        head.Append("Connection: close\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nX-Frame-Options: DENY\r\n");
        if (contentType.StartsWith("text/html", StringComparison.Ordinal)) head.Append("Content-Security-Policy: ").Append(HtmlPolicy).Append("\r\n");
        head.Append("\r\n");
        Started = true;
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), token).ConfigureAwait(false);
        if (!headOnly && body.Length > 0) await stream.WriteAsync(body, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    public Task WriteJsonAsync(int status, object value, CancellationToken token) =>
        WriteAsync(status, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(value, LinkJson.Options), token);

    /// <summary>Streams a bounded, server-owned file without loading it into memory as one byte array.</summary>
    public async Task WriteFileAsync(int status, FileStream file, CancellationToken token)
    {
        long length = file.Length;
        const string contentType = "application/octet-stream";
        var head = new StringBuilder();
        head.Append("HTTP/1.1 ").Append(status).Append(' ').Append(Reasons.GetValueOrDefault(status, "OK")).Append("\r\n");
        head.Append("Content-Type: ").Append(contentType).Append("\r\n");
        head.Append("Content-Length: ").Append(length).Append("\r\n");
        head.Append("Cache-Control: no-store\r\nConnection: close\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\n\r\n");
        Started = true;
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), token).ConfigureAwait(false);
        if (!headOnly)
        {
            file.Position = 0;
            byte[] buffer = new byte[64 * 1024];
            int read;
            while ((read = await file.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
                await stream.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
        }
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    public async Task StartSseAsync(CancellationToken token)
    {
        const string head = "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream; charset=utf-8\r\nCache-Control: no-store\r\nConnection: close\r\nX-Accel-Buffering: no\r\nX-Content-Type-Options: nosniff\r\n\r\n";
        Started = true;
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    public async Task WriteSseAsync(string name, object data, CancellationToken token)
    {
        string json = JsonSerializer.Serialize(data, LinkJson.Options);
        await stream.WriteAsync(Encoding.UTF8.GetBytes("event: " + name + "\ndata: " + json + "\n\n"), token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }
}
