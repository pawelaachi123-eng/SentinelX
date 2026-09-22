using System.Text.Json.Serialization;

namespace SentinelX;

public sealed class WatchSettings
{
    public bool Enabled { get; set; }
    public int CpuAlertPercent { get; set; } = 90;
    public int RamAlertPercent { get; set; } = 90;
    public int MinSecondsBeforeAlert { get; set; } = 15;
    public int CooldownMinutes { get; set; } = 5;
}

