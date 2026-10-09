using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace SentinelX.Services.Agent;

/// <summary>UI-side pipe client. The token is resolved per request so the UI
/// picks up a freshly started Agent without a restart.</summary>
public sealed class AgentClient(string pipeName, Func<string?> token)
{
    public static bool IsAgentRunning(string root)
    {
        try
        {
            using var mutex = Mutex.OpenExisting(AgentAuth.MutexName(root));
            return true;
        }
        catch (WaitHandleCannotBeOpenedException) { return false; }
        catch (UnauthorizedAccessException) { return true; }
        catch { return false; }
    }

    public async Task<JsonElement> SendAsync(string op, object payload, CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(35));
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await client.ConnectAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or TimeoutException)
        {
            throw new AgentException("offline");
        }
        await AgentProtocol.WriteMessageAsync(client, new { op, token = token() ?? "", payload }, timeout.Token).ConfigureAwait(false);
        string? line = await AgentProtocol.ReadMessageAsync(client, timeout.Token).ConfigureAwait(false);
        if (string.IsNullOrEmpty(line)) throw new AgentException("protocol");
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            bool ok = root.TryGetProperty("ok", out var o) && o.ValueKind == JsonValueKind.True;
            if (!ok)
            {
                string error = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String
                    ? e.GetString() ?? "failed" : "failed";
                throw new AgentException(error);
            }
            return root.TryGetProperty("data", out var d) ? d.Clone() : JsonSerializer.SerializeToElement(new { });
        }
        catch (AgentException) { throw; }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { throw new AgentException("protocol"); }
    }
}
