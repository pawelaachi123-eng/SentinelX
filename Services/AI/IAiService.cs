namespace SentinelX.Services.AI;
public interface IAiService
{
    Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken token = default);
    Task<string> SelectModelAsync(string model, CancellationToken token = default);
    Task<string> AskAsync(string input, string context, CancellationToken token);
    string RoutingReason { get; }
    void Cancel();
}
