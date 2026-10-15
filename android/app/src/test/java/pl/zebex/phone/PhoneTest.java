package pl.zebex.phone;

import android.app.AlertDialog;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.EditText;
import android.widget.TextView;
import org.json.JSONObject;
import org.junit.Before;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.robolectric.Robolectric;
import org.robolectric.RobolectricTestRunner;
import org.robolectric.RuntimeEnvironment;
import org.robolectric.annotation.Config;
import org.robolectric.shadows.ShadowAlertDialog;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.UUID;
import static org.junit.Assert.*;

@RunWith(RobolectricTestRunner.class)
@Config(sdk = 28)
public class PhoneTest {
    private Store store;
    @Before public void clear() {
        Context context = RuntimeEnvironment.getApplication(); store = Store.get(context);
        for (String table : new String[]{"documents","scans","products","products_stage"}) store.getWritableDatabase().delete(table,null,null);
        context.getSharedPreferences("connection",Context.MODE_PRIVATE).edit().clear().commit();
    }
    private static void rejects(Runnable action) { try { action.run(); fail("Expected rejection"); } catch (IllegalArgumentException expected) { } }
    @Test public void exactDecimalQuantitiesAndCodesPersist() throws Exception {
        String id=store.create("Magazyn","summed");store.add(id,"000123","0.1");store.add(id,"000123","0.2");
        assertEquals("0.3",store.sum(id,"000123").toPlainString());assertEquals("000123",store.scans(id).getJSONObject(0).getString("code"));assertEquals(2,store.scans(id).length());
    }
    @Test public void frozenUploadRetriesHaveTheSameIdAndRows() throws Exception {
        String id=store.create("Magazyn","separate");store.add(id,"001","2.5");JSONObject first=store.freeze(id);JSONObject second=store.freeze(id);
        assertEquals(first.toString(),second.toString());assertTrue(store.document(id).getBoolean("frozen"));
        try{store.add(id,"002","1");fail();}catch(IllegalArgumentException expected){}assertEquals(1,store.scans(id).length());
    }
    @Test public void serverStatusNeverDeletesOfflineScans() throws Exception {
        String id=store.create("Magazyn","separate");store.add(id,"001","2");store.freeze(id);store.status(id,"queued");store.status(id,"suz");
        assertEquals("suz",store.document(id).getString("status"));assertEquals(1,store.scans(id).length());
    }
    @Test public void incompleteCatalogNeverReplacesVisibleProducts() throws Exception {
        store.beginCatalog();store.stageProducts(new org.json.JSONArray("[{\"code\":\"001\",\"name\":\"Old\",\"stock\":1,\"price\":2}]"));store.commitCatalog();
        store.beginCatalog();store.stageProducts(new org.json.JSONArray("[{\"code\":\"002\",\"name\":\"Partial\",\"stock\":3,\"price\":4}]"));
        assertTrue(store.productLabel("001").contains("Old"));assertEquals("",store.productLabel("002"));store.commitCatalog();assertEquals("",store.productLabel("001"));assertTrue(store.productLabel("002").contains("Partial"));
    }
    @Test public void quantityValidationMatchesCollectorModes() {
        assertEquals("2.5",MainActivity.quantityValue("2,50","summed"));assertEquals("-1",MainActivity.quantityValue("-1","summed"));
        rejects(()->MainActivity.quantityValue("0","summed"));rejects(()->MainActivity.quantityValue("-1","separate"));rejects(()->MainActivity.quantityValue("2","quick"));
        rejects(()->MainActivity.quantityValue("79228162514264337593543950336","summed"));rejects(()->MainActivity.quantityValue("0.00000000000000000000000000001","summed"));
    }
    @Test public void barcodeTextRejectsDataThatWouldBeCorruptedBySuz() {
        MainActivity.validateText("000123",128,"Kod");rejects(()->MainActivity.validateText("Zażółć",128,"Kod"));rejects(()->MainActivity.validateText("A\tB",128,"Kod"));rejects(()->MainActivity.validateText("A\"B",128,"Kod"));
    }
    @Test public void pairingQrRequiresLocalHttpsAndPinnedCertificate() throws Exception {
        JSONObject valid=new JSONObject().put("v",1).put("url","https://192.168.1.20:18790").put("pin","A".repeat(64)).put("code","123456");assertEquals("123456",Link.validatePairQr(valid.toString()).getString("code"));
        for(String bad:new String[]{"http://192.168.1.20:18790","https://example.com:18790","https://8.8.8.8:18790","https://192.168.1.20:80","https://192.168.1.20:18790/path","https://user@192.168.1.20:18790"}){
            JSONObject q=new JSONObject(valid.toString()).put("url",bad);try{Link.validatePairQr(q.toString());fail(bad);}catch(IllegalArgumentException expected){}
        }
    }
    private static View find(View view,String text) {
        if(view instanceof TextView && text.contentEquals(((TextView)view).getText()))return view;
        if(view instanceof ViewGroup)for(int i=0;i<((ViewGroup)view).getChildCount();i++){View r=find(((ViewGroup)view).getChildAt(i),text);if(r!=null)return r;}return null;
    }
    private static EditText input(View view){if(view instanceof EditText)return (EditText)view;if(view instanceof ViewGroup)for(int i=0;i<((ViewGroup)view).getChildCount();i++){EditText r=input(((ViewGroup)view).getChildAt(i));if(r!=null)return r;}return null;}
    @Test public void activityLaunchesAndManualScanIsSavedBeforeLeavingScreen() throws Exception {
        var controller=Robolectric.buildActivity(MainActivity.class).setup();MainActivity activity=controller.get();View root=activity.getWindow().getDecorView();
        assertNotNull(find(root,"Zebex Phone"));input(root).setText("000123");find(root,"Dodaj").performClick();
        org.robolectric.shadows.ShadowLooper.idleMainLooper();AlertDialog dialog=ShadowAlertDialog.getLatestAlertDialog();input(dialog.getWindow().getDecorView()).setText("2,5");dialog.getButton(AlertDialog.BUTTON_POSITIVE).performClick();
        String id=activity.getSharedPreferences("connection",Context.MODE_PRIVATE).getString("current","");assertEquals("2.5",store.scans(id).getJSONObject(0).getString("quantity"));controller.pause().stop().destroy();
    }
    @Test public void cameraResultUsesSameDurableScanPath() throws Exception {
        var controller=Robolectric.buildActivity(MainActivity.class).setup();MainActivity activity=controller.get();
        activity.onActivityResult(51002,MainActivity.RESULT_OK,new Intent().putExtra("SCAN_RESULT","000456"));org.robolectric.shadows.ShadowLooper.idleMainLooper();AlertDialog dialog=ShadowAlertDialog.getLatestAlertDialog();dialog.getButton(AlertDialog.BUTTON_POSITIVE).performClick();
        String id=activity.getSharedPreferences("connection",Context.MODE_PRIVATE).getString("current","");assertEquals("000456",store.scans(id).getJSONObject(0).getString("code"));controller.pause().stop().destroy();
    }
    @Test public void androidClientReachesTlsGatewayAndSerialSuzBridge() throws Exception {
        String fixture=System.getProperty("zebex.fixture");org.junit.Assume.assumeNotNull(fixture);
        JSONObject qr=new JSONObject(new String(Files.readAllBytes(java.nio.file.Paths.get(fixture)),java.nio.charset.StandardCharsets.UTF_8));SharedPreferences prefs=RuntimeEnvironment.getApplication().getSharedPreferences("integration",Context.MODE_PRIVATE);prefs.edit().clear().commit();Link link=new Link(prefs);
        String client=UUID.randomUUID().toString();link.pair(qr.toString(),client,"Android integration");assertTrue(link.paired());
        JSONObject products=link.call("GET","/v1/products?offset=0&limit=500",null);store.beginCatalog();store.stageProducts(products.getJSONArray("products"));store.commitCatalog();assertTrue(store.productLabel("000123").contains("Test produktu"));
        String id=store.create("Android Test","summed");store.add(id,"000123","0.1");store.add(id,"000123","0.2");JSONObject upload=store.freeze(id);
        JSONObject accepted=link.call("POST","/v1/documents",upload);assertEquals("queued",accepted.getString("status"));assertEquals(accepted.getString("id"),link.call("POST","/v1/documents",upload).getString("id"));
        Process bridge=new ProcessBuilder(System.getProperty("ZEBEX_TEST_DOTNET"),System.getProperty("ZEBEX_BRIDGE_TEST_DLL"),System.getProperty("ZEBEX_BRIDGE_TEST_PORT"),"115200","3","0","1",System.getProperty("ZEBEX_TEST_DOCUMENT")).redirectErrorStream(true).start();
        String report=new String(bridge.getInputStream().readAllBytes(),java.nio.charset.StandardCharsets.UTF_8);assertEquals(report,0,bridge.waitFor());assertTrue(report.contains("Receive data completed"));
        assertTrue(new String(Files.readAllBytes(java.nio.file.Paths.get(System.getProperty("ZEBEX_TEST_DOCUMENT"))),java.nio.charset.StandardCharsets.UTF_8).contains("000123\t0.3"));
        JSONObject receipt=link.call("GET","/v1/documents/"+id,null);assertEquals("suz",receipt.getString("status"));store.status(id,receipt.getString("status"));assertEquals(2,store.scans(id).length());
        assertEquals("suz",link.call("POST","/v1/documents",upload).getString("status"));
        JSONObject bad=new JSONObject(qr.toString()).put("pin","0".repeat(64));try{new Link(prefs).pair(bad.toString(),client,"Bad pin");fail("Wrong TLS pin accepted");}catch(javax.net.ssl.SSLException expected){}
    }
}
