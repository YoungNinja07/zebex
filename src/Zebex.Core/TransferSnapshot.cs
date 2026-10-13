namespace Zebex.Core;

/// <summary>An immutable copy of one document, bound to one runtime session and pending transfer.</summary>
public sealed class DocumentTransferSnapshot
{
    public Guid TransferId { get; } = Guid.NewGuid();
    public Guid SessionId { get; }
    public Guid DocumentId { get; }
    public long Revision { get; }
    public string Name { get; }
    public ScanMode Mode { get; }
    public DeleteAfterSending DeletionPolicy { get; }
    public IReadOnlyList<Scan> Rows { get; }

    internal DocumentTransferSnapshot(Guid sessionId, Guid documentId, long revision, string name,
        ScanMode mode, IEnumerable<Scan> rows, DeleteAfterSending deletionPolicy)
    {
        SessionId = sessionId; DocumentId = documentId; Revision = revision; Name = name; Mode = mode;
        DeletionPolicy = deletionPolicy; Rows = Array.AsReadOnly(rows.ToArray());
    }
}

internal sealed record DocumentRevision(Guid DocumentId, long Revision);

/// <summary>Freeze session state at the beginning of catalog reception, before the payload is available.</summary>
public sealed class CatalogTransferBaseline
{
    public Guid SessionId { get; }
    internal long CatalogRevision { get; }
    internal IReadOnlyList<DocumentRevision> DocumentRevisions { get; }

    internal CatalogTransferBaseline(Guid sessionId, long catalogRevision, IEnumerable<DocumentRevision> documentRevisions)
    {
        SessionId = sessionId; CatalogRevision = catalogRevision;
        DocumentRevisions = Array.AsReadOnly(documentRevisions.ToArray());
    }
}

/// <summary>A complete validated catalog awaiting verified delivery and an unchanged session baseline.</summary>
public sealed class CatalogTransferSnapshot
{
    public Guid TransferId { get; } = Guid.NewGuid();
    public Guid SessionId { get; }
    public IReadOnlyList<Product> Products { get; }
    internal long CatalogRevision { get; }
    internal IReadOnlyList<DocumentRevision> DocumentRevisions { get; }

    internal CatalogTransferSnapshot(Guid sessionId, long catalogRevision, IEnumerable<Product> products,
        IEnumerable<DocumentRevision> documentRevisions)
    {
        SessionId = sessionId; CatalogRevision = catalogRevision;
        Products = Array.AsReadOnly(products.ToArray());
        DocumentRevisions = Array.AsReadOnly(documentRevisions.ToArray());
    }
}
