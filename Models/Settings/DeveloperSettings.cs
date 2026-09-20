using System.Text.Json.Serialization;

namespace SentinelX;

public sealed class DeveloperSettings
{
    public bool DeveloperMode { get; set; }
    public bool SaveVoiceSamples { get; set; }
}

