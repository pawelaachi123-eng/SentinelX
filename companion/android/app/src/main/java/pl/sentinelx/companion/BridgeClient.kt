package pl.sentinelx.companion

import java.io.DataInputStream
import java.io.DataOutputStream
import java.net.InetSocketAddress
import java.net.Socket
import java.nio.ByteBuffer
import java.nio.ByteOrder
import org.json.JSONObject

/**
 * Klient mostu TCP (ten sam protokół co PhoneBridgeService w SentinelX):
 * [1 bajt rodzaju][4 bajty długości LE][dane] — 1: JSON, 2: audio od rozmówcy (do PC),
 * 3: mowa TTS z PC. Pierwsza wiadomość to „hello” z tokenem — inaczej PC zamyka gniazdo.
 */
class BridgeClient(
    private val host: String,
    private val port: Int,
    private val token: String,
    private val deviceName: String,
    private val onJson: (JSONObject) -> Unit,
    private val onTtsAudio: (ShortArray) -> Unit,
    private val onState: (String) -> Unit,
) {
    private var socket: Socket? = null
    private var writer: DataOutputStream? = null
    private val writeLock = Any()
    @Volatile var isConnected = false
        private set

    fun connect() {
        Thread {
            try {
                val s = Socket()
                s.connect(InetSocketAddress(host, port), 8000)
                s.tcpNoDelay = true
                socket = s
                val out = DataOutputStream(s.getOutputStream().buffered())
                val input = DataInputStream(s.getInputStream())
                synchronized(writeLock) { writer = out }
                sendJson(JSONObject().put("type", "hello").put("token", token).put("device", deviceName))
                isConnected = true
                onState("Sparowano z SentinelX.")
                val lenBuf = ByteArray(4)
                while (isOpen) {
                    val kind = input.readByte().toInt()
                    input.readFully(lenBuf)
                    val len = ByteBuffer.wrap(lenBuf).order(ByteOrder.LITTLE_ENDIAN).int
                    if (len < 0 || len > 4_000_000) break
                    val payload = ByteArray(len)
                    input.readFully(payload)
                    when (kind) {
                        1 -> onJson(JSONObject(String(payload, Charsets.UTF_8)))
                        3 -> {
                            val pcm = ShortArray(len / 2)
                            ByteBuffer.wrap(payload).order(ByteOrder.LITTLE_ENDIAN).asShortBuffer().get(pcm)
                            onTtsAudio(pcm)
                        }
                    }
                }
            } catch (e: Exception) {
                onState("Rozłączono: ${e.message ?: e.javaClass.simpleName}")
            } finally {
                isConnected = false
                close()
            }
        }.start()
    }

    fun sendJson(json: JSONObject) {
        send(1, json.toString().toByteArray(Charsets.UTF_8))
    }

    /** Wysyła kawałek audio od rozmówcy (int16 LE, 16 kHz mono). */
    fun sendAudioUp(pcm: ShortArray) {
        val bytes = ByteArray(pcm.size * 2)
        ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN).asShortBuffer().put(pcm)
        send(2, bytes)
    }

    private fun send(kind: Int, payload: ByteArray) {
        synchronized(writeLock) {
            val w = writer ?: return
            try {
                w.writeByte(kind)
                val len = ByteArray(4)
                ByteBuffer.wrap(len).order(ByteOrder.LITTLE_ENDIAN).putInt(payload.size)
                w.write(len)
                w.write(payload)
                w.flush()
            } catch (e: Exception) {
                onState("Nie udało się wysłać: ${e.message ?: e.javaClass.simpleName}")
            }
        }
    }

    private fun isOpen: Boolean get() = socket?.isClosed == false && isConnected

    fun close() {
        try { socket?.close() } catch (_: Exception) {}
        synchronized(writeLock) { writer = null }
        isConnected = false
    }
}
