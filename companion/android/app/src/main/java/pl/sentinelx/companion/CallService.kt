package pl.sentinelx.companion

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Context
import android.content.Intent
import android.media.AudioManager
import android.os.IBinder
import org.json.JSONObject

/**
 * Foreground service trzymająca most przy życiu podczas rozmowy:
 * most TCP ↔ SentinelX, prawdziwe dzwonienie z SIM (CallController), relacja głośnika (AudioGateway).
 * Polecenia od Sentinela: dial (z numerem), end_call. Do PC idą: call_state (prawdziwe stany systemu)
 * i audio up z mikrofonu.
 */
class CallService : Service() {

    companion object {
        const val CHANNEL = "sentinel_bridge"
        @Volatile var statusListener: ((String) -> Unit)? = null
    }

    private var bridge: BridgeClient? = null
    private var audio: AudioGateway? = null
    private var calls: CallController? = null

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        startForeground(1, buildNotification("Most działa — Sentinel może dzwonić z Twojej karty SIM."))
        if (bridge == null) startBridge()
        return START_STICKY
    }

    private fun startBridge() {
        val prefs = getSharedPreferences("companion", Context.MODE_PRIVATE)
        val host = prefs.getString("host", "") ?: ""
        val port = prefs.getString("port", "7877")?.toIntOrNull() ?: 7877
        val token = prefs.getString("token", "") ?: ""
        if (host.isEmpty() || token.isEmpty()) {
            report("Najpierw wpisz adres, port i token w aplikacji.")
            return
        }
        calls = CallController(this) { state, seconds ->
            bridge?.sendJson(JSONObject().put("type", "call_state").put("state", state).put("duration", seconds))
        }
        calls?.startMonitoring()
        audio = AudioGateway(getSystemService(Context.AUDIO_SERVICE) as AudioManager) { chunk ->
            bridge?.sendAudioUp(chunk)
        }
        bridge = BridgeClient(host, port, token, Build.MODEL,
            onJson = ::onJson,
            onTtsAudio = { pcm -> audio?.playDown(pcm) },
            onState = ::report)
        bridge?.connect()
    }

    private fun onJson(json: JSONObject) {
        when (json.optString("type")) {
            "dial" -> {
                val number = json.optString("number")
                val dialed = calls?.dial(number) ?: false
                if (!dialed) {
                    report("Brak uprawnienia CALL_PHONE albo numer niepoprawny (" + number + ").")
                    bridge?.sendJson(JSONObject().put("type", "call_state").put("state", "FAILED").put("duration", 0))
                } else {
                    audio?.start()
                    report("Dzwonię prawdziwym połączeniem SIM: " + number)
                }
            }
            "end_call" -> {
                calls?.endCall()
                audio?.stop()
                report("Połączenie zakończone.")
            }
        }
    }

    private fun report(text: String) {
        statusListener?.invoke(text)
    }

    private fun buildNotification(text: String): Notification {
        val manager = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        if (Build.VERSION.SDK_INT >= 26) {
            manager.createNotificationChannel(
                NotificationChannel(CHANNEL, "Sentinel most", NotificationManager.IMPORTANCE_LOW)
            )
        }
        val builder = if (Build.VERSION.SDK_INT >= 26) Notification.Builder(this, CHANNEL)
        else Notification.Builder(this)
        return builder.setContentTitle("Sentinel Companion").setContentText(text)
            .setSmallIcon(android.R.drawable.stat_sys_phone_call).build()
    }

    override fun onDestroy() {
        bridge?.close()
        audio?.stop()
        calls?.stopMonitoring()
        bridge = null; audio = null; calls = null
        super.onDestroy()
    }
}
