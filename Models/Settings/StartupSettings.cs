using System.Text.Json.Serialization;

namespace SentinelX;

public sealed class StartupSettings
{
    public bool StartMinimized { get; set; }
    public bool StartWithWindows { get; set; }
    public bool StartVoiceOnLaunch { get; set; }
}

