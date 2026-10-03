package pl.sentinelx.phone;

import android.Manifest;
import android.app.Activity;
import android.app.AlertDialog;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.content.Intent;
import android.content.ContentResolver;
import android.content.pm.PackageManager;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.net.ConnectivityManager;
import android.net.Network;
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
import android.webkit.ValueCallback;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Button;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.TextView;
import android.widget.Toast;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.InterruptedIOException;
import java.io.OutputStream;
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Sentinel X Telefon. A thin shell: the whole interface is the web app that the PC serves (same chat, tasks, notes, status and alerts as on the PC).
 * The shell adds what a browser cannot: it finds the PC by itself, trusts only its pinned certificate, shows alerts as notifications,
 * listens for speech in Polish and can wake the PC up (Wake-on-LAN).
 */
public class MainActivity extends Activity {
    private static final int REQUEST_VOICE = 41;
    private static final int REQUEST_NOTIFICATIONS = 42;
    private static final int REQUEST_FILE_CHOOSER = 43;
    private static final int REQUEST_SAVE_FILE = 44;
    private static final long MAX_PHONE_DOWNLOAD_BYTES = 25L * 1024L * 1024L;

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
    private Button offlineButton;
    private ValueCallback<Uri[]> pendingFileChooser;
    private final Object downloadGate = new Object();
    private PendingPhoneDownload pendingPhoneDownload;
    private AtomicBoolean activePhoneDownload;
    private boolean nativeDownloadBusy;
    private ConnectivityManager connectivityManager;
    private ConnectivityManager.NetworkCallback networkCallback;
    private final Handler ui = new Handler(Looper.getMainLooper());
    private volatile boolean connecting;
    private volatile boolean offlineController;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        session = new Session(this);
        Notifier.ensureChannels(this);
        AlertJobService.schedule(this);

