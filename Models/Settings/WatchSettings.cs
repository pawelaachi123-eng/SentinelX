using System.Text.Json.Serialization;

namespace SentinelX;

public sealed class WatchSettings
{
    /// <summary>Optional background CPU/RAM alerts. OFF by default; this setting does not capture the screen.</summary>
    public bool Enabled { get; set; } = false;
    public int CpuAlertPercent { get; set; } = 90;
    public int RamAlertPercent { get; set; } = 90;
    public int MinSecondsBeforeAlert { get; set; } = 15;
    public int CooldownMinutes { get; set; } = 5;
}
