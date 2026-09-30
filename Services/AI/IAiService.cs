namespace SentinelX.Services.AI;
public interface IAiService
{
    Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken token = default);
    Task<string> SelectModelAsync(string model, CancellationToken token = default);
    Task<string> AskAsync(string input, string context, CancellationToken token);
    /// <summary>Live answer chunks. Optional: offline test doubles keep answering in one piece.</summary>
    Task<string> AskAsync(string input, string context, CancellationToken token, Action<string>? onDelta) => AskAsync(input, context, token);
    /// <summary>True while chunks of an answer are arriving.</summary>
    bool IsStreaming => false;
    /// <summary>Text produced before a generation was stopped — never silently discarded.</summary>
    string PartialAnswer => "";
    string RoutingReason { get; }
    bool LastResponseSucceeded => true;
    void Cancel();
}
