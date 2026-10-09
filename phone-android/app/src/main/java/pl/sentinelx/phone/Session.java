package pl.sentinelx.phone;

import android.content.Context;
import android.content.SharedPreferences;

import java.util.Locale;

/** Everything the app remembers: which PC, the fingerprint of its certificate (pinned) and this phone's token. */
final class Session {
    private final SharedPreferences prefs;
    private final SecretStore secrets;

    Session(Context context) {
        prefs = context.getApplicationContext().getSharedPreferences("sentinelx", Context.MODE_PRIVATE);
        secrets = new SecretStore(context);
        String legacy=prefs.getString("token","");
        if(!legacy.isEmpty()){secrets.put("pc.token",legacy);if(!prefs.edit().remove("token").commit())throw new IllegalStateException("token_migration");}
    }

    String host() { return prefs.getString("host", ""); }
    int port() { return prefs.getInt("port", 43180); }
    String fingerprint() { return prefs.getString("fp", ""); }
    String pcName() { return prefs.getString("pcName", ""); }
    String token() { return secrets.get("pc.token"); }
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
        if (!samePc) { secrets.remove("pc.token"); editor.remove("token").remove("lastAlert").remove("mac"); }
        editor.apply();
    }

    void saveToken(String token, String pcName) {
        secrets.put("pc.token",token);
        SharedPreferences.Editor editor = prefs.edit().putLong("lastAlert", 0L);
        if (pcName != null && !pcName.isEmpty()) editor.putString("pcName", pcName);
        editor.apply();
    }

    void saveMac(String mac) { prefs.edit().putString("mac", mac == null ? "" : mac).apply(); }
    void setLastAlert(long id) { prefs.edit().putLong("lastAlert", id).apply(); }
    void setNotificationsAsked() { prefs.edit().putBoolean("notificationsAsked", true).apply(); }
    void clearToken() { secrets.remove("pc.token"); prefs.edit().remove("token").remove("lastAlert").apply(); }

    void forgetPc() {
        secrets.remove("pc.token");
        prefs.edit().remove("host").remove("port").remove("fp").remove("pcName").remove("token").remove("mac").remove("lastAlert").apply();
    }
}
