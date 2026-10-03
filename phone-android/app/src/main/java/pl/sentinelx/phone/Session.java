package pl.sentinelx.phone;

import android.content.Context;
import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;

import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.util.Locale;

import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/** Remembers the paired PC and encrypts its bearer token with an Android Keystore AES-GCM key. */
final class Session {
    private static final String KEY_ALIAS = "pl.sentinelx.phone.session.v1";
    private static final String TOKEN_CIPHER = "tokenCiphertext";
    private static final String LEGACY_TOKEN = "token";
    private static final Object TOKEN_LOCK = new Object();

    private final SharedPreferences prefs;

    Session(Context context) {
        prefs = context.getApplicationContext().getSharedPreferences("sentinelx", Context.MODE_PRIVATE);
    }

    String host() { return prefs.getString("host", ""); }
    int port() { return prefs.getInt("port", 43180); }
    String fingerprint() { return prefs.getString("fp", ""); }
    String pcName() { return prefs.getString("pcName", ""); }

    /** Reads the encrypted token, migrating the earlier app-private plaintext preference once when possible. */
    String token() {
        synchronized (TOKEN_LOCK) {
            try {
                String encrypted = prefs.getString(TOKEN_CIPHER, "");
                if (!encrypted.isEmpty()) {
                    String value = decryptToken(encrypted);
                    if (validToken(value)) return value;
                    throw new IllegalStateException("Stored session token is invalid.");
                }

                String legacy = prefs.getString(LEGACY_TOKEN, "");
                if (!validToken(legacy)) {
                    if (!legacy.isEmpty()) prefs.edit().remove(LEGACY_TOKEN).apply();
                    return "";
                }
                String migrated = encryptToken(legacy);
                boolean saved = prefs.edit().putString(TOKEN_CIPHER, migrated).remove(LEGACY_TOKEN).commit();
                if (!saved) throw new IllegalStateException("Could not migrate the session token.");
                return legacy;
            } catch (Exception e) {
                // A lost/corrupt Keystore key requires re-pairing; never fall back to a plaintext credential.
                prefs.edit().remove(TOKEN_CIPHER).remove(LEGACY_TOKEN).apply();
                deleteKey();
                return "";
            }
        }
    }

    String mac() { return prefs.getString("mac", ""); }
    long lastAlert() { return prefs.getLong("lastAlert", 0L); }
    boolean notificationsAsked() { return prefs.getBoolean("notificationsAsked", false); }

    boolean hasPc() { return !host().isEmpty() && !fingerprint().isEmpty(); }
    boolean hasToken() { return !token().isEmpty(); }
    String baseUrl() { return "https://" + host() + ":" + port() + "/"; }

    /** Remember where the PC is. A different certificate means a different PC (or a reinstalled one): the old token is useless then. */
    void savePc(String host, int port, String fp, String name) {
        String newFp = fp.toLowerCase(Locale.ROOT);
        boolean samePc = newFp.equals(fingerprint());
        SharedPreferences.Editor editor = prefs.edit()
                .putString("host", host).putInt("port", port).putString("fp", newFp);
        if (name != null && !name.isEmpty()) editor.putString("pcName", name);
        if (!samePc) editor.remove(TOKEN_CIPHER).remove(LEGACY_TOKEN).remove("lastAlert").remove("mac");
        editor.apply();
    }

    /** Returns false rather than persisting the bearer token in plaintext if Keystore encryption is unavailable. */
    boolean saveToken(String token, String pcName) {
        if (!validToken(token)) return false;
        String safeName = pcName == null ? "" : pcName.replaceAll("\\p{Cntrl}", " ").trim();
        if (safeName.length() > 64) safeName = safeName.substring(0, 64);
        synchronized (TOKEN_LOCK) {
            try {
                String encrypted = encryptToken(token);
                SharedPreferences.Editor editor = prefs.edit()
                        .putString(TOKEN_CIPHER, encrypted)
                        .putLong("lastAlert", 0L)
                        .remove(LEGACY_TOKEN);
                if (!safeName.isEmpty()) editor.putString("pcName", safeName);
                return editor.commit();
            } catch (Exception e) {
                prefs.edit().remove(TOKEN_CIPHER).remove(LEGACY_TOKEN).apply();
                deleteKey();
                return false;
            }
        }
    }