        root = new FrameLayout(this);
        root.setBackgroundColor(getColor(R.color.sx_background));
        web = new WebView(this);
        web.setBackgroundColor(getColor(R.color.sx_background));
        configureWeb();
        root.addView(web, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        overlay = buildOverlay();
        root.addView(overlay, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        setContentView(root);
        applyInsets();
        watchNetworkChanges();
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
        box.setBackgroundColor(getColor(R.color.sx_background));
        box.setPadding(dp(28), dp(28), dp(28), dp(28));
        box.setClickable(true);

        overlayTitle = new TextView(this);
        overlayTitle.setTextColor(getColor(R.color.sx_text_primary));
        overlayTitle.setTextSize(22);
        overlayTitle.setGravity(Gravity.CENTER);
        overlayTitle.setTypeface(overlayTitle.getTypeface(), android.graphics.Typeface.BOLD);

        overlayText = new TextView(this);
        overlayText.setTextColor(getColor(R.color.sx_text_secondary));
        overlayText.setTextSize(15);
        overlayText.setGravity(Gravity.CENTER);
        overlayText.setPadding(0, dp(10), 0, dp(18));

        spinner = new ProgressBar(this);

        retryButton = button("Szukaj ponownie", true, v -> connect());
        wakeButton = button("Wybudź komputer", false, v -> wakePc());
        addressButton = button("Wpisz adres ręcznie", false, v -> promptAddress());
        resetButton = button("Połącz od nowa", false, v -> confirmReset());
        offlineButton = button("Otwórz panel offline", false, v -> loadOfflineController());

        box.addView(overlayTitle, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        box.addView(overlayText, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        box.addView(spinner, new LinearLayout.LayoutParams(dp(40), dp(40)));
        for (Button b : new Button[] { retryButton, wakeButton, addressButton, resetButton, offlineButton }) {
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
                ? new GradientDrawable(GradientDrawable.Orientation.LEFT_RIGHT,
                    new int[] { getColor(R.color.sx_accent_cyan), getColor(R.color.sx_accent_violet) })
                : new GradientDrawable();
        shape.setCornerRadius(dp(14));
        if (!primary) {
            shape.setColor(getColor(R.color.sx_surface));
            shape.setStroke(dp(1), getColor(R.color.sx_border));
        }
        b.setBackground(shape);
        b.setTextColor(primary ? getColor(R.color.sx_on_accent) : getColor(R.color.sx_text_primary));
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
        offlineButton.setVisibility(buttons);
        overlay.setVisibility(View.VISIBLE);
    }

    private void hideStatus() {
        overlay.setVisibility(View.GONE);
    }

    private void watchNetworkChanges() {
        connectivityManager = getSystemService(ConnectivityManager.class);
        if (connectivityManager == null || Build.VERSION.SDK_INT < 24) return;
        networkCallback = new ConnectivityManager.NetworkCallback() {
            @Override public void onAvailable(Network network) {
                ui.post(() -> { if (offlineController && !connecting) connect(); });
            }
        };
        try { connectivityManager.registerDefaultNetworkCallback(networkCallback); }
        catch (Exception ignored) { networkCallback = null; }
    }

    // ------------------------------------------------------------------ web view

    @SuppressWarnings("SetJavaScriptEnabled")
    private void configureWeb() {
        WebSettings settings = web.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        settings.setAllowFileAccess(false);
        // Only a user-selected document URI reaches an <input type=file>; arbitrary file:// access stays disabled.
        settings.setAllowContentAccess(true);
        settings.setMixedContentMode(WebSettings.MIXED_CONTENT_NEVER_ALLOW);
        settings.setMediaPlaybackRequiresUserGesture(true);
        settings.setSupportZoom(false);
        web.addJavascriptInterface(new Bridge(), "SXNative");
        web.setWebChromeClient(new WebChromeClient() {
            @Override
            public boolean onShowFileChooser(WebView view, ValueCallback<Uri[]> callback, FileChooserParams params) {
                if (pendingFileChooser != null) pendingFileChooser.onReceiveValue(null);
                pendingFileChooser = callback;
                Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
                intent.addCategory(Intent.CATEGORY_OPENABLE);
                intent.setType("*/*");
                intent.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, false);
                try {
                    startActivityForResult(intent, REQUEST_FILE_CHOOSER);
                    return true;
                } catch (Exception e) {
                    pendingFileChooser = null;
                    callback.onReceiveValue(null);
                    Toast.makeText(MainActivity.this, "Nie udało się otworzyć wyboru pliku.", Toast.LENGTH_LONG).show();
                    return false;
                }
            }
        });
        web.setWebViewClient(new WebViewClient() {
            @Override
            public void onReceivedSslError(WebView view, SslErrorHandler handler, SslError error) {
                String expected = session.fingerprint();
                String actual = fingerprintOf(error.getCertificate());
                if (!expected.isEmpty() && expected.equalsIgnoreCase(actual)) {
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
                if (request.isForMainFrame()) runOnUiThread(() -> {
                    if (!offlineController) loadOfflineController();
                    else showOffline();
                });
            }

            @Override
            public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                return !isPinnedOrigin(request.getUrl()); // keep navigation on the exact pinned HTTPS origin
            }

            @Override
            public void onPageFinished(WebView view, String url) {
                if (offlineController || (url != null && isPinnedOrigin(Uri.parse(url)))) hideStatus();
            }
        });
    }

    /** Load the exact Phone/web build packaged from the shared PWA sources, with a CSP and no file:// access. */
    private void loadOfflineController() {
        try {
            String html = readWebAsset("index.html");
            String css = readWebAsset("app.css");
            String script = readWebAsset("app.js");
            html = html.replace("<link rel=\"manifest\" href=\"manifest.webmanifest\">", "")
                    .replace("<link rel=\"icon\" href=\"icon.svg\" type=\"image/svg+xml\">", "")
                    .replace("<link rel=\"apple-touch-icon\" href=\"apple-touch-icon.png\">", "")
                    .replace("<link rel=\"stylesheet\" href=\"app.css\">", "<style>\n" + css + "\n</style>")
                    .replace("<script src=\"app.js\"></script>", "<script>\n" + script + "\n</script>");
            String policy = "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:; connect-src 'self'; object-src 'none'; base-uri 'none'; form-action 'none'\">";
            html = html.replace("</head>", policy + "</head>");
            String base = session.hasPc() ? session.baseUrl() : "https://offline.sentinelx.invalid/";
            offlineController = true;
            showStatus("Panel kontrolera", "Interfejs działa na telefonie. Szukam komputera w sieci lokalnej lub przez skonfigurowany VPN.", true, false);
            web.loadDataWithBaseURL(base, html, "text/html", "UTF-8", null);
        } catch (Exception e) {
            offlineController = false;
            showStatus("Panel offline niedostępny", "Nie udało się odczytać lokalnego interfejsu. Spróbuj ponownie albo przebuduj aplikację.", false, true);
        }
    }

    private String readWebAsset(String name) throws Exception {
        try (InputStream input = getAssets().open("phone-web/" + name);
             ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[8192];
            int read;
            while ((read = input.read(buffer)) != -1) output.write(buffer, 0, read);
            return new String(output.toByteArray(), StandardCharsets.UTF_8);
        }
    }

    /** Ask Android's Storage Access Framework for a destination; the file bytes never cross the JS bridge. */
    private boolean requestPhoneDownload(String id, String name, String sha256) {
        if (id == null || !id.matches("[A-Za-z0-9_-]{1,96}") || sha256 == null || !sha256.matches("(?i)[a-f0-9]{64}")) return false;
        String safeName = name == null ? "" : name.replaceAll("[\\p{Cntrl}/\\\\:*?\"<>|]", "_").trim();
        if (safeName.isEmpty()) safeName = "sentinelx-file.bin";
        if (safeName.length() > 180) safeName = safeName.substring(0, 180);
        synchronized (downloadGate) {
            if (nativeDownloadBusy) return false;
            nativeDownloadBusy = true;
            pendingPhoneDownload = new PendingPhoneDownload(id, safeName, sha256.toLowerCase(Locale.ROOT));
        }
        runOnUiThread(this::choosePhoneDownloadDestination);
        return true;
    }

    private void choosePhoneDownloadDestination() {
        PendingPhoneDownload request;
        synchronized (downloadGate) { request = pendingPhoneDownload; }
        if (request == null) return;
        Intent intent = new Intent(Intent.ACTION_CREATE_DOCUMENT);
        intent.addCategory(Intent.CATEGORY_OPENABLE);
        intent.setType("application/octet-stream");
        intent.putExtra(Intent.EXTRA_TITLE, request.name);
        try { startActivityForResult(intent, REQUEST_SAVE_FILE); }
        catch (Exception e) { finishPhoneDownload(false, false, "Nie udało się otworzyć wyboru miejsca zapisu."); }
    }

    private void startPhoneDownload(PendingPhoneDownload request, Uri destination) {
        AtomicBoolean cancelled = new AtomicBoolean(false);
        synchronized (downloadGate) { activePhoneDownload = cancelled; }
        new Thread(() -> transferPhoneFile(request, destination, cancelled), "sentinelx-phone-download").start();
    }

    private void transferPhoneFile(PendingPhoneDownload request, Uri destination, AtomicBoolean cancelled) {
        File temporary = null;
        boolean saved = false;
        boolean wasCancelled = false;
        String message = "Nie udało się pobrać pliku.";
        try {
            String token = session.token();
            if (token.isEmpty() || !session.hasPc()) throw new java.io.IOException("Sesja nie jest już sparowana.");
            String url = session.baseUrl() + "api/files/download?id=" + URLEncoder.encode(request.id, "UTF-8");
            javax.net.ssl.HttpsURLConnection connection = PinnedTls.open(url, session.fingerprint(), 10000);
            try {
                connection.setReadTimeout(30000);
                connection.setRequestProperty("Authorization", "Bearer " + token);
                connection.setRequestProperty("Accept", "application/octet-stream");
                int status = connection.getResponseCode();
                if (status != 200) throw new java.io.IOException("PC zwrócił HTTP " + status + ".");
                long expectedLength = connection.getContentLengthLong();
                if (expectedLength > MAX_PHONE_DOWNLOAD_BYTES) throw new java.io.IOException("Plik przekracza limit 25 MiB.");
                temporary = File.createTempFile("sentinelx-", ".download", getCacheDir());
                MessageDigest digest = MessageDigest.getInstance("SHA-256");
                long received = 0, lastProgress = 0;
                try (InputStream input = connection.getInputStream(); FileOutputStream output = new FileOutputStream(temporary)) {
                    byte[] buffer = new byte[8192];
                    int read;
                    while ((read = input.read(buffer)) != -1) {
                        if (cancelled.get()) throw new InterruptedIOException("Transfer anulowany.");
                        received += read;
                        if (received > MAX_PHONE_DOWNLOAD_BYTES) throw new java.io.IOException("Plik przekracza limit 25 MiB.");
                        output.write(buffer, 0, read);
                        digest.update(buffer, 0, read);
                        if (received - lastProgress >= 256 * 1024) {
                            reportPhoneDownloadProgress(received, expectedLength);
                            lastProgress = received;
                        }
                    }
                }
                if (received < 1 || (expectedLength >= 0 && received != expectedLength))
                    throw new java.io.IOException("Rozmiar pobranego pliku jest niezgodny z odpowiedzią PC.");
                String actualHash = toHex(digest.digest());
                if (!MessageDigest.isEqual(actualHash.getBytes(StandardCharsets.US_ASCII), request.sha256.getBytes(StandardCharsets.US_ASCII)))
                    throw new java.io.IOException("Kontrola SHA-256 nie powiodła się; plik nie zostanie zapisany.");
                reportPhoneDownloadProgress(received, received);
            } finally { connection.disconnect(); }

            if (cancelled.get()) throw new InterruptedIOException("Transfer anulowany.");
            ContentResolver resolver = getContentResolver();
            try (InputStream input = new FileInputStream(temporary); OutputStream output = resolver.openOutputStream(destination, "w")) {
                if (output == null) throw new java.io.IOException("Nie można zapisać pliku w wybranej lokalizacji.");
                byte[] buffer = new byte[8192];
                int read;
                while ((read = input.read(buffer)) != -1) {
                    if (cancelled.get()) throw new InterruptedIOException("Transfer anulowany.");
                    output.write(buffer, 0, read);
                }
                output.flush();
            }
            saved = true;
            message = "Zapisano „" + request.name + "”; SHA-256 zgodny.";
        } catch (InterruptedIOException e) {
            wasCancelled = cancelled.get();
            message = wasCancelled ? "Pobieranie anulowano; niezweryfikowany plik nie został zapisany." : "Transfer został przerwany przez system.";
        } catch (Exception e) {
            message = e.getMessage() == null ? "Transfer pliku nie powiódł się." : e.getMessage();
        } finally {
            if (temporary != null) {
                try { if (!temporary.delete()) temporary.deleteOnExit(); }
                catch (Exception ignored) { }
            }
            if (!saved) {
                try { getContentResolver().delete(destination, null, null); }
                catch (Exception ignored) { }
            }
            finishPhoneDownload(saved, wasCancelled, message);
        }
    }

    private static String toHex(byte[] bytes) {
        StringBuilder result = new StringBuilder(bytes.length * 2);
        for (byte value : bytes) result.append(String.format(Locale.ROOT, "%02x", value & 0xff));
        return result.toString();
    }

    private void reportPhoneDownloadProgress(long received, long total) {
        ui.post(() -> {
            if (web == null || isFinishing() || isDestroyed()) return;
            String script = "window.onNativeDownloadProgress&&window.onNativeDownloadProgress(" + received + "," + total + ")";
            web.evaluateJavascript(script, null);
        });
    }

    private void finishPhoneDownload(boolean success, boolean cancelled, String message) {
        synchronized (downloadGate) {
            pendingPhoneDownload = null;
            activePhoneDownload = null;
            nativeDownloadBusy = false;
        }
        String script = "window.onNativeDownloadComplete&&window.onNativeDownloadComplete(" + success + "," + cancelled + "," + JSONObject.quote(message) + ")";
        runOnUiThread(() -> { if (web != null && !isFinishing() && !isDestroyed()) web.evaluateJavascript(script, null); });
    }

    private void cancelPhoneDownload() {
        boolean wasWaitingForDestination = false;
        synchronized (downloadGate) {
            if (activePhoneDownload != null) activePhoneDownload.set(true);
            else if (pendingPhoneDownload != null) {
                pendingPhoneDownload = null;
                wasWaitingForDestination = true;
            }
        }
        if (wasWaitingForDestination) finishPhoneDownload(false, true, "Pobieranie anulowano.");
    }

    private boolean isPinnedOrigin(Uri uri) {
        return uri != null
                && "https".equalsIgnoreCase(uri.getScheme())
                && uri.getHost() != null
                && uri.getHost().equalsIgnoreCase(session.host())
                && uri.getPort() == session.port()
                && uri.getUserInfo() == null;
    }

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
                List<PcLocator.Pc> candidates = distinctByCertificate(PcLocator.discoverAll(session.fingerprint(), 2500));
                List<PcLocator.Pc> all = new ArrayList<>();
                for (PcLocator.Pc candidate : candidates) {
                    if (all.size() >= 8) break;
                    // UDP discovery is unauthenticated. Verify the advertised fingerprint against TLS before trusting or loading the page.
                    if (PcLocator.probe(candidate.host, candidate.port, candidate.fingerprint, 1500)) all.add(candidate);
                }
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
                runOnUiThread(this::loadOfflineController);
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
            offlineController = false;
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
            if (!PcLocator.probe(host, port, fp, 4000)) {
                runOnUiThread(() -> {
                    showOffline();
                    Toast.makeText(this, "Pod tym adresem nie znaleziono Sentinel X zgodnego z API telefonu.", Toast.LENGTH_LONG).show();
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

    private boolean notificationsAvailable() {
        if (Build.VERSION.SDK_INT >= 33
                && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) return false;
        NotificationManager manager = getSystemService(NotificationManager.class);
        if (manager == null || !manager.areNotificationsEnabled()) return false;
        if (Build.VERSION.SDK_INT >= 26) {
            NotificationChannel alerts = manager.getNotificationChannel(Notifier.CHANNEL_ALERTS);
            NotificationChannel info = manager.getNotificationChannel(Notifier.CHANNEL_INFO);
            boolean alertChannel = alerts != null && alerts.getImportance() != NotificationManager.IMPORTANCE_NONE;
            boolean infoChannel = info != null && info.getImportance() != NotificationManager.IMPORTANCE_NONE;
            return alertChannel || infoChannel;
        }
        return true;
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
        if (requestCode == REQUEST_SAVE_FILE) {
            PendingPhoneDownload request;
            synchronized (downloadGate) { request = pendingPhoneDownload; pendingPhoneDownload = null; }
            if (request == null) return;
            Uri destination = resultCode == RESULT_OK && data != null ? data.getData() : null;
            if (destination == null) { finishPhoneDownload(false, true, "Wybór miejsca zapisu został anulowany."); return; }
            startPhoneDownload(request, destination);
            return;
        }
        if (requestCode == REQUEST_FILE_CHOOSER) {
            ValueCallback<Uri[]> callback = pendingFileChooser;
            pendingFileChooser = null;
            if (callback == null) return;
            Uri[] selected = null;
            if (resultCode == RESULT_OK && data != null) {
                if (data.getClipData() != null && data.getClipData().getItemCount() > 0) {
                    selected = new Uri[] { data.getClipData().getItemAt(0).getUri() };
                } else if (data.getData() != null) {
                    selected = new Uri[] { data.getData() };
                }
            }
            callback.onReceiveValue(selected);
            return;
        }
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
        if (connectivityManager != null && networkCallback != null) {
            try { connectivityManager.unregisterNetworkCallback(networkCallback); }
            catch (Exception ignored) { }
            networkCallback = null;
        }
        if (pendingFileChooser != null) { pendingFileChooser.onReceiveValue(null); pendingFileChooser = null; }
        synchronized (downloadGate) {
            if (activePhoneDownload != null) activePhoneDownload.set(true);
            pendingPhoneDownload = null;
            nativeDownloadBusy = false;
        }
        web.removeJavascriptInterface("SXNative");
        web.destroy();
        super.onDestroy();
    }

    private static final class PendingPhoneDownload {
        final String id;
        final String name;
        final String sha256;
        PendingPhoneDownload(String id, String name, String sha256) { this.id = id; this.name = name; this.sha256 = sha256; }
    }

    /** What the web app may ask of the phone. Only our own (pinned) page is ever loaded into this WebView. */
    private final class Bridge {
        @JavascriptInterface
        public boolean isApp() { return true; }

        @JavascriptInterface
        public boolean isOfflineMode() { return offlineController; }

        @JavascriptInterface
        public String networkStatusJson() { return PhoneNetworkStatus.toJson(MainActivity.this); }

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

        /** Stable wire identifiers; the permission-dependent capabilities describe support, not server authorization. */
        @JavascriptInterface
        public String capabilitiesJson() {
            JSONArray capabilities = new JSONArray();
            if (notificationsAvailable()) capabilities.put("notifications");
            if (voiceAvailable()) capabilities.put("voiceInput");
            capabilities.put("wakeOnLan");
            return capabilities.toString();
        }

        @JavascriptInterface
        public boolean saveSession(String token, String name, String pcName) {
            boolean saved = token != null && !token.isEmpty() && session.saveToken(token, pcName);
            if (saved) AlertJobService.schedule(MainActivity.this);
            return saved;
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
        public boolean downloadFile(String id, String name, String sha256) { return requestPhoneDownload(id, name, sha256); }

        @JavascriptInterface
        public void cancelDownload() { MainActivity.this.cancelPhoneDownload(); }

        @JavascriptInterface
        public void rediscover() { runOnUiThread(MainActivity.this::connect); }
    }
}
