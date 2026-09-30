# Sentinel Phone (Android source)

Open this directory as a standalone project in Android Studio (Android Gradle Plugin 8.7.3, Kotlin 2.0.21, Android SDK 35). This repository intentionally contains source only—no wrapper JAR, APK, signing key, or compiled binary.

Pair only while Sentinel Bridge is explicitly running on the PC. The phone asks for a private IPv4, port 43179, PC-displayed SHA-256 certificate fingerprint and single-use code. It rejects non-RFC1918 IPv4 endpoints and validates the exact TLS certificate pin before sending the code or token. The random bearer token is persisted only as AES-GCM ciphertext with an Android Keystore key. “Usuń klucz tego telefonu” clears local credentials; also revoke the paired token on the PC.

Voice input is an explicit button using Android's speech-recognition provider with an offline preference hint; the platform may still use a network service. No background wake-word listener is enabled. IR hardware is checked, but no profile or codes are configured/transmitted. Home-screen shortcuts open the app to perform an action rather than running a background listener.
