using System.Linq;
using System.Collections.Concurrent;
using SentinelX.Models.Devices;

namespace SentinelX.Services.Devices;

/// <summary>
/// Abstrakcyjny silnik urządzeń. Żaden producent nie jest hardkodowany.
/// Integracje/Pluginy rejestrują urządzenia i ich możliwości; wykonanie akcji
/// odbywa się przez providerów (np. lokalny HTTP API TV, protokół Philips Hue itp.).
/// </summary>
public sealed class DeviceEngine
{
    private readonly ConcurrentDictionary<string, DeviceInfo> devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IDeviceProvider> providers = new(StringComparer.OrdinalIgnoreCase);

    public event Action<DeviceInfo>? DeviceChanged;

    public void RegisterProvider(IDeviceProvider provider) => providers[provider.ProviderId] = provider;

    public void Upsert(DeviceInfo device)
    {
        devices[device.DeviceId] = device;
        DeviceChanged?.Invoke(device);
    }

    public bool Remove(string deviceId) => devices.TryRemove(deviceId, out _);

    public DeviceInfo? Get(string deviceId) => devices.TryGetValue(deviceId, out var d) ? d : null;
    public IReadOnlyList<DeviceInfo> All() => devices.Values.ToList();
    public IReadOnlyList<DeviceInfo> ByType(DeviceType type) => devices.Values.Where(d => d.Type == type).ToList();

    public async Task<DeviceActionResult> InvokeAsync(string deviceId, DeviceAction action, CancellationToken token)
    {
        if (!devices.TryGetValue(deviceId, out var dev))
            return DeviceActionResult.Fail("Urządzenie nieznane.");
        if (!dev.Supports(action.RequiredCapability))
            return DeviceActionResult.Fail($"Urządzenie nie obsługuje: {action.RequiredCapability}");
        if (!dev.Permissions.Any(p => p.Allowed && p.Scope == action.Scope))
            return DeviceActionResult.Fail("Brak uprawnienia do tej akcji.");
        if (!providers.TryGetValue(dev.ProviderId, out var prov))
            return DeviceActionResult.Fail($"Brak dostawcy dla: {dev.ProviderId}");
        try
        {
            return await prov.ExecuteAsync(dev, action, token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return DeviceActionResult.Fail(ex.Message);
        }
    }
}

public interface IDeviceProvider
{
    string ProviderId { get; }
    Task<DeviceActionResult> ExecuteAsync(DeviceInfo device, DeviceAction action, CancellationToken token);
}

public sealed record DeviceAction(string Name, DeviceCapabilities RequiredCapability, string Scope, object? Payload = null)
{
    // Canonical action names
    public const string PowerOnName = "power.on";
    public const string PowerOffName = "power.off";
    public const string SetVolumeName = "volume.set";
    public const string MuteName = "volume.mute";
    public const string SetInputName = "input.set";
    public const string LaunchAppName = "app.launch";
    public const string StatusName = "status.query";
}

public sealed record DeviceActionResult(bool Success, string? Message, object? Data = null)
{
    public static DeviceActionResult Ok(string? msg = null, object? data = null) => new(true, msg, data);
    public static DeviceActionResult Fail(string msg) => new(false, msg);
}
