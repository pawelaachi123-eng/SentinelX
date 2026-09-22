using System.Text.Json.Serialization;

namespace SentinelX;

public sealed class ResourceSettings
{
    public int MonitorIntervalSeconds { get; set; } = 1;
    public int GamingMonitorIntervalSeconds { get; set; } = 3;
}

