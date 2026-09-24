using System.Text.Json.Serialization;

namespace SentinelX;

public sealed class StartupSettings
{
    public bool StartMinimized { get; set; }
    public bool StartWithWindows { get; set; }
    /// <summary>Default ON since 0.91 (explicit user decision): Sentinel listens right after launch.
    /// The indicator in the Centrum header shows the microphone state at all times and one click stops it.
    /// Existing settings files keep whatever the user already chose; new installs start listening.</summary>
    public bool StartVoiceOnLaunch { get; set; } = true;
}

