using System.Net.Http;

namespace SentinelX.Services.Engine;

/// <summary>The compatibility UI (--legacy) builds its own LocalAiService outside the DI container; it shares one lazily created engine
/// so that it, too, needs no Ollama. The engine starts its automatic setup the first time it is asked for.</summary>
public static class LegacyEngine
{
    private static readonly Lazy<EngineService> Instance = new(() =>
    {
        var engine = new EngineService();
        engine.Start();
        return engine;
    });

    public static HttpMessageHandler Handler() => Instance.Value.CreateHandler();
    public static string Describe() => Instance.Value.Describe();
}
