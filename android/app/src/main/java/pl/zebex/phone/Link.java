package pl.zebex.phone;

import android.content.SharedPreferences;
import org.json.JSONObject;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.net.URI;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.SecureRandom;
import java.security.cert.CertificateException;
import java.security.cert.X509Certificate;
import java.util.Locale;
import javax.net.ssl.HttpsURLConnection;
import javax.net.ssl.SSLContext;
import javax.net.ssl.TrustManager;
import javax.net.ssl.X509TrustManager;

final class Link {
    static final class Failure extends Exception { final int status; Failure(int status, String message) { super(message); this.status = status; } }
    private final SharedPreferences preferences;
    Link(SharedPreferences preferences) { this.preferences = preferences; }
    boolean paired() { return preferences.contains("token"); }
    static JSONObject validatePairQr(String text) throws Exception {
        JSONObject q = new JSONObject(text); URI uri = new URI(q.getString("url")); String host = uri.getHost();
        if (q.getInt("v") != 1 || !"https".equals(uri.getScheme()) || uri.getPort() != 18790 || uri.getRawUserInfo() != null || uri.getRawQuery() != null || uri.getRawFragment() != null || (uri.getRawPath() != null && !uri.getRawPath().isEmpty())) throw new IllegalArgumentException("To nie jest kod QR z programu Zebex Phone.");
        if (host == null || !host.matches("[0-9]{1,3}(\\.[0-9]{1,3}){3}")) throw new IllegalArgumentException("Kod QR musi wskazywać komputer w lokalnej sieci Wi-Fi.");
        String[] parts = host.split("\\."); int[] b = new int[4]; for (int i = 0; i < 4; i++) { b[i] = Integer.parseInt(parts[i]); if (b[i] > 255 || !Integer.toString(b[i]).equals(parts[i])) throw new IllegalArgumentException("Nieprawidłowy adres komputera."); }
        if (!(b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168))) throw new IllegalArgumentException("Wymagana wspólna lokalna sieć Wi-Fi.");
        if (!q.getString("pin").matches("[A-Fa-f0-9]{64}") || !q.getString("code").matches("[0-9]{6}")) throw new IllegalArgumentException("Nieprawidłowy kod parowania.");
        return q;
    }
    void pair(String qr, String clientId, String name) throws Exception {
        JSONObject q = validatePairQr(qr);
        name = name.replaceAll("[\\p{Cntrl}]", " ").trim(); if (name.isEmpty()) name = "Android"; if (name.length() > 64) name = name.substring(0,64);
        JSONObject body = new JSONObject().put("code", q.getString("code")).put("clientId", clientId).put("name", name);
        JSONObject result = request(q.getString("url"), q.getString("pin"), null, "POST", "/v1/pair", body);
        if (!result.getString("clientId").equalsIgnoreCase(clientId) || !result.getString("token").matches("[A-Fa-f0-9]{64}")) throw new IllegalArgumentException("Komputer zwrócił nieprawidłowe parowanie.");
        if (!preferences.edit().putString("url", q.getString("url")).putString("pin", q.getString("pin").toUpperCase(Locale.ROOT)).putString("token", result.getString("token")).commit()) throw new java.io.IOException("Nie zapisano parowania na telefonie.");
    }
    JSONObject call(String method, String path, JSONObject body) throws Exception {
        if (!paired()) throw new IllegalArgumentException("Najpierw połącz telefon z komputerem kodem QR.");
        return request(preferences.getString("url", ""), preferences.getString("pin", ""), preferences.getString("token", ""), method, path, body);
    }
    static final class PinMismatchException extends CertificateException {
        PinMismatchException(String detail) { super(detail); }
    }
    private static String hex(byte[] bytes) {
        StringBuilder result = new StringBuilder();
        for (byte value : bytes) result.append(String.format(Locale.ROOT,"%02X",value & 255));
        return result.toString();
    }
    static String tlsError(Exception error) {
        if (!(error instanceof javax.net.ssl.SSLException)) return null;
        Throwable cause = error;
        for (int i=0; cause != null && i<12; i++,cause=cause.getCause()) {
            if (cause instanceof PinMismatchException)
                return "Komputer zwrócił inny certyfikat niż zapisany w QR. " + cause.getMessage() + ". Sprawdź, czy działa jedna instancja programu i wybrano adres właściwego komputera.";
            if (cause instanceof java.security.cert.CertificateNotYetValidException)
                return "Certyfikat komputera nie jest jeszcze ważny według zegara telefonu. Sprawdź datę i godzinę Windows oraz Androida. Szczegóły: " + cause.getMessage();
            if (cause instanceof java.security.cert.CertificateExpiredException)
                return "Certyfikat komputera wygasł według zegara telefonu. Sprawdź datę i godzinę Windows oraz Androida. Szczegóły: " + cause.getMessage();
        }
        StringBuilder detail = new StringBuilder(); cause = error;
        for (int i=0; cause != null && i<5; i++,cause=cause.getCause()) {
            if (i>0) detail.append("\n");
            detail.append(cause.getClass().getSimpleName()).append(": ");
            String message = cause.getMessage();
            if (message != null) detail.append(message.substring(0,Math.min(message.length(),500)));
        }
        return "Nie udało się uzgodnić szyfrowanego połączenia z komputerem. To nie musi być niezgodność certyfikatu.\n\nSzczegóły błędu:\n" + detail;
    }
    private static byte[] fingerprint(X509Certificate c) throws Exception { return MessageDigest.getInstance("SHA-256").digest(c.getEncoded()); }
    private static byte[] unhex(String value) { byte[] b = new byte[value.length()/2]; for (int i = 0; i < b.length; i++) b[i] = (byte)Integer.parseInt(value.substring(i*2, i*2+2),16); return b; }
    private static JSONObject request(String base, String pin, String token, String method, String path, JSONObject body) throws Exception {
        byte[] expected = unhex(pin);
        SSLContext context = SSLContext.getInstance("TLS");
        context.init(null, new TrustManager[]{new X509TrustManager() {
            @Override public X509Certificate[] getAcceptedIssuers() { return new X509Certificate[0]; }
            @Override public void checkClientTrusted(X509Certificate[] chain, String authType) throws CertificateException { throw new CertificateException("Nieobsługiwane"); }
            @Override public void checkServerTrusted(X509Certificate[] chain, String authType) throws CertificateException {
                if (chain == null || chain.length == 0) throw new CertificateException("Komputer nie przesłał certyfikatu.");
                byte[] actual;
                try { actual = fingerprint(chain[0]); }
                catch (Exception e) { throw new CertificateException("Nie można odczytać certyfikatu komputera.", e); }
                if (!MessageDigest.isEqual(expected, actual)) {
                    throw new PinMismatchException("QR: " + pin.substring(0,12).toUpperCase(Locale.ROOT) + ", komputer: " + hex(actual).substring(0,12));
                }
                chain[0].checkValidity();
            }
        }}, new SecureRandom());
        HttpsURLConnection connection = (HttpsURLConnection)new URL(base + path).openConnection();
        connection.setSSLSocketFactory(context.getSocketFactory());
        connection.setHostnameVerifier((hostname, session) -> { try { return MessageDigest.isEqual(expected, fingerprint((X509Certificate)session.getPeerCertificates()[0])); } catch (Exception e) { return false; } });
        connection.setConnectTimeout(5000); connection.setReadTimeout(12000); connection.setInstanceFollowRedirects(false); connection.setRequestMethod(method);
        connection.setRequestProperty("Accept", "application/json");
        if (token != null) connection.setRequestProperty("Authorization", "Bearer " + token);
        try {
            if (body != null) {
                byte[] data = body.toString().getBytes(StandardCharsets.UTF_8); connection.setDoOutput(true); connection.setRequestProperty("Content-Type", "application/json; charset=utf-8"); connection.setFixedLengthStreamingMode(data.length);
                try (java.io.OutputStream out = connection.getOutputStream()) { out.write(data); }
            }
            int status = connection.getResponseCode(); InputStream input = status >= 400 ? connection.getErrorStream() : connection.getInputStream();
            if (input == null) throw new Failure(status, "Komputer nie zwrócił danych.");
            ByteArrayOutputStream bytes = new ByteArrayOutputStream();
            try (input) { byte[] block = new byte[8192]; int read; while ((read = input.read(block)) != -1) { if (bytes.size() + read > 2097152) throw new java.io.IOException("Za duża odpowiedź komputera."); bytes.write(block, 0, read); } }
            JSONObject result = new JSONObject(bytes.toString("UTF-8"));
            if (status != 200) throw new Failure(status, result.optString("error", "Błąd połączenia z komputerem."));
            return result;
        } finally { connection.disconnect(); }
    }
}
