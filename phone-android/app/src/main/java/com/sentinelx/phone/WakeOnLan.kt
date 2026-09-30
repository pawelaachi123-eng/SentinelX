package com.sentinelx.phone

import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress

object WakeOnLan {
    /** Sends a standard magic packet only to the local IPv4 broadcast address; does not claim the PC woke. */
    fun send(macInput: String): Boolean {
        val normalized = macInput.trim().replace('-', ':').uppercase()
        val parts = normalized.split(':')
        require(parts.size == 6 && parts.all { it.matches(Regex("^[0-9A-F]{2}$")) }) { "Wpisz MAC w formacie AA:BB:CC:DD:EE:FF." }
        val mac = parts.map { it.toInt(16).toByte() }.toByteArray()
        val packet = ByteArray(6 + 16 * mac.size)
        for (i in 0 until 6) packet[i] = 0xff.toByte()
        for (i in 0 until 16) System.arraycopy(mac, 0, packet, 6 + i * mac.size, mac.size)
        DatagramSocket().use { socket ->
            socket.broadcast = true
            socket.soTimeout = 2_000
            socket.send(DatagramPacket(packet, packet.size, InetAddress.getByName("255.255.255.255"), 9))
        }
        mac.fill(0)
        packet.fill(0)
        return true
    }
}
