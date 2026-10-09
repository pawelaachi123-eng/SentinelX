namespace SentinelX.Services.Base;

/// <summary>Base + Agent-queue control surface. Implemented by the in-process
/// service and by the pipe proxy used when the headless Agent owns the queue
/// (single writer — the UI never touches queue.bin/identity.bin itself).</summary>
public interface IBaseControl : IDisposable
{
    event Action? Changed;
    string Status { get; }
    BaseIdentity? PublicConfiguration { get; }
    IReadOnlyList<string> Capabilities { get; }
    void Start();
    Task StopAsync();
    Task PairAsync(BaseIdentity next, CancellationToken cancel);
    Task UnpairAsync();
    Task<IReadOnlyList<AgentTask>> TasksAsync();
    Task<BaseMessage> RequestAsync(string type, object data, CancellationToken cancel);
    Task<string> RepairQueueAsync();
    Task<string?> QueueStorageErrorAsync(CancellationToken cancel);
}
