package pl.sentinelx.phone;

import android.content.Context;
import android.content.SharedPreferences;

import java.util.Locale;

/** Everything the app remembers: which PC, the fingerprint of its certificate (pinned) and this phone's token. */
final class Session {
    private final SharedPreferences prefs;

    Session(Context context) {
        prefs = context.getApplicationContext().getSharedPreferences("sentinelx", Context.MODE_PRIVATE);
    }

    String host() { return prefs.getString("host", ""); }
    int port() { return prefs.getInt("port", 43180); }
    String fingerprint() { return prefs.getString("fp", ""); }
    String pcName() { return prefs.getString("pcName", ""); }
    String token() { return prefs.getString("token", ""); }
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
        if (!samePc) editor.remove("token").remove("lastAlert").remove("mac");
        editor.apply();
    }

    void saveToken(String token, String pcName) {
        if (token == null || !token.matches("[A-Za-z0-9_-]{32,128}")) return;
        SharedPreferences.Editor editor = prefs.edit().putString("token", token).putLong("lastAlert", 0L);
        if (pcName != null && !pcName.isEmpty()) {
            String safeName = pcName.replaceAll("\\p{Cntrl}", " ").trim();
            editor.putString("pcName", safeName.length() > 64 ? safeName.substring(0, 64) : safeName);
        }
        editor.apply();
    }

    void saveMac(String mac) {
        String value = mac == null ? "" : mac.trim();
        if (!value.isEmpty() && !value.matches("(?i)([0-9a-f]{2}[:-]){5}[0-9a-f]{2}")) value = "";
        prefs.edit().putString("mac", value).apply();
    }
    void setLastAlert(long id) { prefs.edit().putLong("lastAlert", id).apply(); }
    void setNotificationsAsked() { prefs.edit().putBoolean("notificationsAsked", true).apply(); }
    void clearToken() { prefs.edit().remove("token").remove("lastAlert").apply(); }

    void forgetPc() {
        prefs.edit().remove("host").remove("port").remove("fp").remove("pcName").remove("token").remove("mac").remove("lastAlert").apply();
    }
}
