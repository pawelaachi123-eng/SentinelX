package com.sentinelx.phone

import org.json.JSONObject
import java.net.InetSocketAddress
import java.security.MessageDigest
import java.security.SecureRandom
import java.security.cert.X509Certificate
import java.util.Base64
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLSocket
import javax.net.ssl.X509TrustManager

class BridgeClient {
    fun pair(host: String, port: Int, fingerprint: String, code: String, deviceName: String): JSONObject {
        val request = JSONObject()
            .put("protocolVersion", 1)
            .put("type", "pair")
            .put("pairingCode", code.trim().uppercase())
            .put("deviceName", deviceName.trim())
        return exchange(host, port, fingerprint, request)
    }

    fun execute(saved: SavedBridge, intent: String, parameters: JSONObject = JSONObject()): JSONObject {
        val nonce = ByteArray(18).also(SecureRandom()::nextBytes)
        val request = JSONObject()
            .put("protocolVersion", 1)
            .put("type", "execute")
            .put("deviceId", saved.deviceId)
            .put("token", saved.token)
            .put("intent", intent)
            .put("parameters", parameters)
            .put("nonce", Base64.getUrlEncoder().withoutPadding().encodeToString(nonce))
            .put("timestampUnixSeconds", System.currentTimeMillis() / 1000L)
        nonce.fill(0)
        return exchange(saved.host, saved.port, saved.fingerprint, request)
    }

    private fun exchange(host: String, port: Int, suppliedFingerprint: String, request: JSONObject): JSONObject {
        require(isPrivateIpv4(host)) { "Podaj prywatny adres IPv4 widoczny w ustawieniach Sentinel Bridge." }
        require(port in 1..65535)
        val expectedPin = normalizeFingerprint(suppliedFingerprint)
        require(expectedPin.length == 64 && expectedPin.all { it in "0123456789ABCDEF" }) { "Odcisk SHA-256 certyfikatu jest nieprawidłowy." }
        val trust = object : X509TrustManager {
            override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
            override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) {
                throw java.security.cert.CertificateException("Klient nie używa certyfikatów klienta.")
            }
            override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) {
                if (chain.isNullOrEmpty()) throw java.security.cert.CertificateException("Certyfikat bridge nie został wysłany.")
                chain[0].checkValidity()
                val pin = MessageDigest.getInstance("SHA-256").digest(chain[0].encoded)
                    .joinToString("") { "%02X".format(it) }
                if (!MessageDigest.isEqual(pin.toByteArray(Charsets.US_ASCII), expectedPin.toByteArray(Charsets.US_ASCII)))
                    throw java.security.cert.CertificateException("Odcisk certyfikatu nie pasuje do wartości sprawdzonej na PC.")
            }
        }
        val context = SSLContext.getInstance("TLS")
        context.init(null, arrayOf(trust), SecureRandom())
        val raw = java.net.Socket()
        raw.connect(InetSocketAddress(host, port), 5_000)
        raw.soTimeout = 12_000
        val socket = context.socketFactory.createSocket(raw, host, port, true) as SSLSocket
        socket.use { tls ->
            tls.enabledProtocols = tls.supportedProtocols.filter { it == "TLSv1.3" || it == "TLSv1.2" }.toTypedArray()
            tls.startHandshake()
            val output = tls.getOutputStream().bufferedWriter(Charsets.UTF_8)
            output.write(request.toString())
            output.write("\n")
            output.flush()
            val reader = tls.getInputStream().bufferedReader(Charsets.UTF_8)
            val response = reader.readLine() ?: throw java.io.IOException("Bridge zamknął połączenie bez wyniku.")
            require(response.length <= 32 * 1024) { "Wynik przekroczył limit rozmiaru." }
            return JSONObject(response)
        }
    }

    private fun normalizeFingerprint(input: String): String = input.filter { it.isLetterOrDigit() }.uppercase()

    private fun isPrivateIpv4(value: String): Boolean {
        val parts = value.split('.')
        if (parts.size != 4 || parts.any { it.length !in 1..3 || it.any { c -> !c.isDigit() } }) return false
        val octets = parts.map { it.toIntOrNull() ?: return false }
        if (octets.any { it !in 0..255 }) return false
        return octets[0] == 10 || (octets[0] == 172 && octets[1] in 16..31) || (octets[0] == 192 && octets[1] == 168)
    }
}
