using SentinelX.Models;
namespace SentinelX.Services.Monitoring;
public interface ISystemMonitorService
{
    SystemSnapshot Current { get; }
    event Action<SystemSnapshot>? Updated;
    void Start();
}
