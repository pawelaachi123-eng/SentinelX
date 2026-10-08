using System.IO;
using System.IO.Pipes;

namespace SentinelX.Services.Agent;

/// <summary>Loopback named-pipe server. Every request is authorized against the
/// current boot token before dispatch; failures always answer with an error
/// envelope. One request per connection, at most 4 concurrent clients.</summary>
public sealed class AgentPipeServer(
    string pipeName,
    Func<string?> expectedToken,
    Func<AgentRequest, CancellationToken, Task<object>> handler) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(4);
    private bool disposed;

    public async Task RunAsync(CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try
            {
                await server.WaitForConnectionAsync(cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync().ConfigureAwait(false);
                break;
            }
            _ = HandleAsync(server, cancel);
        }
    }

    private async Task HandleAsync(NamedPipeServerStream server, CancellationToken cancel)
    {
        await using (server)
        {
            try
            {
                await gate.WaitAsync(cancel).ConfigureAwait(false);
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                    timeout.CancelAfter(TimeSpan.FromSeconds(30));
                    string? line = await AgentProtocol.ReadMessageAsync(server, timeout.Token).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(line)) return;
                    AgentRequest request;
                    try
                    {
                        request = AgentProtocol.ParseRequest(line);
                    }
                    catch (AgentException e)
                    {
                        await Reply(server, false, null, e.Code, timeout.Token).ConfigureAwait(false);
                        return;
                    }
                    if (!AgentAuth.ValidToken(request.Token, expectedToken()))
                    {
                        await Reply(server, false, null, "auth", timeout.Token).ConfigureAwait(false);
                        return;
                    }
                    try
                    {
                        object data = await handler(request, timeout.Token).ConfigureAwait(false);
                        await Reply(server, true, data, "", timeout.Token).ConfigureAwait(false);
                    }
                    catch (AgentException e)
                    {
                        await Reply(server, false, null, e.Code, timeout.Token).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is IOException or System.Text.Json.JsonException or InvalidOperationException or UnauthorizedAccessException)
                    {
                        await Reply(server, false, null, "failed", timeout.Token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    gate.Release();
                }
            }
            catch (AgentException e)
            {
                // Oversize envelope: the line was fully read, so an explicit error still fits.
                try { await Reply(server, false, null, e.Code, CancellationToken.None).ConfigureAwait(false); } catch { }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (Exception) { }
        }
    }

    private static Task Reply(Stream stream, bool ok, object? data, string error, CancellationToken cancel)
        => AgentProtocol.WriteMessageAsync(stream, new { ok, data, error }, cancel);

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        gate.Dispose();
        await Task.CompletedTask;
    }
}
