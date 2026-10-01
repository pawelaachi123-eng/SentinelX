using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SentinelX.Services.Link;

public sealed record LanAddress(IPAddress Address, string InterfaceName, bool HasGateway, string Mac);

/// <summary>Network helpers: who may talk to the link, and which addresses to show to the user.</summary>
public static class LinkNetwork
{
    /// <summary>Loopback, RFC1918, link-local and the CGNAT range used by Tailscale (100.64.0.0/10) — never the public internet.</summary>
    public static bool IsTrusted(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily == AddressFamily.InterNetworkV6) return address.IsIPv6LinkLocal || address.IsIPv6UniqueLocal;
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] b = address.GetAddressBytes();
        return b[0] == 10
            || b[0] == 172 && b[1] is >= 16 and <= 31
            || b[0] == 192 && b[1] == 168
            || b[0] == 169 && b[1] == 254
            || b[0] == 100 && b[1] is >= 64 and <= 127;
    }

    private static readonly object CacheGate = new();
    private static IReadOnlyList<LanAddress> cached = [];
    private static DateTime cachedAt = DateTime.MinValue;

    /// <summary>Usable IPv4 addresses of real network adapters, best first (the adapter with a default gateway is the home network).
    /// Cached for a few seconds: the phone polls the PC state every few seconds and the adapter scan is not free.</summary>
    public static IReadOnlyList<LanAddress> GetLanAddresses()
    {
        lock (CacheGate)
        {
            if (DateTime.UtcNow - cachedAt < TimeSpan.FromSeconds(20)) return cached;
            cached = ScanAddresses();
            cachedAt = DateTime.UtcNow;
            return cached;
        }
    }

    private static IReadOnlyList<LanAddress> ScanAddresses()
    {
        var result = new List<LanAddress>();
        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                string label = (nic.Name + " " + nic.Description).ToLowerInvariant();
                if (label.Contains("vethernet") || label.Contains("vmware") || label.Contains("virtualbox") || label.Contains("hyper-v")
                    || label.Contains("wsl") || label.Contains("docker") || label.Contains("bluetooth") || label.Contains("loopback")) continue;
                IPInterfaceProperties properties = nic.GetIPProperties();
                bool gateway = properties.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                string mac = string.Join(":", nic.GetPhysicalAddress().GetAddressBytes().Select(x => x.ToString("X2")));
                foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
                {
                    IPAddress ip = unicast.Address;
                    if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip) || !IsTrusted(ip)) continue;
                    if (ip.GetAddressBytes()[0] == 169) continue; // APIPA: no DHCP answer, so nobody can reach it
                    result.Add(new LanAddress(ip, nic.Name, gateway, mac));
                }
            }
        }
        catch (NetworkInformationException) { }
        return result.OrderByDescending(x => x.HasGateway).ThenBy(x => x.Address.ToString(), StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<string> BuildUrls(int port) =>
        GetLanAddresses().Select(x => "https://" + x.Address + ":" + port + "/").Distinct().ToArray();

    /// <summary>MAC addresses of adapters that reach the LAN — the phone needs one to wake this PC (Wake-on-LAN).</summary>
    public static IReadOnlyList<string> GetWakeMacs() =>
        GetLanAddresses().Where(x => x.Mac.Length >= 17).Select(x => x.Mac).Distinct().ToArray();
}
