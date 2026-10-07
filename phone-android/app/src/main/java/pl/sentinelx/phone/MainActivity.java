package pl.sentinelx.phone;

import android.Manifest;
import android.app.Activity;
import android.app.AlertDialog;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.net.http.SslCertificate;
import android.net.http.SslError;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.speech.RecognizerIntent;
import android.text.InputType;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowInsets;
import android.webkit.JavascriptInterface;
import android.webkit.SslErrorHandler;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Button;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.TextView;
import android.widget.Toast;

import org.json.JSONObject;

import java.util.ArrayList;
import java.util.List;

/**
 * Sentinel X Telefon. A thin shell: the whole interface is the web app that the PC serves (same chat, tasks, notes, status and alerts as on the PC).
 * The shell adds what a browser cannot: it finds the PC by itself, trusts only its pinned certificate, shows alerts as notifications,
 * listens for speech in Polish and can wake the PC up (Wake-on-LAN).
 */
public class MainActivity extends Activity {
    private static final int BG = 0xFF0D0F14;
    private static final int REQUEST_VOICE = 41;
    private static final int REQUEST_NOTIFICATIONS = 42;

    private Session session;
    private FrameLayout root;
    private WebView web;
    private LinearLayout overlay;
    private TextView overlayTitle;
    private TextView overlayText;
    private ProgressBar spinner;
    private Button retryButton;
    private Button wakeButton;
    private Button addressButton;
    private Button resetButton;
    private final Handler ui = new Handler(Looper.getMainLooper());
    private volatile boolean connecting;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        session = new Session(this);
        Notifier.ensureChannels(this);
        AlertJobService.schedule(this);

