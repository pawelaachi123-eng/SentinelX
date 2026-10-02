package pl.sentinelx.phone;

import org.json.JSONObject;

import java.io.IOException;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.net.InterfaceAddress;
import java.net.NetworkInterface;
import java.net.SocketTimeoutException;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Locale;

/** Finds the PC on the local network by itself: a UDP broadcast "where is my PC?" that Sentinel X on the PC answers. */
final class PcLocator {
    static final int DISCOVERY_PORT = 43181;

    static final class Pc {
        final String host;
        final int port;
        final String fingerprint;
        final String name;

        Pc(String host, int port, String fingerprint, String name) {
            this.host = host;
            this.port = port;
            this.fingerprint = fingerprint;
            this.name = name;
        }
    }

    private PcLocator() { }

    /** When a fingerprint is given only the PC that owns that certificate counts (so a neighbour's PC is never picked up by accident). */
    static Pc discover(String wantedFingerprint) {
        for (Pc candidate : discoverAll(wantedFingerprint, 2500)) {
            if (probe(candidate.host, candidate.port, candidate.fingerprint, 1500)) return candidate;
        }
        return null;
    }

    static List<Pc> discoverAll(String wantedFingerprint, int listenMs) {
        List<Pc> result = new ArrayList<>();
        DatagramSocket socket = null;
        try {
            socket = new DatagramSocket();
            socket.setBroadcast(true);
            byte[] probe = "SXLINK1?".getBytes(StandardCharsets.US_ASCII);
            for (InetAddress target : broadcastTargets()) {
                try {
                    socket.send(new DatagramPacket(probe, probe.length, target, DISCOVERY_PORT));
                } catch (IOException ignored) {
                    // this network interface cannot broadcast; the others may
                }
            }
            long until = System.currentTimeMillis() + listenMs;
            byte[] buffer = new byte[2048];
            while (System.currentTimeMillis() < until && result.size() < 32) {
                socket.setSoTimeout((int) Math.max(50, until - System.currentTimeMillis()));
                DatagramPacket packet = new DatagramPacket(buffer, buffer.length);
                try {
                    socket.receive(packet);
                } catch (SocketTimeoutException e) {
                    break;
                }
                try {
                    JSONObject json = new JSONObject(new String(packet.getData(), 0, packet.getLength(), StandardCharsets.UTF_8));
                    if (!"SentinelX".equals(json.optString("app"))) continue;
                    String fp = json.optString("fingerprint", "").toLowerCase(Locale.ROOT);
                    if (!fp.matches("[0-9a-f]{64}")) continue;
                    if (wantedFingerprint != null && !wantedFingerprint.isEmpty() && !wantedFingerprint.equalsIgnoreCase(fp)) continue;
                    int port = json.optInt("port", 43180);
                    if (port < 1 || port > 65535) continue;
                    String name = json.optString("name", "").replaceAll("\\p{Cntrl}", " ").trim();
                    if (name.length() > 64) name = name.substring(0, 64);
                    result.add(new Pc(packet.getAddress().getHostAddress(), port, fp, name));
                } catch (Exception ignored) {
                    // not a Sentinel X answer
                }
            }
        } catch (Exception ignored) {
            // no usable network
        } finally {
            if (socket != null) socket.close();
        }
        return result;
    }

    static List<InetAddress> broadcastTargets() {
        List<InetAddress> targets = new ArrayList<>();
        try {
            targets.add(InetAddress.getByName("255.255.255.255"));
        } catch (Exception ignored) {
            // cannot happen for a literal address
        }
        try {
            for (NetworkInterface nic : Collections.list(NetworkInterface.getNetworkInterfaces())) {
                if (!nic.isUp() || nic.isLoopback()) continue;
                for (InterfaceAddress address : nic.getInterfaceAddresses()) {
                    InetAddress broadcast = address.getBroadcast();
                    if (broadcast != null && !targets.contains(broadcast)) targets.add(broadcast);
                }
            }
        } catch (Exception ignored) {
            // fall back to the global broadcast only
        }
        return targets;
    }

    /** True when a Sentinel X with exactly this certificate answers on this address. */
    static boolean probe(String host, int port, String fingerprint) {
        return probe(host, port, fingerprint, 3000);
    }

    static boolean probe(String host, int port, String fingerprint, int timeoutMs) {
        try {
            String body = PinnedTls.getText("https://" + host + ":" + port + "/api/hello", fingerprint, null, timeoutMs);
            return "SentinelX".equals(new JSONObject(body).optString("app"));
        } catch (Exception e) {
            return false;
        }
    }
}
