namespace SentinelX.Services.Gaming;
public interface IGamingService
{
    bool DetectionAvailable { get; }
    bool IsGaming();
    string GetRunningGame();
}
