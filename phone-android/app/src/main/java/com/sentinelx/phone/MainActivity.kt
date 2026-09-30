package com.sentinelx.phone

import android.Manifest
import android.app.Activity
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.hardware.ConsumerIrManager
import android.os.Bundle
import android.speech.RecognizerIntent
import android.view.View
import android.widget.Button
import android.widget.EditText
import android.widget.TextView
import java.text.Normalizer
import java.util.Locale
import java.util.concurrent.Executors
import org.json.JSONObject

class MainActivity : Activity() {
    private lateinit var secureStore: SecurePairStore
    private val client = BridgeClient()
    private val io = Executors.newSingleThreadExecutor()
    private lateinit var host: EditText
    private lateinit var port: EditText
    private lateinit var fingerprint: EditText
    private lateinit var pairingCode: EditText
    private lateinit var deviceName: EditText
    private lateinit var wolMac: EditText
    private lateinit var command: EditText
    private lateinit var status: TextView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)
        secureStore = SecurePairStore(this)
        host = findViewById(R.id.host)
        port = findViewById(R.id.port)
        fingerprint = findViewById(R.id.fingerprint)
        pairingCode = findViewById(R.id.pairing_code)
        deviceName = findViewById(R.id.device_name)
        wolMac = findViewById(R.id.wol_mac)
        command = findViewById(R.id.command)
        status = findViewById(R.id.status)
        secureStore.load()?.let {
            host.setText(it.host); port.setText(it.port.toString()); fingerprint.setText(it.fingerprint)
            pairingCode.visibility = View.GONE
        }
        wolMac.setText(secureStore.loadWakeMac())
        findViewById<Button>(R.id.pair).setOnClickListener { pair() }
        findViewById<Button>(R.id.forget_pairing).setOnClickListener {
            secureStore.clear()
            pairingCode.visibility = View.VISIBLE
            status.text = "Klucz usunięty z telefonu. Cofnij ten telefon również w ustawieniach Bridge na PC, aby unieważnić jego autoryzację."
        }
        findViewById<Button>(R.id.send).setOnClickListener { sendNaturalCommand(command.text.toString()) }
        findViewById<Button>(R.id.voice).setOnClickListener { startVoice() }
        findViewById<Button>(R.id.pc_on).setOnClickListener { wakePc() }
        findViewById<Button>(R.id.pc_status).setOnClickListener { sendIntent("PC_STATUS") }
        findViewById<Button>(R.id.tv_on).setOnClickListener { sendIntent("TV_POWER_ON", "tv") }
        findViewById<Button>(R.id.tv_mute).setOnClickListener { sendIntent("TV_MUTE", "tv") }
        findViewById<Button>(R.id.game).setOnClickListener { sendIntent("PC_LAUNCH_APP", "CS2") }
        status.text = "${bridgeLabel()}\n${irCapabilityLabel()}"
        handleWidgetAction(intent)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        handleWidgetAction(intent)
    }

    private fun pair() {
        val h = host.text.toString().trim()
        val p = port.text.toString().toIntOrNull() ?: 43179
        val pin = fingerprint.text.toString().trim()
        val code = pairingCode.text.toString()
        val name = deviceName.text.toString()
        setBusy("Sprawdzam odcisk TLS i jednorazowy kod parowania…")
        io.execute {
            try {
                val response = client.pair(h, p, pin, code, name)
                val resultStatus = response.optString("status")
                if (resultStatus != "PAIRED") throw IllegalStateException(response.optString("message", "Parowanie nie powiodło się."))
                val token = response.getString("token")
                val deviceId = response.getString("deviceId")
                secureStore.save(h, p, pin, deviceId, token)
                runOnUiThread {
                    pairingCode.text.clear(); pairingCode.visibility = View.GONE
                    showResult("CONNECTED", response.optString("message", "Telefon sparowano."))
                }
            } catch (ex: Exception) {
                runOnUiThread { showResult("FAILED", ex.message ?: "Parowanie nie powiodło się.") }
            }
        }
    }

    private fun sendNaturalCommand(text: String) {
        val normalized = normalize(text)
        val target = "tv"
        when {
            normalized.matches(Regex(".*\\b(wlacz|odpal|uruchom)\\b.*\\b(tv|telewizor|telewizora)\\b.*")) || normalized == "tv on" -> sendIntent("TV_POWER_ON", target)
            normalized.matches(Regex(".*\\b(wylacz|zgas)\\b.*\\b(tv|telewizor|telewizora)\\b.*")) || normalized == "tv off" -> sendIntent("TV_POWER_OFF", target)
            normalized.matches(Regex(".*\\b(wycisz|mute)\\b.*")) && (normalized.contains("tv") || normalized.contains("telewizor") || normalized == "wycisz" || normalized == "mute" || normalized == "wycisz go") -> sendIntent("TV_MUTE", target)
            normalized.contains("glosniej") || normalized.contains("louder") || normalized.contains("volume up") -> sendIntent("TV_VOLUME_UP", target)
            normalized.contains("ciszej") || normalized.contains("quieter") || normalized.contains("volume down") -> sendIntent("TV_VOLUME_DOWN", target)
            normalized.contains("hdmi 1") -> sendIntent("TV_HDMI_1", target)
            normalized.contains("nastepny kanal") || normalized.contains("next channel") -> sendIntent("TV_NEXT_CHANNEL", target)
            normalized.contains("youtube") && (normalized.contains("tv") || normalized.contains("telewizor")) -> sendIntent("TV_YOUTUBE", target)
            normalized.contains("temperature gpu") || normalized.contains("temperatura gpu") || normalized.contains("temperaturze gpu") -> sendIntent("PC_GPU_TEMP")
            normalized.contains("screenshot") || normalized.contains("zrzut ekranu") || normalized.contains("zrob zdjecie ekranu") -> sendIntent("PC_SCREENSHOT")
            normalized.contains("ram") || normalized.contains("cpu") || normalized.contains("status pc") || normalized.contains("status komputera") -> sendIntent("PC_STATUS")
            normalized.matches(Regex(".*\\b(wlacz|obudz)\\b.*\\b(komputer|pc)\\b.*")) -> wakePc()
            normalized.matches(Regex(".*\\b(wylacz|zamknij)\\b.*\\b(komputer|pc)\\b.*")) -> sendIntent("PC_SHUTDOWN")
            normalized.contains("cs2") -> sendIntent("PC_LAUNCH_APP", "CS2")
            normalized.contains("discord") -> sendIntent("PC_LAUNCH_APP", "Discord")
            normalized.contains("steam") -> sendIntent("PC_LAUNCH_APP", "Steam")
            normalized.contains("spotify") -> sendIntent("PC_LAUNCH_APP", "Spotify")
            normalized.contains("brave") -> sendIntent("PC_LAUNCH_APP", "Brave")
            normalized.contains("chrome") -> sendIntent("PC_LAUNCH_APP", "Chrome")
            normalized.contains("pobran") || normalized.contains("download") -> sendIntent("PC_DOWNLOAD_STATUS")
            normalized.startsWith("tryb ") || normalized.startsWith("makro ") -> sendTextCommand(text)
            else -> showResult("UNSUPPORTED", "Nie rozpoznałem bezpiecznego intentu. Dostępne są: PC status, RAM/CPU, aplikacje z listy, TV ON/OFF/głośność/wyciszenie/HDMI 1/YouTube, pobrania i wcześniej skonfigurowane makra.")
        }
    }

    private fun sendIntent(intent: String, target: String? = null) {
        val saved = secureStore.load()
        if (saved == null) { showResult("FAILED", "Najpierw sparuj telefon z Sentinel Bridge."); return }
        val parameters = JSONObject()
        if (target != null) parameters.put("target", target)
        setBusy("Wysyłam $intent przez lokalny, przypięty TLS…")
        io.execute {
            try {
                val response = client.execute(saved, intent, parameters)
                runOnUiThread { showResult(response.optString("status", "FAILED"), response.optString("message", "Brak opisu wyniku.")) }
            } catch (ex: Exception) {
                runOnUiThread { showResult("FAILED", ex.message ?: "Połączenie z PC nie powiodło się.") }
            }
        }
    }

    private fun sendTextCommand(text: String) {
        val saved = secureStore.load()
        if (saved == null) { showResult("FAILED", "Najpierw sparuj telefon z Sentinel Bridge."); return }
        setBusy("Szukam dokładnego dopasowania do lokalnego makra…")
        io.execute {
            try {
                val response = client.execute(saved, "PHONE_TEXT", JSONObject().put("text", text.take(80)))
                runOnUiThread { showResult(response.optString("status", "FAILED"), response.optString("message", "Brak opisu wyniku.")) }
            } catch (ex: Exception) {
                runOnUiThread { showResult("FAILED", ex.message ?: "Połączenie z PC nie powiodło się.") }
            }
        }
    }

    private fun wakePc() {
        val mac = wolMac.text.toString().trim()
        secureStore.saveWakeMac(mac)
        if (mac.isBlank()) { showResult("UNSUPPORTED", "Aby użyć Wake-on-LAN, skonfiguruj MAC i włącz WOL w BIOS/UEFI oraz sterowniku sieciowym."); return }
        setBusy("Wysyłam standardowy pakiet Wake-on-LAN w sieci lokalnej…")
        io.execute {
            try {
                WakeOnLan.send(mac)
                runOnUiThread { showResult("UNVERIFIED", "Wysłano pakiet WOL do lokalnego broadcastu. Nie mogę potwierdzić, czy komputer się uruchomił; spróbuj PC STATUS za chwilę.") }
            } catch (ex: Exception) {
                runOnUiThread { showResult("FAILED", ex.message ?: "Nie udało się wysłać pakietu WOL.") }
            }
        }
    }

    private fun startVoice() {
        if (checkSelfPermission(Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(arrayOf(Manifest.permission.RECORD_AUDIO), REQUEST_AUDIO)
            return
        }
        val voiceIntent = Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH)
            .putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM)
            .putExtra(RecognizerIntent.EXTRA_LANGUAGE, "pl-PL")
            .putExtra(RecognizerIntent.EXTRA_PREFER_OFFLINE, true)
            .putExtra(RecognizerIntent.EXTRA_PROMPT, "Powiedz komendę dla Sentinel")
        try { startActivityForResult(voiceIntent, REQUEST_VOICE) }
        catch (_: Exception) { showResult("UNSUPPORTED", "Brak usługi rozpoznawania mowy w telefonie.") }
    }

    @Deprecated("Uses the platform speech recognizer as an explicit user action.")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode == REQUEST_VOICE && resultCode == RESULT_OK) {
            val spoken = data?.getStringArrayListExtra(RecognizerIntent.EXTRA_RESULTS)?.firstOrNull().orEmpty()
            command.setText(spoken)
            if (spoken.isNotBlank()) sendNaturalCommand(spoken)
        }
    }

    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode == REQUEST_AUDIO && grantResults.firstOrNull() == PackageManager.PERMISSION_GRANTED) startVoice()
    }

    private fun handleWidgetAction(source: Intent?) {
        val action = source?.getStringExtra(EXTRA_WIDGET_INTENT) ?: return
        val target = source.getStringExtra(EXTRA_WIDGET_TARGET)
        source.removeExtra(EXTRA_WIDGET_INTENT)
        source.removeExtra(EXTRA_WIDGET_TARGET)
        if (action == "PC_WAKE") wakePc() else sendIntent(action, target)
    }

    private fun setBusy(message: String) = runOnUiThread { status.text = "Łączę…\n$message" }
    private fun showResult(result: String, message: String) { status.text = "$result · $message\n${bridgeLabel()}\n${irCapabilityLabel()}" }
    private fun bridgeLabel(): String = if (secureStore.load() == null) "Phone Bridge: NOT PAIRED" else "Phone Bridge: PAIRED (PC availability is checked on request)"
    private fun irCapabilityLabel(): String {
        val manager = getSystemService(Context.CONSUMER_IR_SERVICE) as? ConsumerIrManager
        return when {
            manager?.hasIrEmitter() == true -> "IR nadajnik: wykryty; profil urządzenia nie jest skonfigurowany — nie wysyłam kodów."
            else -> "IR nadajnik: niedostępny lub niezgłoszony przez Androida; brak losowych kodów."
        }
    }

    private fun normalize(input: String): String = Normalizer.normalize(input.lowercase(Locale.ROOT).replace('ł', 'l'), Normalizer.Form.NFD)
        .replace(Regex("\\p{Mn}+"), "").replace(Regex("\\s+"), " ").trim().trimEnd('.', '!', '?')

    companion object {
        const val EXTRA_WIDGET_INTENT = "sentinel.intent"
        const val EXTRA_WIDGET_TARGET = "sentinel.target"
        private const val REQUEST_VOICE = 4101
        private const val REQUEST_AUDIO = 4102
    }
}
