namespace SentinelX.Services.Intent;
public interface IIntentRouter
{
    Task<string> ProcessAsync(string input, CancellationToken token);
    /// <summary>Optional live chunks from the language model; deterministic tools answer in one piece.</summary>
    Task<string> ProcessAsync(string input, CancellationToken token, Action<string>? onDelta) => ProcessAsync(input, token);
}