    void saveMac(String mac) {
        String value = mac == null ? "" : mac.trim();
        if (!value.isEmpty() && !value.matches("(?i)([0-9a-f]{2}[:-]){5}[0-9a-f]{2}")) value = "";
        prefs.edit().putString("mac", value).apply();
    }
    void setLastAlert(long id) { prefs.edit().putLong("lastAlert", id).apply(); }
    void setNotificationsAsked() { prefs.edit().putBoolean("notificationsAsked", true).apply(); }

    void clearToken() {
        synchronized (TOKEN_LOCK) { prefs.edit().remove(TOKEN_CIPHER).remove(LEGACY_TOKEN).remove("lastAlert").apply(); }
    }

    void forgetPc() {
        synchronized (TOKEN_LOCK) {
            prefs.edit().remove("host").remove("port").remove("fp").remove("pcName")
                    .remove(TOKEN_CIPHER).remove(LEGACY_TOKEN).remove("mac").remove("lastAlert").apply();
        }
    }

    private static boolean validToken(String token) {
        return token != null && token.matches("[A-Za-z0-9_-]{32,128}");
    }

    private static String encryptToken(String token) throws Exception {
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.ENCRYPT_MODE, getOrCreateKey());
        byte[] iv = cipher.getIV();
        byte[] ciphertext = cipher.doFinal(token.getBytes(StandardCharsets.UTF_8));
        byte[] payload = new byte[1 + iv.length + ciphertext.length];
        payload[0] = (byte) iv.length;
        System.arraycopy(iv, 0, payload, 1, iv.length);
        System.arraycopy(ciphertext, 0, payload, 1 + iv.length, ciphertext.length);
        return Base64.encodeToString(payload, Base64.NO_WRAP);
    }

    private static String decryptToken(String encoded) throws Exception {
        byte[] payload = Base64.decode(encoded, Base64.NO_WRAP);
        if (payload.length < 1 + 12 + 16) throw new IllegalStateException("Encrypted token is truncated.");
        int ivLength = payload[0] & 0xff;
        if (ivLength < 12 || ivLength > 16 || payload.length <= 1 + ivLength + 16)
            throw new IllegalStateException("Encrypted token has an invalid layout.");
        byte[] iv = new byte[ivLength];
        byte[] ciphertext = new byte[payload.length - 1 - ivLength];
        System.arraycopy(payload, 1, iv, 0, ivLength);
        System.arraycopy(payload, 1 + ivLength, ciphertext, 0, ciphertext.length);
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.DECRYPT_MODE, getOrCreateKey(), new GCMParameterSpec(128, iv));
        return new String(cipher.doFinal(ciphertext), StandardCharsets.UTF_8);
    }

    private static SecretKey getOrCreateKey() throws Exception {
        KeyStore store = KeyStore.getInstance("AndroidKeyStore");
        store.load(null);
        if (store.containsAlias(KEY_ALIAS)) {
            KeyStore.Entry entry = store.getEntry(KEY_ALIAS, null);
            if (entry instanceof KeyStore.SecretKeyEntry secret) return secret.getSecretKey();
            store.deleteEntry(KEY_ALIAS);
        }
        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
        generator.init(new KeyGenParameterSpec.Builder(KEY_ALIAS, KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setRandomizedEncryptionRequired(true)
                .setKeySize(256)
                .build());
        return generator.generateKey();
    }

    private static void deleteKey() {
        try {
            KeyStore store = KeyStore.getInstance("AndroidKeyStore");
            store.load(null);
            if (store.containsAlias(KEY_ALIAS)) store.deleteEntry(KEY_ALIAS);
        } catch (Exception ignored) {
            // A damaged/unavailable token is discarded even if the platform refuses to remove its old key.
        }
    }
}
