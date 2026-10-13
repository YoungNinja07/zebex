using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Zebex.Core;

public enum ScanMode { Separate, Summed, Quick }
public enum DeleteAfterSending { Manual, Automatic, Never }
public enum ScanFeedback { None, Count, Data }
public sealed record Product(string Code, string Name, decimal Stock, decimal Price);
public sealed record DocumentSnapshot(string Name, ScanMode Mode, List<Scan> Rows);
public sealed record SessionSnapshot(int Version, List<DocumentSnapshot> Documents, List<Product> Products,
    DeleteAfterSending Deletion, ScanFeedback Feedback);

public sealed class CollectorDocument(string name, ScanMode mode)
{
    private readonly List<Scan> rows = [];
    public Guid DocumentId { get; } = Guid.NewGuid();
    public long Revision { get; private set; }
    public string Name { get; } = name;
    public ScanMode Mode { get; } = mode;
    public IReadOnlyList<Scan> Rows => rows.AsReadOnly();
    public void Add(string code, decimal quantity)
    {
        code = ValidateCode(code);
        if (Mode == ScanMode.Quick) quantity = 1;
        if (quantity == 0 || (Mode != ScanMode.Summed && quantity < 0))
            throw new ArgumentException("Wpisz dodatnią ilość; korekty ujemne są dostępne w Skanuj+ i dokumentach ze sklejaniem.");
        var index = Mode == ScanMode.Summed ? rows.FindIndex(s => s.Code == code) : -1;
        if (index < 0) rows.Add(new(code, quantity));
        else rows[index] = rows[index] with { Quantity = checked(rows[index].Quantity + quantity) };
        Revision++;
    }
    internal void Restore(IEnumerable<Scan> restored)
    {
        foreach (var row in restored)
        {
            var code = ValidateCode(row.Code);
            if (Mode != ScanMode.Summed && row.Quantity <= 0) throw new ArgumentException("Nieprawidłowa ilość w sesji.");
            if (Mode == ScanMode.Summed && rows.Any(s => s.Code == code)) throw new ArgumentException("Powtórzony kod w sumarycznym dokumencie sesji.");
            if (Mode == ScanMode.Quick && row.Quantity != 1) throw new ArgumentException("Szybki skan musi mieć ilość 1.");
            rows.Add(new(code, row.Quantity));
            Revision++;
        }
    }
    public void RemoveLast() { if (rows.Count > 0) { rows.RemoveAt(rows.Count - 1); Revision++; } }
    internal void ResetSums()
    {
        if (Mode != ScanMode.Summed || !rows.Any(r => r.Quantity != 0)) return;
        for (var i = 0; i < rows.Count; i++) rows[i] = rows[i] with { Quantity = 0 };
        Revision++;
    }
    public string Export(char separator)
    {
        if (separator is not (';' or '\t')) throw new ArgumentException("Nieobsługiwany separator.");
        var data = new StringBuilder("Kod" + separator + "Ilosc\r\n");
        foreach (var row in rows) data.Append(Escape(row.Code, separator)).Append(separator)
            .Append(row.Quantity.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        return data.ToString();
    }
    public static string ValidateCode(string code)
    {
        code = code.Trim();
        if (code.Length is 0 or > 128 || code.Any(char.IsControl)) throw new ArgumentException("Wpisz kod od 1 do 128 znaków, bez znaków sterujących.");
        return code;
    }
    internal static string Escape(string text, char separator) => text.IndexOfAny([separator, '"', '\r', '\n']) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
}

public sealed class CollectorSession
{
    private readonly List<CollectorDocument> documents = [];
    private Dictionary<string, Product> products = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, DocumentTransferSnapshot> pendingTransfers = [];
    private readonly Dictionary<Guid, CatalogTransferSnapshot> pendingCatalogs = [];
    private long catalogRevision;
    // Runtime identities intentionally do not survive JSON restore or a new application session.
    public Guid SessionId { get; } = Guid.NewGuid();
    public IReadOnlyList<CollectorDocument> Documents => documents.AsReadOnly();
    public IReadOnlyCollection<Product> Products => products.Values;
    public DeleteAfterSending Deletion { get; set; } = DeleteAfterSending.Manual;
    public ScanFeedback Feedback { get; set; } = ScanFeedback.Count;
    public CollectorDocument Open(string name, ScanMode mode)
    {
        name = name.Trim();
        if (name.Length is 0 or > 64 || name.Any(char.IsControl)) throw new ArgumentException("Nazwa dokumentu musi mieć od 1 do 64 znaków.");
        var existing = documents.FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            if (existing.Mode != mode) throw new ArgumentException("Dokument o tej nazwie już istnieje w innym trybie. Wybierz inną nazwę lub otwórz go w przeglądzie.");
            return existing;
        }
        var document = new CollectorDocument(name, mode); documents.Add(document); return document;
    }
    public Product? FindProduct(string code) => products.GetValueOrDefault(code);
    public void DeleteDocument(string name)
    {
        var removed = documents.Where(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(d => d.DocumentId).ToHashSet();
        documents.RemoveAll(d => removed.Contains(d.DocumentId));
        foreach (var token in pendingTransfers.Where(p => removed.Contains(p.Value.DocumentId)).Select(p => p.Key).ToArray()) pendingTransfers.Remove(token);
    }
    public void LoadProducts(IEnumerable<Product> source)
    {
        ReplaceProducts(ValidateProducts(source));
    }
    private static Dictionary<string, Product> ValidateProducts(IEnumerable<Product> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var next = new Dictionary<string, Product>(StringComparer.Ordinal);
        foreach (var product in source)
        {
            if (product == null) throw new ArgumentException("Nieprawidłowy produkt w katalogu.");
            var code = CollectorDocument.ValidateCode(product.Code);
            if (next.ContainsKey(code)) throw new ArgumentException("Powtórzony kod w katalogu produktów: " + code);
            if (product.Name == null || product.Name.Length > 256) throw new ArgumentException("Nieprawidłowa nazwa produktu.");
            next.Add(code, product with { Code = code });
        }
        return next;
    }
    private void ReplaceProducts(Dictionary<string, Product> next)
    {
        products = next;
        catalogRevision++;
        foreach (var doc in documents) doc.ResetSums();
    }
    public void DeleteProducts() => ReplaceProducts(new(StringComparer.Ordinal));

    /// <summary>Freeze the exact document for one pending transfer. Call only on the session's owner thread.</summary>
    public DocumentTransferSnapshot CaptureTransfer(string name)
    {
        var document = documents.FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Nie znaleziono dokumentu do wysłania.", nameof(name));
        if (document.Rows.Count == 0) throw new InvalidOperationException("Dokument do wysłania jest pusty.");
        foreach (var token in pendingTransfers.Where(p => p.Value.DocumentId == document.DocumentId).Select(p => p.Key).ToArray()) pendingTransfers.Remove(token);
        var snapshot = new DocumentTransferSnapshot(SessionId, document.DocumentId, document.Revision, document.Name, document.Mode, document.Rows, Deletion);
        pendingTransfers.Add(snapshot.TransferId, snapshot);
        return snapshot;
    }

    /// <summary>
    /// Finish a transfer only after the protocol engine verifies the receiver's complete acknowledgement.
    /// A terminal failure, cancellation, stale snapshot, or changed document never deletes data.
    /// </summary>
    public bool ConfirmVerifiedSend(DocumentTransferSnapshot snapshot, bool receiverAccepted, bool userConfirmedDeletion = false)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SessionId != SessionId || !pendingTransfers.TryGetValue(snapshot.TransferId, out var pending) || !ReferenceEquals(snapshot, pending)) return false;
        pendingTransfers.Remove(snapshot.TransferId);
        if (!receiverAccepted) return false;
        var document = documents.FirstOrDefault(d => d.DocumentId == snapshot.DocumentId);
        if (document == null || document.Revision != snapshot.Revision) return false;
        if (snapshot.DeletionPolicy == DeleteAfterSending.Automatic || (snapshot.DeletionPolicy == DeleteAfterSending.Manual && userConfirmedDeletion)) DeleteDocument(document.Name);
        return true;
    }

    public void CancelTransfer(DocumentTransferSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SessionId == SessionId && pendingTransfers.TryGetValue(snapshot.TransferId, out var pending) && ReferenceEquals(snapshot, pending)) pendingTransfers.Remove(snapshot.TransferId);
    }

    /// <summary>Capture at the start of reception, before incoming bytes can race with local changes.</summary>
    public CatalogTransferBaseline CaptureCatalogBaseline() => new(SessionId, catalogRevision,
        documents.Select(d => new DocumentRevision(d.DocumentId, d.Revision)));

    /// <summary>Validate and stage a complete catalog without changing products or recorded quantities.</summary>
    public CatalogTransferSnapshot StageCatalog(IEnumerable<Product> source) => StageCatalog(source, CaptureCatalogBaseline());

    /// <summary>Stage a completely decoded catalog using the baseline captured before reception began.</summary>
    public CatalogTransferSnapshot StageCatalog(IEnumerable<Product> source, CatalogTransferBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        if (baseline.SessionId != SessionId) throw new ArgumentException("Początek odbioru należy do innej sesji.", nameof(baseline));
        var validated = ValidateProducts(source);
        var snapshot = new CatalogTransferSnapshot(SessionId, baseline.CatalogRevision, validated.Values, baseline.DocumentRevisions);
        pendingCatalogs.Add(snapshot.TransferId, snapshot);
        return snapshot;
    }

    /// <summary>Commit only a verified complete catalog whose baseline still matches this session.</summary>
    public bool CommitVerifiedCatalog(CatalogTransferSnapshot snapshot, bool receiverAccepted)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SessionId != SessionId || !pendingCatalogs.TryGetValue(snapshot.TransferId, out var pending) || !ReferenceEquals(snapshot, pending)) return false;
        pendingCatalogs.Remove(snapshot.TransferId);
        if (!receiverAccepted || catalogRevision != snapshot.CatalogRevision || documents.Count != snapshot.DocumentRevisions.Count) return false;
        var baseline = snapshot.DocumentRevisions.ToDictionary(d => d.DocumentId, d => d.Revision);
        if (documents.Any(d => !baseline.TryGetValue(d.DocumentId, out var revision) || d.Revision != revision)) return false;
        ReplaceProducts(ValidateProducts(snapshot.Products));
        return true;
    }

    public void CancelCatalog(CatalogTransferSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SessionId == SessionId && pendingCatalogs.TryGetValue(snapshot.TransferId, out var pending) && ReferenceEquals(snapshot, pending)) pendingCatalogs.Remove(snapshot.TransferId);
    }
    // This is a local simulation of an acknowledgement, never a claimed SUZ transfer.
    public void ConfirmSimulatedSend(string name, bool userConfirmedDeletion)
    {
        if (Deletion == DeleteAfterSending.Automatic || (Deletion == DeleteAfterSending.Manual && userConfirmedDeletion)) DeleteDocument(name);
    }
    public string ToJson() => JsonSerializer.Serialize(new SessionSnapshot(1,
        documents.Select(d => new DocumentSnapshot(d.Name, d.Mode, d.Rows.ToList())).ToList(),
        products.Values.ToList(), Deletion, Feedback), new JsonSerializerOptions { WriteIndented = true });
    public static CollectorSession FromJson(string json)
    {
        // Keep sessions saved by the first public version readable.
        if (json.TrimStart().StartsWith('['))
        {
            var legacy = JsonSerializer.Deserialize<List<Scan>>(json) ?? throw new ArgumentException("Pusta sesja.");
            var restored = new CollectorSession();
            var doc = restored.Open("Skanuj+", ScanMode.Summed);
            foreach (var scan in legacy) doc.Add(scan.Code, scan.Quantity);
            return restored;
        }
        var snapshot = JsonSerializer.Deserialize<SessionSnapshot>(json) ?? throw new ArgumentException("Pusta sesja.");
        if (snapshot.Version != 1 || snapshot.Documents == null || snapshot.Products == null || !Enum.IsDefined(snapshot.Deletion) || !Enum.IsDefined(snapshot.Feedback)) throw new ArgumentException("Nieobsługiwany lub uszkodzony format sesji.");
        var result = new CollectorSession { Deletion = snapshot.Deletion, Feedback = snapshot.Feedback };
        result.LoadProducts(snapshot.Products);
        foreach (var doc in snapshot.Documents)
        {
            if (!Enum.IsDefined(doc.Mode) || doc.Rows == null || result.documents.Any(d => d.Name.Equals(doc.Name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Nieprawidłowy dokument w sesji.");
            result.Open(doc.Name, doc.Mode).Restore(doc.Rows);
        }
        return result;
    }
}

public static class ProductCsv
{
    public static List<Product> Parse(string text)
    {
        // Format local to the simulator; not an asserted SUZ/EKP exchange format.
        var lines = ReadCsv(text.TrimStart('\uFEFF'));
        if (lines.Count == 0 || !lines[0].SequenceEqual(new[] { "Kod", "Nazwa", "Stan", "Cena" }, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Katalog CSV musi mieć nagłówek Kod;Nazwa;Stan;Cena.");
        var products = new List<Product>();
        foreach (var fields in lines.Skip(1))
        {
            if (fields.Count == 1 && fields[0].Length == 0) continue;
            if (fields.Count != 4 || !decimal.TryParse(fields[2], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var stock)
                || !decimal.TryParse(fields[3], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var price))
                throw new ArgumentException("Błędny wiersz katalogu. Wartości Stan/Cena wpisuj z kropką dziesiętną.");
            products.Add(new(fields[0], fields[1], stock, price));
        }
        var validation = new CollectorSession(); validation.LoadProducts(products);
        return validation.Products.ToList();
    }

    private static List<List<string>> ReadCsv(string text)
    {
        var rows = new List<List<string>>(); var row = new List<string>(); var field = new StringBuilder();
        var quoted = false; var closedQuote = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') { quoted = false; closedQuote = true; }
                else field.Append(c);
            }
            else if (c == ';') { row.Add(field.ToString()); field.Clear(); closedQuote = false; }
            else if (c is '\r' or '\n')
            {
                row.Add(field.ToString()); rows.Add(row); row = []; field.Clear(); closedQuote = false;
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            }
            else if (c == '"' && field.Length == 0 && !closedQuote) quoted = true;
            else if (c == '"' || closedQuote) throw new ArgumentException("Nieprawidłowe cudzysłowy w katalogu CSV.");
            else field.Append(c);
        }
        if (quoted) throw new ArgumentException("Niezamknięty cudzysłów w katalogu CSV.");
        if (field.Length > 0 || row.Count > 0 || closedQuote) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }
}
