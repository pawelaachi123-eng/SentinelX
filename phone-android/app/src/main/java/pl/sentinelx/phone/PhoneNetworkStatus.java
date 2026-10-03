package pl.sentinelx.phone;

import android.content.Context;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkCapabilities;

import org.json.JSONArray;
import org.json.JSONObject;

import java.net.Inet4Address;
import java.net.InetAddress;
import java.net.NetworkInterface;
import java.util.Collections;
import java.util.Enumeration;
import java.util.TreeSet;

/** Privacy-limited network facts for the controller screen; does not scan peers, read Wi-Fi SSIDs or expose interface MACs. */
final class PhoneNetworkStatus {
    private PhoneNetworkStatus() { }

    static String toJson(Context context) {
        JSONObject result = new JSONObject();
        try {
            ConnectivityManager manager = (ConnectivityManager) context.getSystemService(Context.CONNECTIVITY_SERVICE);
            Network active = manager == null ? null : manager.getActiveNetwork();
            NetworkCapabilities capabilities = manager == null || active == null ? null : manager.getNetworkCapabilities(active);
            String type = "offline";
            boolean connected = capabilities != null;
            if (capabilities != null) {
                if (capabilities.hasTransport(NetworkCapabilities.TRANSPORT_VPN)) type = "vpn";
                else if (capabilities.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)) type = "wifi";
                else if (capabilities.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET)) type = "ethernet";
                else if (capabilities.hasTransport(NetworkCapabilities.TRANSPORT_CELLULAR)) type = "cellular";
                else if (capabilities.hasTransport(NetworkCapabilities.TRANSPORT_BLUETOOTH)) type = "bluetooth";
                else type = "other";
            }
            result.put("type", type);
            result.put("connected", connected);
            result.put("internet", capabilities != null
                    && capabilities.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
                    && capabilities.hasCapability(NetworkCapabilities.NET_CAPABILITY_VALIDATED));

            TreeSet<String> addresses = new TreeSet<>();
            Enumeration<NetworkInterface> interfaces = NetworkInterface.getNetworkInterfaces();
            if (interfaces != null) {
                for (NetworkInterface nic : Collections.list(interfaces)) {
                    try {
                        if (!nic.isUp() || nic.isLoopback()) continue;
                        Enumeration<InetAddress> entries = nic.getInetAddresses();
                        for (InetAddress address : Collections.list(entries)) {
                            if (address.isAnyLocalAddress() || address.isLoopbackAddress() || address.isMulticastAddress()) continue;
                            if (isControllerLocalAddress(address)) addresses.add(address.getHostAddress());
                        }
                    } catch (Exception ignored) {
                        // One interface may disappear while Android changes between Wi-Fi, Ethernet or VPN.
                    }
                }
            }
            JSONArray values = new JSONArray();
            for (String address : addresses) values.put(address);
            result.put("addresses", values);
        } catch (Exception ignored) {
            try { result.put("type", "unknown"); result.put("connected", false); result.put("internet", false); result.put("addresses", new JSONArray()); }
            catch (Exception alsoIgnored) { }
        }
        return result.toString();
    }

    private static boolean isControllerLocalAddress(InetAddress address) {
        if (address.isSiteLocalAddress() || address.isLinkLocalAddress()) return true;
        if (!(address instanceof Inet4Address)) return false;
        byte[] b = address.getAddress();
        // Tailscale and other private overlays commonly allocate from the CGNAT range 100.64.0.0/10.
        return (b[0] & 0xff) == 100 && (b[1] & 0xc0) == 0x40;
    }
}
