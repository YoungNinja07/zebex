using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Zebex.Core;

public sealed record PhoneScan(string Code, string Quantity);
public sealed record PhoneUpload(Guid Id, string Name, string Mode, List<PhoneScan> Rows);
public sealed record PhoneReceipt(Guid Id, Guid ClientId, string Hash, string DocumentName, string Status, DateTime CreatedAtUtc);

public sealed partial class CollectorSession
{
    private readonly Dictionary<Guid, PhoneReceipt> phoneReceipts = [];
    public IReadOnlyCollection<PhoneReceipt> PhoneReceipts => phoneReceipts.Values;
    public PhoneReceipt? GetPhoneReceipt(Guid id, Guid clientId) => phoneReceipts.GetValueOrDefault(id) is { } r && r.ClientId == clientId ? r : null;
    public PhoneReceipt ImportPhoneDocument(Guid clientId, PhoneUpload upload, Action<string>? persistBeforeAccept = null)
    {
        if (clientId == Guid.Empty || upload.Id == Guid.Empty || upload.Rows is not { Count: > 0 and <= 10000 }) throw new ArgumentException("Dokument musi mieć od 1 do 10000 odczytów.");
        var name = upload.Name?.Trim() ?? "";
        if (name.Length is 0 or > 40 || name.Any(c => c < ' ' || c > '~' || c == '"')) throw new ArgumentException("Nazwa dokumentu: od 1 do 40 znaków ASCII, bez cudzysłowów.");
        var mode = upload.Mode switch { "separate" => ScanMode.Separate, "summed" => ScanMode.Summed, "quick" => ScanMode.Quick, _ => throw new ArgumentException("Nieznany tryb dokumentu.") };
        var normalized = new List<Scan>();
        foreach (var row in upload.Rows)
        {
            if (row == null || row.Code == null || row.Code != row.Code.Trim() || row.Code.Any(c => c < ' ' || c > '~' || c == '"')) throw new ArgumentException("Kod musi być tekstem ASCII bez cudzysłowów.");
            var code = CollectorDocument.ValidateCode(row.Code);
            var qty = PhoneQuantity.Parse(row.Quantity);
            if (qty == 0 || (mode != ScanMode.Summed && qty < 0) || (mode == ScanMode.Quick && qty != 1)) throw new ArgumentException("Nieprawidłowa ilość odczytu.");
            normalized.Add(new(code, qty));
        }
        var canonical = JsonSerializer.Serialize(new { name, mode = upload.Mode, rows = normalized.Select(r => new { r.Code, Quantity = r.Quantity.ToString("G29", CultureInfo.InvariantCulture) }) });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        if (phoneReceipts.TryGetValue(upload.Id, out var previous))
        {
            if (previous.ClientId != clientId || previous.Hash != hash) throw new InvalidOperationException("Ten dokument został już przyjęty z innymi danymi. Utwórz nowy dokument na telefonie.");
            return previous;
        }
        if (phoneReceipts.Count >= 10000) throw new InvalidOperationException("Osiągnięto limit historii dokumentów z telefonów.");
        var documentName = "Tel " + name + " " + upload.Id.ToString("N")[..8];
        if (documents.Any(d => d.Name.Equals(documentName, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Nazwa dokumentu jest zajęta. Zmień nazwę na telefonie.");
        // Construct and validate completely before touching live data or receipt history.
        var document = new CollectorDocument(documentName, mode);
        var sums = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var row in normalized)
        {
            if (mode == ScanMode.Summed) sums[row.Code] = PhoneQuantity.Add(sums.GetValueOrDefault(row.Code), row.Quantity);
            document.Add(row.Code, row.Quantity);
        }
        document.PhoneRequestId = upload.Id;
        var receipt = new PhoneReceipt(upload.Id, clientId, hash, documentName, "queued", DateTime.UtcNow);
        if (persistBeforeAccept != null)
        {
            var candidate = new SessionSnapshot(2, documents.Append(document).Select(d => new DocumentSnapshot(d.Name, d.Mode, d.Rows.ToList(), d.PhoneRequestId)).ToList(),
                products.Values.ToList(), Deletion, Feedback, phoneReceipts.Values.Append(receipt).ToList());
            persistBeforeAccept(JsonSerializer.Serialize(candidate, new JsonSerializerOptions { WriteIndented = true }));
        }
        documents.Add(document); phoneReceipts.Add(upload.Id, receipt);
        return receipt;
    }
    public bool MarkPhoneReceivedBySuz(DocumentTransferSnapshot snapshot)
    {
        var doc = documents.FirstOrDefault(d => d.DocumentId == snapshot.DocumentId && d.Revision == snapshot.Revision);
        if (snapshot.SessionId != SessionId || doc?.PhoneRequestId is not Guid id || !phoneReceipts.TryGetValue(id, out var r)) return false;
        phoneReceipts[id] = r with { Status = "suz" }; return true;
    }
    internal void RestorePhoneReceipts(IEnumerable<PhoneReceipt> receipts)
    {
        foreach (var r in receipts)
        {
            if (r.Id == Guid.Empty || r.ClientId == Guid.Empty || r.Hash is not { Length: 64 } || r.Hash.Any(c => !Uri.IsHexDigit(c))
                || r.Status is not ("queued" or "suz" or "removed") || !phoneReceipts.TryAdd(r.Id, r)) throw new ArgumentException("Uszkodzona historia dokumentów z telefonów.");
        }
        if (phoneReceipts.Count > 10000 || documents.Where(d => d.PhoneRequestId != null).Select(d => d.PhoneRequestId).Distinct().Count() != documents.Count(d => d.PhoneRequestId != null)) throw new ArgumentException("Nieprawidłowe identyfikatory dokumentów telefonu.");
        foreach (var doc in documents.Where(d => d.PhoneRequestId != null))
            if (!phoneReceipts.TryGetValue(doc.PhoneRequestId!.Value, out var receipt) || receipt.DocumentName != doc.Name) throw new ArgumentException("Brak potwierdzenia dokumentu telefonu w sesji.");
        foreach (var r in phoneReceipts.Values.Where(r => r.Status == "queued"))
            if (!documents.Any(d => d.PhoneRequestId == r.Id && d.Name == r.DocumentName)) throw new ArgumentException("Brak oczekującego dokumentu telefonu w sesji.");
    }
}
