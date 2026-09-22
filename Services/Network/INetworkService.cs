namespace SentinelX.Services.Network;
public interface INetworkService
{
    Task<ActionExecutionResult> TestInternetAsync(CancellationToken cancellationToken);
    Task<ActionExecutionResult> TestPingAsync(string host, CancellationToken cancellationToken = default);
    Task<ActionExecutionResult> TestDnsAsync(string host, CancellationToken cancellationToken = default);
    string GetNetworkSummary();
}
