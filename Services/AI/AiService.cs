namespace SentinelX.Services.AI;

/// <summary>Model discovery and selection on top of the existing tested adaptive inference/fallback pipeline (LocalAiService).
/// Since 0.94 the models come from the built-in engine — no Ollama, no external client library.</summary>
public sealed class AiService(LocalAiService local) : IAiService
{
    public async Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken token = default) =>
        await local.GetInstalledModelsAsync(token).ConfigureAwait(false);
    public Task<string> SelectModelAsync(string model, CancellationToken token = default) => local.SetPreferredModelAsync(model, token);
    public Task<string> AskAsync(string input, string context, CancellationToken token) => local.AskAsync(input, context, token);
    public Task<string> AskAsync(string input, string context, CancellationToken token, Action<string>? onDelta) => local.AskAsync(input, context, token, onDelta);
    public bool IsStreaming => local.IsStreaming;
    public string PartialAnswer => local.LastPartialAnswer;
    public string RoutingReason => local.LastRoutingReason;
    public void Cancel() => local.CancelCurrentRequest();
}
