using System.Net.Http;
using OllamaSharp;
namespace SentinelX.Services.AI;

/// <summary>OllamaSharp discovery and the existing tested adaptive inference/fallback pipeline.</summary>
public sealed class AiService : IAiService, IDisposable
{
    private readonly LocalAiService local;
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
    { BaseAddress = new Uri("http://127.0.0.1:11434"), Timeout = TimeSpan.FromSeconds(15) };
    private readonly OllamaApiClient client;
    public AiService(LocalAiService local) { this.local = local; client = new OllamaApiClient(http); }
    public async Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken token = default) =>
        (await client.ListLocalModelsAsync(token)).Select(x => x.Name).Where(LocalAiService.IsLocalModelName).OrderBy(x => x).ToArray();
    public Task<string> SelectModelAsync(string model, CancellationToken token = default) => local.SetPreferredModelAsync(model, token);
    public Task<string> AskAsync(string input, string context, CancellationToken token) => local.AskAsync(input, context, token);
    public string RoutingReason => local.LastRoutingReason;
    public void Cancel() => local.CancelCurrentRequest();
    public void Dispose() { ((IDisposable)client).Dispose(); http.Dispose(); }
}
