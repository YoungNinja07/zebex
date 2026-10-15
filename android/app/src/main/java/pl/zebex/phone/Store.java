package pl.zebex.phone;

import android.content.ContentValues;
import android.content.Context;
import android.database.Cursor;
import android.database.sqlite.SQLiteDatabase;
import android.database.sqlite.SQLiteOpenHelper;
import org.json.JSONArray;
import org.json.JSONObject;
import java.math.BigDecimal;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.UUID;

final class Store extends SQLiteOpenHelper {
    private static Store instance;
    static synchronized Store get(Context context) { if (instance == null) instance = new Store(context.getApplicationContext()); return instance; }
    private Store(Context context) { super(context, "collector.db", null, 1); setWriteAheadLoggingEnabled(true); }
    @Override public void onCreate(SQLiteDatabase db) {
        db.execSQL("CREATE TABLE documents(id TEXT PRIMARY KEY,name TEXT NOT NULL,mode TEXT NOT NULL,frozen INTEGER NOT NULL DEFAULT 0,status TEXT NOT NULL DEFAULT 'draft')");
        db.execSQL("CREATE TABLE scans(id INTEGER PRIMARY KEY AUTOINCREMENT,document_id TEXT NOT NULL,code TEXT NOT NULL,quantity TEXT NOT NULL)");
        db.execSQL("CREATE INDEX scans_document ON scans(document_id,id)");
        db.execSQL("CREATE TABLE products(code TEXT PRIMARY KEY,name TEXT,stock TEXT,price TEXT)");
        db.execSQL("CREATE TABLE products_stage(code TEXT PRIMARY KEY,name TEXT,stock TEXT,price TEXT)");
    }
    @Override public void onUpgrade(SQLiteDatabase db, int oldVersion, int newVersion) { throw new IllegalStateException("Nieobsługiwana aktualizacja bazy. Dane pozostają na telefonie."); }
    String create(String name, String mode) {
        String id = UUID.randomUUID().toString(); ContentValues values = new ContentValues(); values.put("id", id); values.put("name", name); values.put("mode", mode);
        getWritableDatabase().insertOrThrow("documents", null, values); return id;
    }
    JSONObject document(String id) throws Exception {
        try (Cursor c = getReadableDatabase().rawQuery("SELECT id,name,mode,frozen,status FROM documents WHERE id=?", new String[]{id})) {
            if (!c.moveToFirst()) return null;
            return new JSONObject().put("id", c.getString(0)).put("name", c.getString(1)).put("mode", c.getString(2)).put("frozen", c.getInt(3) == 1).put("status", c.getString(4));
        }
    }
    List<JSONObject> documents() throws Exception {
        List<JSONObject> result = new ArrayList<>();
        try (Cursor c = getReadableDatabase().rawQuery("SELECT id FROM documents ORDER BY rowid DESC", null)) { while (c.moveToNext()) result.add(document(c.getString(0))); }
        return result;
    }
    JSONArray scans(String id) throws Exception {
        JSONArray rows = new JSONArray();
        try (Cursor c = getReadableDatabase().rawQuery("SELECT code,quantity FROM scans WHERE document_id=? ORDER BY id", new String[]{id})) {
            while (c.moveToNext()) rows.put(new JSONObject().put("code", c.getString(0)).put("quantity", c.getString(1)));
        }
        return rows;
    }
    void add(String id, String code, String quantity) throws Exception {
        SQLiteDatabase db = getWritableDatabase(); db.beginTransaction();
        try {
            JSONObject d = document(id); if (d == null || d.getBoolean("frozen")) throw new IllegalArgumentException("Dokument jest zamknięty. Utwórz nowy.");
            try (Cursor c = db.rawQuery("SELECT COUNT(*) FROM scans WHERE document_id=?", new String[]{id})) { c.moveToFirst(); if (c.getInt(0) >= 10000) throw new IllegalArgumentException("Limit 10000 odczytów. Utwórz nowy dokument."); }
            ContentValues values = new ContentValues(); values.put("document_id", id); values.put("code", code); values.put("quantity", quantity); db.insertOrThrow("scans", null, values); db.setTransactionSuccessful();
        } finally { db.endTransaction(); }
    }
    void removeLast(String id) throws Exception {
        if (document(id).getBoolean("frozen")) throw new IllegalArgumentException("Dokument jest zamknięty.");
        getWritableDatabase().execSQL("DELETE FROM scans WHERE id=(SELECT MAX(id) FROM scans WHERE document_id=?)", new Object[]{id});
    }
    JSONObject freeze(String id) throws Exception {
        SQLiteDatabase db = getWritableDatabase(); db.beginTransaction();
        try {
            JSONObject d = document(id); JSONArray rows = scans(id); if (rows.length() == 0) throw new IllegalArgumentException("Najpierw zeskanuj produkty.");
            ContentValues values = new ContentValues(); values.put("frozen", 1); if (!d.getBoolean("frozen")) values.put("status", "pending"); db.update("documents", values, "id=?", new String[]{id});
            JSONObject body = new JSONObject().put("id", id).put("name", d.getString("name")).put("mode", d.getString("mode")).put("rows", rows);
            db.setTransactionSuccessful(); return body;
        } finally { db.endTransaction(); }
    }
    void status(String id, String status) {
        if (!status.equals("queued") && !status.equals("suz") && !status.equals("removed")) throw new IllegalArgumentException("Nieznany status dokumentu.");
        ContentValues values = new ContentValues(); values.put("status", status); getWritableDatabase().update("documents", values, "id=?", new String[]{id});
    }
    BigDecimal sum(String id, String code) throws Exception {
        BigDecimal result = BigDecimal.ZERO;
        try (Cursor c = getReadableDatabase().rawQuery("SELECT quantity FROM scans WHERE document_id=? AND code=?", new String[]{id,code})) { while (c.moveToNext()) result = result.add(new BigDecimal(c.getString(0))); }
        return result;
    }
    int count(String id) { try (Cursor c = getReadableDatabase().rawQuery("SELECT COUNT(*) FROM scans WHERE document_id=?",new String[]{id})) { c.moveToFirst();return c.getInt(0); } }
    List<String> display(String id) throws Exception {
        JSONObject d = document(id); List<String> result = new ArrayList<>();
        LinkedHashMap<String, BigDecimal> sums = new LinkedHashMap<>();
        if (!d.getString("mode").equals("summed")) {
            try (Cursor c = getReadableDatabase().rawQuery("SELECT code,quantity FROM scans WHERE document_id=? ORDER BY id DESC LIMIT 200",new String[]{id})) { while(c.moveToNext())result.add(c.getString(0)+"  × "+c.getString(1)+productLabel(c.getString(0))); }
            return result;
        }
        try (Cursor c = getReadableDatabase().rawQuery("SELECT code,quantity FROM scans WHERE document_id=? ORDER BY id",new String[]{id})) {
            while(c.moveToNext()) { String code=c.getString(0);BigDecimal total=sums.getOrDefault(code,BigDecimal.ZERO).add(new BigDecimal(c.getString(1)));sums.remove(code);sums.put(code,total); }
        }
        List<String> codes=new ArrayList<>(sums.keySet());java.util.Collections.reverse(codes);
        for (String code : codes.subList(0,Math.min(200,codes.size()))) result.add(code + "  × " + sums.get(code).stripTrailingZeros().toPlainString() + productLabel(code));
        return result;
    }
    String productLabel(String code) {
        try (Cursor c = getReadableDatabase().rawQuery("SELECT name,stock,price FROM products WHERE code=?", new String[]{code})) {
            return c.moveToFirst() ? "\n" + c.getString(0) + " • stan " + c.getString(1) + " • cena " + c.getString(2) : "";
        }
    }
    void beginCatalog() { getWritableDatabase().delete("products_stage", null, null); }
    void stageProducts(JSONArray products) throws Exception {
        SQLiteDatabase db = getWritableDatabase(); db.beginTransaction();
        try {
            for (int i = 0; i < products.length(); i++) {
                JSONObject p = products.getJSONObject(i); ContentValues v = new ContentValues();
                v.put("code", p.getString("code")); v.put("name", p.getString("name")); v.put("stock", p.getString("stock")); v.put("price", p.getString("price")); db.insertOrThrow("products_stage", null, v);
            }
            db.setTransactionSuccessful();
        } finally { db.endTransaction(); }
    }
    void commitCatalog() {
        SQLiteDatabase db = getWritableDatabase(); db.beginTransaction();
        try { db.delete("products", null, null); db.execSQL("INSERT INTO products SELECT * FROM products_stage"); db.delete("products_stage", null, null); db.setTransactionSuccessful(); }
        finally { db.endTransaction(); }
    }
}
