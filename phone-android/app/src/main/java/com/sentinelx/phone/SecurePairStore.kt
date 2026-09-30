package com.sentinelx.phone

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

data class SavedBridge(val host: String, val port: Int, val fingerprint: String, val deviceId: String, val token: String)

/** Stores the bridge token only as AES-GCM ciphertext under a non-exportable Android Keystore key. */
class SecurePairStore(context: Context) {
    private val prefs = context.getSharedPreferences("sentinel_bridge", Context.MODE_PRIVATE)

    fun save(host: String, port: Int, fingerprint: String, deviceId: String, token: String) {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, getOrCreateKey())
        val clear = token.toByteArray(Charsets.UTF_8)
        val encrypted = try { cipher.doFinal(clear) } finally { clear.fill(0) }
        val packed = cipher.iv + encrypted
        encrypted.fill(0)
        val ciphertext = Base64.encodeToString(packed, Base64.NO_WRAP)
        packed.fill(0)
        check(prefs.edit()
            .putString("host", host)
            .putInt("port", port)
            .putString("fingerprint", fingerprint)
            .putString("device_id", deviceId)
            .putString("token_ciphertext", ciphertext)
            .commit()) { "Nie udało się zapisać zaszyfrowanego klucza telefonu." }
    }

    fun load(): SavedBridge? {
        val packedText = prefs.getString("token_ciphertext", null) ?: return null
        return try {
            val packed = Base64.decode(packedText, Base64.NO_WRAP)
            require(packed.size > 12 + 16)
            val cipher = Cipher.getInstance("AES/GCM/NoPadding")
            cipher.init(Cipher.DECRYPT_MODE, getOrCreateKey(), GCMParameterSpec(128, packed.copyOfRange(0, 12)))
            val clear = cipher.doFinal(packed.copyOfRange(12, packed.size))
            val token = clear.toString(Charsets.UTF_8)
            clear.fill(0)
            SavedBridge(
                prefs.getString("host", "") ?: "",
                prefs.getInt("port", 0),
                prefs.getString("fingerprint", "") ?: "",
                prefs.getString("device_id", "") ?: "",
                token
            ).takeIf { it.host.isNotBlank() && it.port in 1..65535 && it.deviceId.isNotBlank() && it.token.isNotBlank() }
        } catch (_: Exception) {
            clear()
            null
        }
    }

    fun saveWakeMac(mac: String) { prefs.edit().putString("wake_mac", mac).apply() }
    fun loadWakeMac(): String = prefs.getString("wake_mac", "") ?: ""
    fun clear() {
        prefs.edit().remove("token_ciphertext").remove("device_id").remove("host").remove("port").remove("fingerprint").commit()
        runCatching {
            val keyStore = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
            if (keyStore.containsAlias(KEY_ALIAS)) keyStore.deleteEntry(KEY_ALIAS)
        }
    }

    private fun getOrCreateKey(): SecretKey {
        val keyStore = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (keyStore.getKey(KEY_ALIAS, null) as? SecretKey)?.let { return it }
        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
        generator.init(KeyGenParameterSpec.Builder(KEY_ALIAS, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
            .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
            .setRandomizedEncryptionRequired(true)
            .build())
        return generator.generateKey()
    }

    companion object { private const val KEY_ALIAS = "SentinelPhoneBridgeToken" }
}
