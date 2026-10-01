package pl.sentinelx.phone;

import java.io.IOException;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.util.Locale;

/** Wake-on-LAN "magic packet": switches a sleeping or shut-down PC on, if its network card and BIOS allow it. */
final class WakeOnLan {
    private WakeOnLan() { }

    static byte[] parse(String mac) {
        String[] parts = mac.trim().toLowerCase(Locale.ROOT).split("[:\\-]");
        if (parts.length != 6) throw new IllegalArgumentException("MAC address needs six parts");
        byte[] bytes = new byte[6];
        for (int i = 0; i < 6; i++) bytes[i] = (byte) Integer.parseInt(parts[i], 16);
        return bytes;
    }

    static void send(String mac) throws IOException {
        byte[] hardware = parse(mac);
        byte[] packet = new byte[6 + 16 * 6];
        for (int i = 0; i < 6; i++) packet[i] = (byte) 0xff;
        for (int i = 0; i < 16; i++) System.arraycopy(hardware, 0, packet, 6 + i * 6, 6);
        try (DatagramSocket socket = new DatagramSocket()) {
            socket.setBroadcast(true);
            for (InetAddress target : PcLocator.broadcastTargets()) {
                try {
                    socket.send(new DatagramPacket(packet, packet.length, target, 9));
                } catch (IOException ignored) {
                    // try the other networks
                }
            }
        }
    }
}
