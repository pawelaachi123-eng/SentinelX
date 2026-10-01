package pl.sentinelx.phone;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.net.InetSocketAddress;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.SecureRandom;
import java.security.cert.CertificateException;
import java.security.cert.X509Certificate;
import java.util.Locale;

import javax.net.ssl.HttpsURLConnection;
import javax.net.ssl.SSLContext;
import javax.net.ssl.SSLSocket;
import javax.net.ssl.TrustManager;
import javax.net.ssl.X509TrustManager;

/** HTTPS to the PC without any certificate authority: only the certificate whose SHA-256 fingerprint was shown during pairing is trusted. */
final class PinnedTls {
    private PinnedTls() { }

    static String sha256Hex(byte[] data) {
        try {
            byte[] hash = MessageDigest.getInstance("SHA-256").digest(data);
            StringBuilder hex = new StringBuilder(hash.length * 2);
            for (byte b : hash) hex.append(String.format(Locale.ROOT, "%02x", b & 0xff));
            return hex.toString();
        } catch (Exception e) {
            throw new IllegalStateException(e);
        }
    }

    /** Trusts exactly one certificate. With an empty fingerprint it accepts anything and only records what it saw (used to read a fingerprint). */
    static final class PinnedTrustManager implements X509TrustManager {
        private final String expected;
        private volatile String seen = "";

        PinnedTrustManager(String expectedHex) {
            expected = expectedHex == null ? "" : expectedHex.toLowerCase(Locale.ROOT);
        }

        String seen() { return seen; }

        @Override
        public void checkClientTrusted(X509Certificate[] chain, String authType) throws CertificateException {
            throw new CertificateException("client certificates are not used");
        }

        @Override
        public void checkServerTrusted(X509Certificate[] chain, String authType) throws CertificateException {
            if (chain == null || chain.length == 0) throw new CertificateException("empty certificate chain");
            try {
                seen = sha256Hex(chain[0].getEncoded());
            } catch (Exception e) {
                throw new CertificateException(e);
            }
            if (!expected.isEmpty() && !MessageDigest.isEqual(seen.getBytes(StandardCharsets.US_ASCII), expected.getBytes(StandardCharsets.US_ASCII))) {
                throw new CertificateException("certificate fingerprint mismatch");
            }
        }

        @Override
        public X509Certificate[] getAcceptedIssuers() { return new X509Certificate[0]; }
    }

    static HttpsURLConnection open(String url, String fingerprint, int timeoutMs) throws IOException {
        try {
            SSLContext context = SSLContext.getInstance("TLS");
            context.init(null, new TrustManager[] { new PinnedTrustManager(fingerprint) }, new SecureRandom());
            HttpsURLConnection connection = (HttpsURLConnection) new URL(url).openConnection();
            connection.setSSLSocketFactory(context.getSocketFactory());
            connection.setHostnameVerifier((host, session) -> true); // the pinned fingerprint replaces host name checks: the PC has no public name
            connection.setConnectTimeout(timeoutMs);
            connection.setReadTimeout(timeoutMs);
            connection.setUseCaches(false);
            return connection;
        } catch (IOException e) {
            throw e;
        } catch (Exception e) {
            throw new IOException(e);
        }
    }

    static class HttpStatusException extends IOException {
        final int status;

        HttpStatusException(int status) {
            super("HTTP " + status);
            this.status = status;
        }
    }

    static String getText(String url, String fingerprint, String token, int timeoutMs) throws IOException {
        HttpsURLConnection connection = open(url, fingerprint, timeoutMs);
        try {
            if (token != null && !token.isEmpty()) connection.setRequestProperty("Authorization", "Bearer " + token);
            connection.setRequestProperty("Accept", "application/json");
            int code = connection.getResponseCode();
            if (code != 200) throw new HttpStatusException(code);
            return readAll(connection.getInputStream());
        } finally {
            connection.disconnect();
        }
    }

    private static String readAll(InputStream stream) throws IOException {
        try (InputStream in = stream) {
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            byte[] buffer = new byte[4096];
            int read;
            while ((read = in.read(buffer)) != -1) {
                out.write(buffer, 0, read);
                if (out.size() > 512 * 1024) throw new IOException("response too large");
            }
            return out.toString("UTF-8");
        }
    }

    /** Connects without trusting anything and returns the fingerprint of the certificate the PC presents, or null (used when the user types an address). */
    static String peekFingerprint(String host, int port, int timeoutMs) {
        PinnedTrustManager recorder = new PinnedTrustManager("");
        try {
            SSLContext context = SSLContext.getInstance("TLS");
            context.init(null, new TrustManager[] { recorder }, new SecureRandom());
            try (SSLSocket socket = (SSLSocket) context.getSocketFactory().createSocket()) {
                socket.connect(new InetSocketAddress(host, port), timeoutMs);
                socket.setSoTimeout(timeoutMs);
                socket.startHandshake();
            }
            String fp = recorder.seen();
            return fp.isEmpty() ? null : fp;
        } catch (Exception e) {
            return null;
        }
    }
}