        root = new FrameLayout(this);
        root.setBackgroundColor(BG);
        web = new WebView(this);
        web.setBackgroundColor(BG);
        configureWeb();
        root.addView(web, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        overlay = buildOverlay();
        root.addView(overlay, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        Button baseShortcut=button("Base SX4",false,v->startActivity(new Intent(this,BaseActivity.class)));
        FrameLayout.LayoutParams shortcut=new FrameLayout.LayoutParams(dp(112),dp(44),Gravity.TOP|Gravity.END);root.addView(baseShortcut,shortcut);
        setContentView(root);
        applyInsets();
        askForNotifications();
        connect();
    }

    // ------------------------------------------------------------------ layout

    @SuppressWarnings("deprecation")
    private void applyInsets() {
        root.setOnApplyWindowInsetsListener((view, insets) -> {
            int left;
            int top;
            int right;
            int bottom;
            if (Build.VERSION.SDK_INT >= 30) {
                android.graphics.Insets bars = insets.getInsets(WindowInsets.Type.systemBars() | WindowInsets.Type.ime());
                left = bars.left;
                top = bars.top;
                right = bars.right;
                bottom = bars.bottom;
            } else {
                left = insets.getSystemWindowInsetLeft();
                top = insets.getSystemWindowInsetTop();
                right = insets.getSystemWindowInsetRight();
                bottom = insets.getSystemWindowInsetBottom();
            }
            view.setPadding(left, top, right, bottom);
            return insets;
        });
        root.requestApplyInsets();
    }

    private int dp(int value) {
        return (int) (value * getResources().getDisplayMetrics().density + 0.5f);
    }

    private LinearLayout buildOverlay() {
        LinearLayout box = new LinearLayout(this);
        box.setOrientation(LinearLayout.VERTICAL);
        box.setGravity(Gravity.CENTER);
        box.setBackgroundColor(BG);
        box.setPadding(dp(28), dp(28), dp(28), dp(28));
        box.setClickable(true);

        overlayTitle = new TextView(this);
        overlayTitle.setTextColor(0xFFE8ECF4);
        overlayTitle.setTextSize(22);
        overlayTitle.setGravity(Gravity.CENTER);
        overlayTitle.setTypeface(overlayTitle.getTypeface(), android.graphics.Typeface.BOLD);

        overlayText = new TextView(this);
        overlayText.setTextColor(0xFF8B92A5);
        overlayText.setTextSize(15);
        overlayText.setGravity(Gravity.CENTER);
        overlayText.setPadding(0, dp(10), 0, dp(18));

        spinner = new ProgressBar(this);

        retryButton = button("Szukaj ponownie", true, v -> connect());
        wakeButton = button("Wybudź komputer", false, v -> wakePc());
        addressButton = button("Wpisz adres ręcznie", false, v -> promptAddress());
        resetButton = button("Połącz od nowa", false, v -> confirmReset());

        box.addView(overlayTitle, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        box.addView(overlayText, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        box.addView(spinner, new LinearLayout.LayoutParams(dp(40), dp(40)));
        Button baseButton=button("Sentinel Base · działa także bez PC",true,v->startActivity(new Intent(this,BaseActivity.class)));
        box.addView(baseButton,new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT,dp(54)));
        for (Button b : new Button[] { retryButton, wakeButton, addressButton, resetButton }) {
            LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(50));
            params.topMargin = dp(10);
            box.addView(b, params);
        }
        return box;
    }

    private Button button(String text, boolean primary, View.OnClickListener listener) {
        Button b = new Button(this);
        b.setText(text);
        b.setAllCaps(false);
        b.setTextSize(15);
        b.setOnClickListener(listener);
        GradientDrawable shape = primary
                ? new GradientDrawable(GradientDrawable.Orientation.LEFT_RIGHT, new int[] { 0xFF00D4FF, 0xFF8B5CF6 })
                : new GradientDrawable();
        shape.setCornerRadius(dp(14));
        if (!primary) {
            shape.setColor(0xFF141820);
            shape.setStroke(dp(1), 0xFF2A2F3C);
        }
        b.setBackground(shape);
        b.setTextColor(primary ? 0xFF04121A : 0xFFE8ECF4);
        return b;
    }

    private void showStatus(String title, String text, boolean searching, boolean withButtons) {
        overlayTitle.setText(title);
        overlayText.setText(text);
        spinner.setVisibility(searching ? View.VISIBLE : View.GONE);
        int buttons = withButtons ? View.VISIBLE : View.GONE;
        retryButton.setVisibility(buttons);
        addressButton.setVisibility(buttons);
        resetButton.setVisibility(session.hasPc() ? buttons : View.GONE);
        wakeButton.setVisibility(withButtons && !session.mac().isEmpty() ? View.VISIBLE : View.GONE);
        overlay.setVisibility(View.VISIBLE);
    }

    private void hideStatus() {
        overlay.setVisibility(View.GONE);
    }

    // ------------------------------------------------------------------ web view

    @SuppressWarnings("SetJavaScriptEnabled")
    private void configureWeb() {
        WebSettings settings = web.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        settings.setAllowFileAccess(false);
        settings.setAllowContentAccess(false);
        settings.setMixedContentMode(WebSettings.MIXED_CONTENT_NEVER_ALLOW);
        settings.setMediaPlaybackRequiresUserGesture(true);
        settings.setSupportZoom(false);
        web.addJavascriptInterface(new Bridge(), "SXNative");
        web.setWebChromeClient(new WebChromeClient());
        web.setWebViewClient(new WebViewClient() {
            @Override
            public void onReceivedSslError(WebView view, SslErrorHandler handler, SslError error) {
                String expected = session.fingerprint();
                String actual = fingerprintOf(error.getCertificate());
                if (!expected.isEmpty() && expected.equalsIgnoreCase(actual) && error.getPrimaryError()!=SslError.SSL_EXPIRED && error.getPrimaryError()!=SslError.SSL_NOTYETVALID && isPcOrigin(error.getUrl())) {
                    handler.proceed(); // exactly the certificate pinned during pairing
                } else {
                    handler.cancel();
                    runOnUiThread(() -> showStatus("Certyfikat komputera się zmienił",
                            "To może się zdarzyć po ponownej instalacji Sentinel X na komputerze. Jeśli to Twój komputer, wybierz „Połącz od nowa” i zatwierdź parowanie na komputerze.",
                            false, true));
                }
            }

            @Override
            public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
                if (request.isForMainFrame()) runOnUiThread(() -> showOffline());
            }

            @Override
            public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                Uri uri = request.getUrl();
                return !isPcOrigin(uri.toString()); // never leave the PC's own page
            }

            @Override
            public void onPageFinished(WebView view, String url) {
                if (isPcOrigin(url)) hideStatus();
            }
        });
    }

    private boolean isPcOrigin(String url) { if(url==null)return false; Uri u=Uri.parse(url);return "https".equals(u.getScheme())&&session.host().equals(u.getHost())&&(u.getPort()==session.port()||u.getPort()==-1&&session.port()==443); }

    private static String fingerprintOf(SslCertificate certificate) {
        try {
            Bundle state = SslCertificate.saveState(certificate);
            byte[] der = state.getByteArray("x509-certificate");
            return der == null ? "" : PinnedTls.sha256Hex(der);
        } catch (Exception e) {
            return "";
        }
    }

    // ------------------------------------------------------------------ finding and opening the PC

    /** Probe the known PC; if it moved (new IP from the router), find it again by broadcast; otherwise offer the manual options. */
    private void connect() {
        if (connecting) return;
        connecting = true;
        showStatus("Szukam komputera…", "Sentinel X szuka Twojego komputera w sieci domowej.", true, false);
        new Thread(() -> {
            try {
                if (session.hasPc() && PcLocator.probe(session.host(), session.port(), session.fingerprint())) {
                    openPc();
                    return;
                }
                List<PcLocator.Pc> all = distinctByCertificate(PcLocator.discoverAll(session.fingerprint(), 2500));
                if (all.size() == 1 || (!all.isEmpty() && session.hasPc())) {
                    PcLocator.Pc found = all.get(0);
                    session.savePc(found.host, found.port, found.fingerprint, found.name);
                    openPc();
                    return;
                }
                if (all.size() > 1) {
                    runOnUiThread(() -> chooseComputer(all));
                    return;
                }
                runOnUiThread(this::showOffline);
            } finally {
                connecting = false;
            }
        }, "sentinelx-connect").start();
    }

    /** One PC can answer on several network adapters; the certificate tells them apart from a really different PC. */
    private static List<PcLocator.Pc> distinctByCertificate(List<PcLocator.Pc> found) {
        List<PcLocator.Pc> result = new ArrayList<>();
        for (PcLocator.Pc pc : found) {
            boolean known = false;
            for (PcLocator.Pc other : result) if (other.fingerprint.equals(pc.fingerprint)) known = true;
            if (!known) result.add(pc);
        }
        return result;
    }

    private void chooseComputer(final List<PcLocator.Pc> computers) {
        String[] names = new String[computers.size()];
        for (int i = 0; i < names.length; i++) {
            PcLocator.Pc pc = computers.get(i);
            names[i] = (pc.name.isEmpty() ? pc.host : pc.name) + "  (" + pc.host + ")";
        }
        showStatus("Znalazłem kilka komputerów", "Wybierz ten, z którym chcesz się połączyć.", false, true);
        new AlertDialog.Builder(this)
                .setTitle("Wybierz komputer")
                .setItems(names, (dialog, which) -> {
                    PcLocator.Pc pc = computers.get(which);
                    session.savePc(pc.host, pc.port, pc.fingerprint, pc.name);
                    openPc();
                })
                .setNegativeButton("Anuluj", null)
                .show();
    }

    private void openPc() {
        runOnUiThread(() -> {
            showStatus("Łączę z komputerem…", session.pcName().isEmpty() ? "" : session.pcName(), true, false);
            web.loadUrl(session.baseUrl());
        });
    }

    private void showOffline() {
        String title = session.hasPc() ? "Nie widzę komputera" : "Nie znalazłem komputera";
        String text = session.hasPc()
                ? "Sprawdź, czy komputer jest włączony i w tej samej sieci Wi‑Fi. Sentinel X połączy się sam, gdy tylko się pojawi."
                : "Na komputerze musi działać Sentinel X (wersja 0.94 lub nowsza), a telefon musi być w tej samej sieci Wi‑Fi. Możesz też wpisać adres ręcznie.";
        showStatus(title, text, false, true);
    }

    private void promptAddress() {
        final EditText input = new EditText(this);
        input.setHint("np. 192.168.1.23");
        input.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_URI);
        input.setSingleLine(true);
        new AlertDialog.Builder(this)
                .setTitle("Adres komputera")
                .setMessage("Adres IP komputera w sieci domowej (widać go w oknie „Telefon” w Sentinel X na komputerze).")
                .setView(input)
                .setNegativeButton("Anuluj", null)
                .setPositiveButton("Połącz", (dialog, which) -> useAddress(input.getText().toString()))
                .show();
    }

    private void useAddress(String typed) {
        String text = typed.trim().replace("https://", "").replace("/", "");
        if (text.isEmpty()) return;
        final String host;
        final int port;
        int colon = text.lastIndexOf(':');
        try {
            host = colon > 0 ? text.substring(0, colon) : text;
            port = colon > 0 ? Integer.parseInt(text.substring(colon + 1)) : 43180;
        } catch (NumberFormatException e) {
            Toast.makeText(this, "Niepoprawny adres.", Toast.LENGTH_LONG).show();
            return;
        }
        showStatus("Łączę…", host, true, false);
        new Thread(() -> {
            String fp = PinnedTls.peekFingerprint(host, port, 4000);
            if (fp == null) {
                runOnUiThread(() -> {
                    showOffline();
                    Toast.makeText(this, "Nic nie odpowiada pod tym adresem.", Toast.LENGTH_LONG).show();
                });
                return;
            }
            session.savePc(host, port, fp, host);
            openPc();
        }, "sentinelx-address").start();
    }

    private void confirmReset() {
        new AlertDialog.Builder(this)
                .setTitle("Połączyć od nowa?")
                .setMessage("Telefon zapomni ten komputer. Potem wystarczy zatwierdzić parowanie jednym kliknięciem na komputerze.")
                .setNegativeButton("Anuluj", null)
                .setPositiveButton("Połącz od nowa", (dialog, which) -> {
                    session.forgetPc();
                    web.clearCache(true);
                    web.loadUrl("about:blank");
                    connect();
                })
                .show();
    }

    private void wakePc() {
        final String mac = session.mac();
        if (mac.isEmpty()) return;
        new Thread(() -> {
            try {
                WakeOnLan.send(mac);
                runOnUiThread(() -> Toast.makeText(this, "Wysłano sygnał budzenia. Komputer włączy się za chwilę, jeśli ma włączone Wake-on-LAN.", Toast.LENGTH_LONG).show());
            } catch (Exception e) {
                runOnUiThread(() -> Toast.makeText(this, "Nie udało się wysłać sygnału budzenia.", Toast.LENGTH_LONG).show());
            }
        }, "sentinelx-wake").start();
    }

    // ------------------------------------------------------------------ notifications and voice

    private void askForNotifications() {
        if (Build.VERSION.SDK_INT < 33 || session.notificationsAsked()) return;
        session.setNotificationsAsked();
        if (checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[] { Manifest.permission.POST_NOTIFICATIONS }, REQUEST_NOTIFICATIONS);
        }
    }

    private boolean voiceAvailable() {
        List<?> handlers = getPackageManager().queryIntentActivities(new Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH), 0);
        return handlers != null && !handlers.isEmpty();
    }

    private void startVoice() {
        Intent intent = new Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH);
        intent.putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM);
        intent.putExtra(RecognizerIntent.EXTRA_LANGUAGE, "pl-PL");
        intent.putExtra(RecognizerIntent.EXTRA_PROMPT, "Powiedz polecenie");
        try {
            startActivityForResult(intent, REQUEST_VOICE);
        } catch (Exception e) {
            Toast.makeText(this, "Rozpoznawanie mowy jest niedostępne na tym telefonie.", Toast.LENGTH_LONG).show();
        }
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != REQUEST_VOICE || resultCode != RESULT_OK || data == null) return;
        ArrayList<String> results = data.getStringArrayListExtra(RecognizerIntent.EXTRA_RESULTS);
        if (results == null || results.isEmpty()) return;
        web.evaluateJavascript("window.onNativeVoice && window.onNativeVoice(" + JSONObject.quote(results.get(0)) + ")", null);
    }

    // ------------------------------------------------------------------ lifecycle

    @Override
    public void onBackPressed() {
        if (overlay.getVisibility() != View.VISIBLE && web.canGoBack()) web.goBack();
        else super.onBackPressed();
    }

    @Override
    protected void onResume() {
        super.onResume();
        web.onResume();
        if (overlay.getVisibility() == View.VISIBLE && !connecting && session.hasPc()) connect();
    }

    @Override
    protected void onPause() {
        web.onPause();
        super.onPause();
    }

    @Override
    protected void onDestroy() {
        ui.removeCallbacksAndMessages(null);
        web.removeJavascriptInterface("SXNative");
        web.destroy();
        super.onDestroy();
    }

    /** What the web app may ask of the phone. Only our own (pinned) page is ever loaded into this WebView. */
    private final class Bridge {
        @JavascriptInterface
        public boolean isApp() { return true; }

        /** The fingerprint of the certificate this page was loaded with: the web app derives the pairing code from it. */
        @JavascriptInterface
        public String tlsFingerprint() { return session.fingerprint(); }

        @JavascriptInterface
        public String deviceName() {
            String model = Build.MODEL == null ? "" : Build.MODEL;
            String maker = Build.MANUFACTURER == null ? "" : Build.MANUFACTURER;
            String name = model.toLowerCase().startsWith(maker.toLowerCase()) ? model : (maker + " " + model).trim();
            return name.length() > 32 ? name.substring(0, 32) : name;
        }

        /** A copy of the token that survives a change of the PC's address (the page's localStorage is tied to the address). */
        @JavascriptInterface
        public String savedToken() { return session.token(); }

        @JavascriptInterface
        public void saveSession(String token, String name, String pcName) {
            if (token != null && !token.isEmpty()) session.saveToken(token, pcName);
            AlertJobService.schedule(MainActivity.this);
        }

        @JavascriptInterface
        public void saveMac(String mac) { session.saveMac(mac); }

        @JavascriptInterface
        public void clearSession() { session.clearToken(); }

        @JavascriptInterface
        public boolean hasVoice() { return voiceAvailable(); }

        @JavascriptInterface
        public void startVoice() { runOnUiThread(MainActivity.this::startVoice); }

        @JavascriptInterface
        public boolean canWake() { return !session.mac().isEmpty(); }

        @JavascriptInterface
        public void wakePc() { MainActivity.this.wakePc(); }

        @JavascriptInterface
        public void rediscover() { runOnUiThread(MainActivity.this::connect); }
    }
}
