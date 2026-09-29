package pl.sentinelx.companion

import android.Manifest
import android.app.Activity
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.view.Gravity
import android.view.ViewGroup
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.TextView

/**
 * Jedyne okno aplikacji: parowanie z SentinelX (adres, port, token) i status.
 * Cała reszta dzieje się w CallService (foreground): most, dzwonienie z SIM, audio głośnika.
 */
class MainActivity : Activity() {

    private lateinit var status: TextView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val pad = (16 * resources.displayMetrics.density).toInt()
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(pad, pad, pad, pad)
        }
        fun label(text: String) = TextView(this).apply { this.text = text }
        fun field(hint: String, value: String) = EditText(this).apply {
            this.hint = hint
            setText(value)
            setSingleLine(true)
        }
        val host = field("adres komputera w WiFi (np. 192.168.1.20)", getPref("host"))
        val port = field("port (z Sentinel: „włącz most telefoniczny”)", getPref("port", "7877"))
        val token = field("token parowania", getPref("token"))
        val connect = Button(this).apply { text = "Połącz i trzymaj most" }
        status = TextView(this).apply { text = "Most wyłączony. Sentinel nie może dzwonić, dopóki się nie sparujesz." }
        connect.setOnClickListener {
            savePref("host", host.text.toString().trim())
            savePref("port", port.text.toString().trim())
            savePref("token", token.text.toString().trim())
            requestNeededPermissions { startForegroundService(Intent(this, CallService::class.java)) }
        }
        root.addView(label("Sentinel Companion — most Twojego telefonu (SIM/eSIM)"))
        root.addView(host); root.addView(port); root.addView(token); root.addView(connect); root.addView(status)
        addContentView(root, ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
        CallService.statusListener = { runOnUiThread { status.text = it } }
    }

    private fun requestNeededPermissions(onReady: () -> Unit) {
        val needed = buildList {
            add(Manifest.permission.CALL_PHONE)
            add(Manifest.permission.RECORD_AUDIO)
            add(Manifest.permission.READ_PHONE_STATE)
            if (Build.VERSION.SDK_INT >= 33) add(Manifest.permission.POST_NOTIFICATIONS)
        }.filter { checkSelfPermission(it) != PackageManager.PERMISSION_GRANTED }
        if (needed.isEmpty()) { onReady(); return }
        pending = onReady
        requestPermissions(needed.toTypedArray(), 41)
    }

    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode == 41 && grantResults.all { it == PackageManager.PERMISSION_GRANTED }) pending?.invoke()
        else status.text = "Bez uprawnień (telefon/mikrofon/stan telefonu) most nie zadziała — to uczciwe ograniczenie Androida."
    }

    private var pending: (() -> Unit)? = null

    private fun getPref(key: String, def: String = "") =
        getSharedPreferences("companion", MODE_PRIVATE).getString(key, def) ?: def

    private fun savePref(key: String, value: String) =
        getSharedPreferences("companion", MODE_PRIVATE).edit().putString(key, value).apply()
}
