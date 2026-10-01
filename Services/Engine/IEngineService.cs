using System.Net.Http;

namespace SentinelX.Services.Engine;

public sealed record EngineEndpoint(Uri BaseAddress, string ApiKey);

public sealed record EngineModelInfo(string Name, long Size, DateTimeOffset Modified, string Digest, string Family, string ParameterSize, string Quantization);

/// <summary>Raised with a message meant for the user (for example "the engine is still installing, 37%").</summary>
public sealed class EngineUnavailableException(string message) : Exception(message);

/// <summary>What the Ollama-compatible facade needs from the engine.</summary>
public interface IEngineRuntime
{
    IReadOnlyList<EngineModelInfo> InstalledModels();
    /// <summary>Starts the model (or reuses the running one). Throws <see cref="EngineUnavailableException"/> when that is not possible yet.</summary>
    Task<EngineEndpoint> AcquireAsync(string model, int contextSize, CancellationToken token);
    /// <summary>The request is finished: keep the model in memory for <paramref name="keepAlive"/> (zero = free the RAM right now).</summary>
    void Release(string model, TimeSpan keepAlive);
    void Unload();
}

/// <summary>"idle" (not started), "installing", "paused" (a game is running), "ready", "error".</summary>
public sealed record EngineStatus(string State, string Message, double Progress, string Model, IReadOnlyList<string> Installed);

public interface IEngineService
{
    EngineStatus Status { get; }
    event Action? Changed;
    /// <summary>A handler that answers the small subset of the Ollama HTTP API the app speaks, using the built-in engine.
    /// No network port is opened: LocalAiService calls it in-process.</summary>
    HttpMessageHandler CreateHandler();
    /// <summary>Begins the automatic setup (runtime + models) in the background. Safe to call more than once.</summary>
    void Start();
    /// <summary>Re-checks every file and finishes anything missing. Returns a sentence for the user.</summary>
    Task<string> RepairAsync(CancellationToken token);
}
