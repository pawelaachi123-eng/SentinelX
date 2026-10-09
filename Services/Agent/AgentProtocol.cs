using System.IO;
using System.Text;
using System.Text.Json;

namespace SentinelX.Services.Agent;

public sealed record AgentRequest(string Op, string Token, JsonElement Payload);

public sealed class AgentException(string code) : IOException(code) { public string Code { get; } = code; }

/// <summary>Newline-delimited JSON envelopes over the loopback named pipe.
/// One request per connection; every envelope answers ok/data or ok=false/error.</summary>
public static class AgentProtocol
{
    public const int MaxMessage = 1 << 20;

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        MaxDepth = 16
    };

    public static async Task<string?> ReadMessageAsync(Stream stream, CancellationToken cancel)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
        var builder = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            int read = await reader.ReadAsync(buffer, cancel).ConfigureAwait(false);
            if (read == 0) break;
            int newline = Array.IndexOf(buffer, '\n', 0, read);
            builder.Append(buffer, 0, newline >= 0 ? newline : read);
            // Incremental cap: past MaxMessage chars the bytes already exceed it,
            // so an unauthenticated peer cannot grow this buffer without a newline.
            if (builder.Length > MaxMessage)
            {
                // The line is oversize. If its newline arrived in this chunk the
                // line is complete; otherwise drain the bounded remainder so the
                // explicit error reply still fits the protocol (an infinite stream
                // aborts the drain and the reply is best-effort).
                if (newline < 0)
                {
                    long drained = 0;
                    var drain = new char[4096];
                    while (drained < 8L * 1024 * 1024)
                    {
                        int rest = await reader.ReadAsync(drain, cancel).ConfigureAwait(false);
                        if (rest == 0) break;
                        drained += rest;
                        if (Array.IndexOf(drain, '\n', 0, rest) >= 0) break;
                    }
                }
                throw new AgentException("size");
            }
            if (newline >= 0) break;
        }
        if (builder.Length == 0) return null;
        if (builder[^1] == '\r') builder.Length--;
        string line = builder.ToString();
        if (Encoding.UTF8.GetByteCount(line) > MaxMessage) throw new AgentException("size");
        return line;
    }

    public static async Task WriteMessageAsync(Stream stream, object value, CancellationToken cancel)
    {
        string line = JsonSerializer.Serialize(value, Json);
        if (Encoding.UTF8.GetByteCount(line) > MaxMessage) throw new AgentException("size");
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true);
        await writer.WriteLineAsync(line.AsMemory(), cancel).ConfigureAwait(false);
        await writer.FlushAsync(cancel).ConfigureAwait(false);
    }

    public static AgentRequest ParseRequest(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            string op = root.TryGetProperty("op", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString() ?? "" : "";
            string token = root.TryGetProperty("token", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
            if (op.Length is < 1 or > 64) throw new AgentException("op");
            JsonElement payload = root.TryGetProperty("payload", out var p) ? p.Clone() : JsonSerializer.SerializeToElement(new { });
            return new AgentRequest(op, token, payload);
        }
        catch (AgentException) { throw; }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { throw new AgentException("format"); }
    }
}
