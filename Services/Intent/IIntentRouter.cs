namespace SentinelX.Services.Intent;
public interface IIntentRouter
{
    Task<string> ProcessAsync(string input, CancellationToken token);
}
