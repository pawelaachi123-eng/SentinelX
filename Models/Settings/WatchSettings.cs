using System.Text.Json.Serialization;

namespace SentinelX;

public sealed class WatchSettings
{
    /// <summary>ON by default since 0.94 ("autopilot"): long high CPU/RAM load raises a notification on the PC and an alert on the phone.</summary>
    public bool Enabled { get; set; } = true;
    public int CpuAlertPercent { get; set; } = 90;
    public int RamAlertPercent { get; set; } = 90;
    public int MinSecondsBeforeAlert { get; set; } = 15;
    public int CooldownMinutes { get; set; } = 5;
}

