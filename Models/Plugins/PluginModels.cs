namespace SentinelX.Models.Plugins;

public sealed record PluginVersion(int Major, int Minor, int Patch)
{
    public static bool TryParse(string s, out PluginVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var parts = s.Trim().Split('.');
        if (parts.Length is < 2 or > 3) return false;
        if (!int.TryParse(parts[0], out int maj) || !int.TryParse(parts[1], out int min)) return false;
        int pat = parts.Length == 3 && int.TryParse(parts[2], out int p) ? p : 0;
        if (maj < 0 || min < 0 || pat < 0) return false;
        version = new PluginVersion(maj, min, pat);
        return true;
    }
    public bool Satisfies(PluginVersion min) =>
        Major > min.Major || (Major == min.Major && (Minor > min.Minor || (Minor == min.Minor && Patch >= min.Patch)));
    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

public sealed record PluginDependency(string PluginId, string MinVersion, bool Optional = false);

[Flags]
public enum PluginPermissions
{
    None = 0,
    ReadFiles = 1 << 0,
    WriteFiles = 1 << 1,
    ExecuteProcess = 1 << 2,
    Network = 1 << 3,
    AccessMemory = 1 << 4,
    AccessVoice = 1 << 5,
    RegisterTools = 1 << 6,
    RegisterAutomations = 1 << 7,
    AccessDevices = 1 << 8,
    AccessIntegrations = 1 << 9,
    /// <summary>Niebezpieczne: pozwala wstrzykiwać kod do procesu. Tylko dla pluginów podpisanych/zaufanych.</summary>
    Unsafe = 1 << 30
}

public enum PluginLifecycleState
{
    Discovered,
    Loaded,
    Started,
    Stopped,
    Faulted,
    Disabled
}

public enum PluginHealthLevel
{
    Healthy,
    Degraded,
    Unhealthy,
    Unknown
}

public sealed class PluginManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Version { get; set; } = "0.0.1";
    public string Description { get; set; } = "";
    public string EntryAssembly { get; set; } = "";
    public string MinimumSentinelVersion { get; set; } = "0.94.0";
    public List<PluginDependency> Dependencies { get; set; } = new();
    public PluginPermissions RequestedPermissions { get; set; } = PluginPermissions.None;
    public bool Signed { get; set; }
    public string IconPath { get; set; } = "";
}
