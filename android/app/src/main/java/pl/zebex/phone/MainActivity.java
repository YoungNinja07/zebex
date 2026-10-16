package pl.zebex.phone;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.Intent;
import android.content.SharedPreferences;
import android.graphics.Color;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.InputFilter;
import android.text.InputType;
import android.view.View;
import android.view.ViewGroup;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ListView;
import android.widget.Spinner;
import android.widget.TextView;
import android.widget.Toast;
import com.google.zxing.integration.android.IntentIntegrator;
import org.json.JSONArray;
import org.json.JSONObject;
import java.math.BigDecimal;
import java.net.URLEncoder;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.List;
import java.util.Locale;
import java.util.UUID;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public final class MainActivity extends Activity {
    private static final int PAIR_SCAN = 51001, PRODUCT_SCAN = 51002;
    private static final BigDecimal MAX = new BigDecimal("79228162514264337593543950335");
    private Store store; private SharedPreferences preferences; private Link link;
    private String current; private TextView connection, title, status; private EditText code; private Button send, scan, undo;
    private ListView list; private boolean busy, visible, polling;
    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Runnable poll = new Runnable() { @Override public void run() { if (!visible) return; pollStatus(); handler.postDelayed(this, 7000); } };
    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        store = Store.get(this); preferences = getSharedPreferences("connection", MODE_PRIVATE); link = new Link(preferences);
        if (!preferences.contains("clientId")) preferences.edit().putString("clientId", UUID.randomUUID().toString()).commit();
        current = preferences.getString("current", null);
        try { if (current == null || store.document(current) == null) select(store.create(defaultName(), "summed")); }
        catch (Exception e) { fail(e); }
        LinearLayout root = new LinearLayout(this); root.setOrientation(LinearLayout.VERTICAL); root.setPadding(dp(14), dp(8), dp(14), dp(8)); root.setBackgroundColor(Color.rgb(244,247,246)); root.setFitsSystemWindows(true);
        TextView heading = text("Zebex Phone", 25); heading.setTextColor(Color.rgb(8,127,91)); root.addView(heading);
        connection = text("", 13); root.addView(connection);
        LinearLayout tools = row(); tools.addView(button("Połącz", () -> camera(true)), weight()); tools.addView(button("Produkty", this::downloadProducts), weight()); tools.addView(button("Dokumenty", this::chooseDocument), weight()); tools.addView(button("Nowy", this::newDocument), weight()); root.addView(tools);
        title = text("", 19); root.addView(title);
        LinearLayout manual = row(); code = new EditText(this); code.setHint("Kod kreskowy"); code.setSingleLine(true); code.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS); code.setFilters(new InputFilter[]{new InputFilter.LengthFilter(128)});
        manual.addView(code, weight()); manual.addView(button("Dodaj", () -> quantity(code.getText().toString())), new LinearLayout.LayoutParams(-2,-2)); root.addView(manual);
        scan = button("SKANUJ APARATEM", () -> camera(false)); scan.setTextSize(18); scan.setTextColor(Color.WHITE); scan.setBackgroundTintList(android.content.res.ColorStateList.valueOf(Color.rgb(8,127,91))); root.addView(scan, new LinearLayout.LayoutParams(-1, dp(58)));
        status = text("", 14); status.setPadding(0, dp(6),0,dp(4)); root.addView(status);
        LinearLayout actions = row(); send = button("Wyślij / ponów", this::upload); undo = button("Cofnij odczyt", () -> { store.removeLast(current); render(); }); actions.addView(send, weight()); actions.addView(undo, weight()); root.addView(actions);
        list = new ListView(this); list.setDividerHeight(dp(1)); root.addView(list, new LinearLayout.LayoutParams(-1,0,1));
        setContentView(root); guard(this::render);
    }
    private int dp(int value) { return Math.round(value * getResources().getDisplayMetrics().density); }
    private TextView text(String value, int size) { TextView t = new TextView(this); t.setText(value); t.setTextSize(size); t.setTextColor(Color.rgb(25,42,37)); return t; }
    private LinearLayout row() { LinearLayout r = new LinearLayout(this); r.setOrientation(LinearLayout.HORIZONTAL); return r; }
    private LinearLayout.LayoutParams weight() { return new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1); }
    @FunctionalInterface private interface Work { void run() throws Exception; }
    private Button button(String label, Work action) { Button b = new Button(this); b.setText(label); b.setTextSize(12); b.setAllCaps(false); b.setMinWidth(0); b.setPadding(dp(3),0,dp(3),0); b.setOnClickListener(v -> guard(action)); return b; }
    private void guard(Work action) { try { action.run(); } catch (Exception e) { fail(e); } }
    private void fail(Exception e) { if (isDestroyed()) return; String message = friendly(e); if (status != null) status.setText(message); new AlertDialog.Builder(this).setTitle("Nie zakończono operacji").setMessage(message).setPositiveButton("OK", null).show(); }
    private static String friendly(Exception e) {
        String tls = Link.tlsError(e); if (tls != null) return tls;
        if (e instanceof java.net.SocketTimeoutException || e instanceof java.net.ConnectException || e instanceof java.net.NoRouteToHostException) return "Nie ma połączenia z komputerem. Sprawdź wspólne Wi-Fi, włączony program i zaporę Windows. Odczyty pozostają na telefonie.";
        return e.getMessage() == null ? "Nie udało się wykonać operacji. Dane pozostają na telefonie." : e.getMessage();
    }
    private static String defaultName() { return "Telefon " + new SimpleDateFormat("yyyy-MM-dd HHmmss", Locale.ROOT).format(new Date()); }
    private void select(String id) { current = id; preferences.edit().putString("current", id).commit(); }
    private void render() throws Exception {
        if (title == null) return;
        JSONObject d = store.document(current); if (d == null) return;
        boolean frozen = d.getBoolean("frozen"); int count = store.count(current);
        String mode = d.getString("mode"); title.setText(d.getString("name") + "\n" + (mode.equals("summed") ? "Sumowanie" : mode.equals("quick") ? "Szybki skan" : "Osobne pozycje") + " • odczyty: " + count);
        connection.setText(link.paired() ? "Sparowany komputer • dane przesyłane przez Wi-Fi" : "Skanowanie offline • połącz komputer kodem QR");
        status.setText(statusText(d.getString("status")));
        send.setEnabled(!busy && count > 0 && !d.getString("status").equals("suz") && !d.getString("status").equals("removed"));
        scan.setEnabled(!frozen); code.setEnabled(!frozen); undo.setEnabled(!frozen && count > 0);
        list.setAdapter(new ArrayAdapter<>(this, android.R.layout.simple_list_item_1, store.display(current)));
    }
    private static String statusText(String value) {
        switch (value) {
            case "pending": return "Dokument zamknięty do wysyłki. Ponów po połączeniu Wi-Fi; kopia pozostaje na telefonie.";
            case "queued": return "Przyjęte na komputerze. W nexo uruchom odbiór danych z kolektora. Kopia pozostaje tutaj.";
            case "suz": return "Odebrane przez SUZ. Sprawdź wynik importu w nexo. Odczyty zachowano na telefonie.";
            case "removed": return "Dokument usunięto na komputerze. Odczyty pozostają na telefonie; sprawdź stan w nexo.";
            default: return "Skany zapisują się na telefonie, także bez Wi-Fi. Po wysłaniu dokumentu nowe skany zapisuj w nowym dokumencie.";
        }
    }
    private void newDocument() {
        LinearLayout box = new LinearLayout(this); box.setOrientation(LinearLayout.VERTICAL); box.setPadding(dp(20),dp(6),dp(20),0);
        EditText name = new EditText(this); name.setText(defaultName()); name.setSingleLine(true); name.setFilters(new InputFilter[]{new InputFilter.LengthFilter(40)}); box.addView(name);
        Spinner modes = new Spinner(this); modes.setAdapter(new ArrayAdapter<>(this, android.R.layout.simple_spinner_dropdown_item, new String[]{"Sumuj powtarzające się kody", "Każdy odczyt osobno", "Szybki skan — zawsze 1"})); box.addView(modes);
        AlertDialog dialog = new AlertDialog.Builder(this).setTitle("Nowy dokument").setView(box).setNegativeButton("Anuluj",null).setPositiveButton("Utwórz",null).create();
        dialog.setOnShowListener(v -> dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(w -> guard(() -> {
            String value = name.getText().toString().trim(); validateText(value,40,"Nazwa"); String mode = new String[]{"summed","separate","quick"}[modes.getSelectedItemPosition()]; select(store.create(value,mode)); code.setText(""); render(); dialog.dismiss();
        }))); dialog.show();
    }
    private void chooseDocument() throws Exception {
        List<JSONObject> docs = store.documents(); String[] labels = new String[docs.size()];
        for (int i = 0; i < docs.size(); i++) labels[i] = docs.get(i).getString("name") + " • " + docs.get(i).getString("status");
        new AlertDialog.Builder(this).setTitle("Dokumenty na telefonie").setItems(labels,(d, index) -> guard(() -> { select(docs.get(index).getString("id")); render(); })).setNegativeButton("Zamknij",null).show();
    }
    private void camera(boolean pair) throws Exception {
        if (!pair && store.document(current).getBoolean("frozen")) throw new IllegalArgumentException("Utwórz nowy dokument, aby skanować kolejne produkty.");
        IntentIntegrator scanner = new IntentIntegrator(this); scanner.setRequestCode(pair ? PAIR_SCAN : PRODUCT_SCAN); scanner.setOrientationLocked(false); scanner.setBeepEnabled(true); scanner.setBarcodeImageEnabled(false);
        if (pair) scanner.setDesiredBarcodeFormats(java.util.Collections.singletonList("QR_CODE"));
        else scanner.setDesiredBarcodeFormats(IntentIntegrator.ALL_CODE_TYPES);
        scanner.setPrompt(pair ? "Zeskanuj QR z zakładki Telefon Android / Wi-Fi na komputerze" : "Zeskanuj kod produktu"); scanner.initiateScan();
    }
    @Override protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode,resultCode,data);
        if (requestCode != PAIR_SCAN && requestCode != PRODUCT_SCAN) return;
        if (resultCode != RESULT_OK || data == null || data.getStringExtra("SCAN_RESULT") == null) return;
        String result = data.getStringExtra("SCAN_RESULT");
        guard(() -> {
            if (requestCode == PRODUCT_SCAN) quantity(result);
            else network(() -> { link.pair(result, preferences.getString("clientId", ""), android.os.Build.MANUFACTURER + " " + android.os.Build.MODEL); return "Telefon połączony z komputerem."; });
        });
    }
    static void validateText(String value, int max, String label) {
        if (value.isEmpty() || value.length() > max || !value.equals(value.trim())) throw new IllegalArgumentException(label + ": nieprawidłowa długość lub spacje na końcach.");
        for (int i = 0; i < value.length(); i++) if (value.charAt(i) < 32 || value.charAt(i) > 126 || value.charAt(i) == '"') throw new IllegalArgumentException(label + ": wymagane znaki ASCII bez cudzysłowów.");
    }
    static String quantityValue(String value, String mode) {
        BigDecimal quantity;
        try { quantity = new BigDecimal(value.trim().replace(',','.')); } catch (Exception e) { throw new IllegalArgumentException("Wpisz ilość, np. 1 lub 2,5."); }
        quantity = quantity.stripTrailingZeros();
        if (quantity.signum() == 0 || (quantity.signum() < 0 && !mode.equals("summed")) || quantity.abs().compareTo(MAX) > 0 || quantity.unscaledValue().abs().compareTo(MAX.toBigIntegerExact()) > 0 || quantity.scale() > 28 || (mode.equals("quick") && quantity.compareTo(BigDecimal.ONE) != 0)) throw new IllegalArgumentException("Nieprawidłowa ilość lub za dużo cyfr. Korekty ujemne są dostępne przy sumowaniu.");
        return quantity.toPlainString();
    }
    private void quantity(String raw) throws Exception {
        String value = raw.trim(); validateText(value,128,"Kod"); JSONObject document = store.document(current);
        if (document.getBoolean("frozen")) throw new IllegalArgumentException("Utwórz nowy dokument.");
        String id = current, mode = document.getString("mode");
        if (mode.equals("quick")) { saveScan(id,value,"1",mode); return; }
        EditText input = new EditText(this); input.setInputType(InputType.TYPE_CLASS_NUMBER | InputType.TYPE_NUMBER_FLAG_DECIMAL | InputType.TYPE_NUMBER_FLAG_SIGNED); input.setText("1"); input.selectAll();
        AlertDialog dialog = new AlertDialog.Builder(this).setTitle(value + store.productLabel(value)).setView(input).setNegativeButton("Anuluj",null).setPositiveButton("Zapisz",null).create();
        dialog.setOnShowListener(v -> dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(w -> guard(() -> { saveScan(id,value,quantityValue(input.getText().toString(),mode),mode); dialog.dismiss(); })));
        dialog.show(); input.requestFocus(); dialog.getWindow().setSoftInputMode(android.view.WindowManager.LayoutParams.SOFT_INPUT_STATE_ALWAYS_VISIBLE);
    }
    private void saveScan(String id, String value, String quantity, String mode) throws Exception {
        if (mode.equals("summed")) {
            BigDecimal sum = store.sum(id,value).add(new BigDecimal(quantity)).stripTrailingZeros();
            if (sum.abs().compareTo(MAX) > 0 || sum.unscaledValue().abs().compareTo(MAX.toBigIntegerExact()) > 0 || sum.scale() > 28) throw new IllegalArgumentException("Suma ilości jest za duża lub wymagałaby zaokrąglenia.");
        }
        store.add(id,value,quantity); code.setText(""); render();
    }
    private void upload() throws Exception {
        if (!link.paired()) throw new IllegalArgumentException("Najpierw połącz komputer kodem QR. Skany pozostają zapisane.");
        String id = current; JSONObject document = store.freeze(id); render();
        network(() -> { JSONObject receipt = link.call("POST","/v1/documents",document); store.status(id,receipt.getString("status")); return "Dokument przyjęty na komputerze. W nexo uruchom odbiór z kolektora."; });
    }
    @FunctionalInterface private interface NetworkWork { String run() throws Exception; }
    private void network(NetworkWork action) throws Exception {
        if (busy) throw new IllegalArgumentException("Poczekaj na zakończenie połączenia.");
        busy = true; render(); status.setText("Łączenie z komputerem…");
        worker.execute(() -> {
            String message; Exception failure = null;
            try { message = action.run(); } catch (Exception e) { message = friendly(e); failure = e; if (e instanceof Link.Failure && ((Link.Failure)e).status == 401) preferences.edit().remove("token").commit(); }
            String result = message; Exception error = failure;
            runOnUiThread(() -> { if (isDestroyed()) return; busy = false; guard(this::render); status.setText(result); if (error != null) fail(error); else Toast.makeText(this,result,Toast.LENGTH_LONG).show(); });
        });
    }
    private void downloadProducts() throws Exception {
        network(() -> {
            store.beginCatalog(); int offset = 0, total = 0; String revision = null;
            while (true) {
                String path = "/v1/products?offset=" + offset + "&limit=500" + (revision == null ? "" : "&revision=" + URLEncoder.encode(revision,"UTF-8"));
                JSONObject page = link.call("GET",path,null); if (revision == null) revision = page.getString("revision");
                if (!revision.equals(page.getString("revision")) || page.getInt("offset") != offset || page.getInt("total") > 100000) throw new IllegalArgumentException("Katalog zmienił się lub jest za duży. Pobierz go ponownie.");
                JSONArray products = page.getJSONArray("products"); store.stageProducts(products); offset += products.length(); total = page.getInt("total");
                if (!page.getBoolean("more")) break;
                if (products.length() == 0) throw new IllegalArgumentException("Niepełny katalog.");
            }
            if (offset != total) throw new IllegalArgumentException("Niepełny katalog. Poprzednie produkty zachowano.");
            store.commitCatalog(); return "Pobrano produkty: " + total + ". Skany na telefonie zachowano.";
        });
    }
    private void pollStatus() {
        if (busy || polling || !link.paired()) return;
        final String id = current;
        try { JSONObject d = store.document(id); if (!d.getBoolean("frozen") || d.getString("status").equals("suz") || d.getString("status").equals("removed")) return; }
        catch (Exception e) { return; }
        polling = true;
        worker.execute(() -> {
            try { JSONObject result = link.call("GET","/v1/documents/" + id,null); store.status(id,result.getString("status")); }
            catch (Exception e) { if (e instanceof Link.Failure && ((Link.Failure)e).status == 401) preferences.edit().remove("token").commit(); }
            runOnUiThread(() -> { polling = false; if (!isDestroyed() && id.equals(current)) guard(this::render); });
        });
    }
    @Override protected void onResume() { super.onResume(); visible = true; handler.postDelayed(poll,3000); }
    @Override protected void onPause() { super.onPause(); visible = false; handler.removeCallbacks(poll); }
    @Override protected void onDestroy() { handler.removeCallbacksAndMessages(null); worker.shutdownNow(); super.onDestroy(); }
}
