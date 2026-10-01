using System.Text.Json.Serialization;

namespace SentinelX;

public sealed class StartupSettings
{
    public bool StartMinimized { get; set; }
    /// <summary>ON by default since 0.94 ("autopilot"): Sentinel starts quietly with Windows so the phone can always reach it.
    /// Turn it off in Settings → Ogólne.</summary>
    public bool StartWithWindows { get; set; } = true;
    /// <summary>Default ON since 0.91 (explicit user decision): Sentinel listens right after launch.
    /// The indicator in the Centrum header shows the microphone state at all times and one click stops it.
    /// Existing settings files keep whatever the user already chose; new installs start listening.</summary>
    public bool StartVoiceOnLaunch { get; set; } = true;
    /// <summary>Set once, the first time a build with the autopilot runs on an existing settings file (see CareService).</summary>
    public bool AutopilotApplied { get; set; }
}

