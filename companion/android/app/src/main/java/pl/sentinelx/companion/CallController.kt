package pl.sentinelx.companion

import android.Manifest
import android.annotation.SuppressLint
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.telephony.PhoneStateListener
import android.telephony.TelephonyManager
import org.json.JSONObject

/**
 * Realne dzwonienie z karty SIM: ACTION_CALL (uprawnienie CALL_PHONE) — rozmówca widzi TWÓJ numer.
 * Stan połączenia raportowany wprost z TelephonyManager — to z niego SentinelX bierze dowód „OFFHOOK”.
 */
class CallController(private val context: Context, private val reportState: (String, Int) -> Unit) {
    private var telephony: TelephonyManager? = null
    private var listener: PhoneStateListener? = null
    private var callStartUtcMs = 0L

    @SuppressLint("MissingPermission")
    fun startMonitoring() {
        if (context.checkSelfPermission(Manifest.permission.READ_PHONE_STATE)
            != PackageManager.PERMISSION_GRANTED) return
        val tm = context.getSystemService(Context.TELEPHONY_SERVICE) as TelephonyManager
        telephony = tm
        val stateListener = object : PhoneStateListener() {
            override fun onCallStateChanged(state: Int, phoneNumber: String?) {
                val name = when (state) {
                    TelephonyManager.CALL_STATE_RINGING -> "DIALING"
                    TelephonyManager.CALL_STATE_OFFHOOK -> {
                        if (callStartUtcMs == 0L) callStartUtcMs = System.currentTimeMillis()
                        "OFFHOOK"
                    }
                    else -> {
                        callStartUtcMs = 0L
                        "IDLE"
                    }
                }
                val seconds = if (callStartUtcMs == 0L) 0
                    else ((System.currentTimeMillis() - callStartUtcMs) / 1000).toInt()
                reportState(name, seconds)
            }
        }
        listener = stateListener
        try { tm.listen(stateListener, PhoneStateListener.LISTEN_CALL_STATE) } catch (_: Exception) {}
    }

    /** Wybiera numer PRAWDZIWYM połączeniem z karty SIM. */
    fun dial(number: String): Boolean {
        if (context.checkSelfPermission(Manifest.permission.CALL_PHONE)
            != PackageManager.PERMISSION_GRANTED) return false
        return try {
            context.startActivity(
                Intent(Intent.ACTION_CALL, Uri.parse("tel:" + number))
                    .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            )
            true
        } catch (_: Exception) { false }
    }

    /** Kończy połączenie oficjalnym API systemowym (od Androida 9 dostępne dla domyślnej aplikacji
     * telefonicznej; tu best-effort — użytkownik może położyć słuchawkę ręcznie, dowód OFFHOOK
     * i tak został zapisany). */
    fun endCall() {
        try {
            val tm = telephony ?: return
            val end = tm.javaClass.getMethod("endCall")
            end.isAccessible = true
            end.invoke(tm)
        } catch (_: Exception) {}
    }

    fun stopMonitoring() {
        try { telephony?.listen(listener, PhoneStateListener.LISTEN_NONE) } catch (_: Exception) {}
        listener = null
    }

    companion object {
        fun parseStateFromCommand(json: JSONObject): String? =
            when (json.optString("type")) {
                "dial" -> json.optString("number")
                else -> null
            }
    }
}
