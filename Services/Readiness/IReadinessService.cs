using SentinelX.Models;
namespace SentinelX.Services.Readiness;
public interface IReadinessService
{
    Task<IReadOnlyList<ReadinessCheck>> CheckAsync(CancellationToken token);
}
