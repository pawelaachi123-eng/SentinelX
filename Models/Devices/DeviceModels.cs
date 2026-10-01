namespace SentinelX.Models.Devices;

public enum DeviceType
{
    Pc = 0,
    Phone = 1,
    Tv = 2,
    SmartLight = 3,
    SmartPlug = 4,
    MediaDevice = 5,
    Speaker = 6,
    Other = 99
}

[Flags]
public enum DeviceCapabilities
{
    None = 0,
    PowerOn = 1 << 0,
    PowerOff = 1 << 1,
    Reboot = 1 << 2,
    Volume = 1 << 3,
    Mute = 1 << 4,
    Input = 1 << 5,
    LaunchApp = 1 << 6,
    StatusQuery = 1 << 7,
    MediaPlayPause = 1 << 8,
    Brightness = 1 << 9,
    Color = 1 << 10,
    PowerConsumption = 1 << 11
}

public enum DeviceConnection
{
    Local,
    Cloud,
    Disconnected,
    Unknown
}

public enum DeviceStatus
{
    Online,
    Offline,
    Standby,
    Busy,
    Error,
    Unknown
}

public sealed record DevicePermission(bool Allowed, string Scope, DateTimeOffset GrantedAt);

public sealed class DeviceInfo
{
    public string DeviceId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DeviceType Type { get; set; }
    public DeviceCapabilities Capabilities { get; set; }
    public DeviceConnection Connection { get; set; } = DeviceConnection.Unknown;
    public DeviceStatus Status { get; set; } = DeviceStatus.Unknown;
    public string ProviderId { get; set; } = ""; // który integration/remote obsługuje
    public Dictionary<string, string> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<DevicePermission> Permissions { get; set; } = new();
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;

    public bool Supports(DeviceCapabilities cap) => (Capabilities & cap) == cap;
}
