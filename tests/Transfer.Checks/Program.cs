using Zebex.Core;

var passed = 0;
void Check(string name, Action test) { test(); Console.WriteLine("PASS " + name); passed++; }
void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}."); }
CollectorSession Example(DeleteAfterSending deletion = DeleteAfterSending.Automatic)
{
    var session = new CollectorSession { Deletion = deletion };
    session.LoadProducts([new("001", "Original", 10, 2)]);
    session.Open("Document", ScanMode.Summed).Add("001", 3);
    return session;
}
Product[] Catalog() => [new("002", "Received", 12, 4)];

Check("frozen transfer retains exact rows independently of later scans", () =>
{
    var session = Example(); var snapshot = session.CaptureTransfer("Document");
    session.Documents[0].Add("001", 2);
    Equal(3M, snapshot.Rows[0].Quantity); Equal(5M, session.Documents[0].Rows[0].Quantity);
    Assert(!session.ConfirmVerifiedSend(snapshot, true, true)); Equal(1, session.Documents.Count);
});
Check("terminal rejection and cancellation preserve data and invalidate tokens", () =>
{
    var session = Example(); var rejected = session.CaptureTransfer("Document");
    Assert(!session.ConfirmVerifiedSend(rejected, false)); Assert(!session.ConfirmVerifiedSend(rejected, true));
    var cancelled = session.CaptureTransfer("Document"); session.CancelTransfer(cancelled);
    Assert(!session.ConfirmVerifiedSend(cancelled, true)); Equal(3M, session.Documents[0].Rows[0].Quantity);
});
Check("new capture invalidates a previous pending transfer", () =>
{
    var session = Example(); var old = session.CaptureTransfer("Document"); var current = session.CaptureTransfer("Document");
    Assert(!session.ConfirmVerifiedSend(old, true)); Assert(session.ConfirmVerifiedSend(current, true)); Equal(0, session.Documents.Count);
});
Check("verified accepted transfer deletes only the captured document", () =>
{
    var session = Example(); session.Open("Other", ScanMode.Separate).Add("002", 7);
    var snapshot = session.CaptureTransfer("Document"); Assert(session.ConfirmVerifiedSend(snapshot, true));
    Equal("Other", session.Documents.Single().Name); Equal(7M, session.Documents.Single().Rows.Single().Quantity);
    Assert(!session.ConfirmVerifiedSend(snapshot, true));
});
Check("same name recreated document cannot accept the old acknowledgement", () =>
{
    var session = Example(); var snapshot = session.CaptureTransfer("Document"); session.DeleteDocument("Document");
    session.Open("Document", ScanMode.Summed).Add("001", 3);
    Assert(!session.ConfirmVerifiedSend(snapshot, true)); Equal(1, session.Documents.Count);
});
Check("restored and unrelated sessions reject an old transfer", () =>
{
    var session = Example(); var snapshot = session.CaptureTransfer("Document");
    var restored = CollectorSession.FromJson(session.ToJson()); Assert(!restored.ConfirmVerifiedSend(snapshot, true));
    var other = Example(); Assert(!other.ConfirmVerifiedSend(snapshot, true)); Equal(1, restored.Documents.Count); Equal(1, other.Documents.Count);
});
Check("remove and restore same rows still counts as a changed document", () =>
{
    var session = Example(); var snapshot = session.CaptureTransfer("Document");
    session.Documents[0].RemoveLast(); session.Documents[0].Add("001", 3);
    Assert(!session.ConfirmVerifiedSend(snapshot, true)); Equal(1, session.Documents.Count);
});
Check("deletion policy is frozen with transfer", () =>
{
    var session = Example(DeleteAfterSending.Never); var snapshot = session.CaptureTransfer("Document");
    session.Deletion = DeleteAfterSending.Automatic; Assert(session.ConfirmVerifiedSend(snapshot, true, true)); Equal(1, session.Documents.Count);
    session.Deletion = DeleteAfterSending.Manual; var manual = session.CaptureTransfer("Document");
    Assert(session.ConfirmVerifiedSend(manual, true)); Equal(1, session.Documents.Count);
    var confirmed = session.CaptureTransfer("Document"); Assert(session.ConfirmVerifiedSend(confirmed, true, true)); Equal(0, session.Documents.Count);
});
Check("catalog staging and failed commit leave products and sums untouched", () =>
{
    var session = Example(); var staged = session.StageCatalog(Catalog());
    Equal("Original", session.FindProduct("001")!.Name); Equal(3M, session.Documents[0].Rows[0].Quantity);
    Assert(!session.CommitVerifiedCatalog(staged, false)); Assert(!session.CommitVerifiedCatalog(staged, true));
    Equal("Original", session.FindProduct("001")!.Name); Equal(3M, session.Documents[0].Rows[0].Quantity);
});
Check("verified complete catalog commits atomically and resets only summed documents", () =>
{
    var session = Example(); session.Open("Ordinary", ScanMode.Separate).Add("001", 8);
    var staged = session.StageCatalog(Catalog()); Assert(session.CommitVerifiedCatalog(staged, true));
    Equal(null, session.FindProduct("001")); Equal("Received", session.FindProduct("002")!.Name);
    Equal(0M, session.Documents[0].Rows[0].Quantity); Equal(8M, session.Documents[1].Rows[0].Quantity);
    Assert(!session.CommitVerifiedCatalog(staged, true));
});
Check("cancelled and foreign-session catalog cannot commit", () =>
{
    var session = Example(); var staged = session.StageCatalog(Catalog()); var restored = CollectorSession.FromJson(session.ToJson());
    Assert(!restored.CommitVerifiedCatalog(staged, true)); session.CancelCatalog(staged); Assert(!session.CommitVerifiedCatalog(staged, true));
    Equal("Original", session.FindProduct("001")!.Name); Equal(3M, session.Documents[0].Rows[0].Quantity);
});
Check("concurrent scan prevents catalog commit and preserves that scan", () =>
{
    var session = Example(); var staged = session.StageCatalog(Catalog()); session.Documents[0].Add("001", 2);
    Assert(!session.CommitVerifiedCatalog(staged, true)); Equal(5M, session.Documents[0].Rows[0].Quantity); Equal("Original", session.FindProduct("001")!.Name);
});
Check("scan during reception prevents later catalog staging from hiding the change", () =>
{
    var session = Example(); var baseline = session.CaptureCatalogBaseline();
    session.Documents[0].Add("001", 2);
    var staged = session.StageCatalog(Catalog(), baseline);
    Assert(!session.CommitVerifiedCatalog(staged, true)); Equal(5M, session.Documents[0].Rows[0].Quantity);
    Equal("Original", session.FindProduct("001")!.Name);
});
Check("catalog reception baseline is bound to its original runtime session", () =>
{
    var session = Example(); var baseline = session.CaptureCatalogBaseline(); var restored = CollectorSession.FromJson(session.ToJson());
    try { restored.StageCatalog(Catalog(), baseline); throw new Exception("Expected different-session rejection."); }
    catch (ArgumentException) { }
    Equal("Original", restored.FindProduct("001")!.Name); Equal(3M, restored.Documents[0].Rows[0].Quantity);
});
Check("new deleted or replaced document prevents catalog commit", () =>
{
    var session = Example(); var staged = session.StageCatalog(Catalog()); session.Open("New", ScanMode.Separate).Add("002", 4);
    Assert(!session.CommitVerifiedCatalog(staged, true)); Equal(4M, session.Documents[1].Rows[0].Quantity);
    staged = session.StageCatalog(Catalog()); session.DeleteDocument("New"); Assert(!session.CommitVerifiedCatalog(staged, true));
    staged = session.StageCatalog(Catalog()); session.DeleteDocument("Document"); session.Open("Document", ScanMode.Summed).Add("001", 3);
    Assert(!session.CommitVerifiedCatalog(staged, true)); Equal(3M, session.Documents[0].Rows[0].Quantity);
});
Check("local catalog change prevents stale remote catalog commit", () =>
{
    var session = Example(); var staged = session.StageCatalog(Catalog()); session.LoadProducts([new("003", "Local", 1, 9)]);
    Assert(!session.CommitVerifiedCatalog(staged, true)); Equal("Local", session.FindProduct("003")!.Name);
});
Check("invalid staged catalog cannot mutate products or recorded data", () =>
{
    var session = Example();
    try { session.StageCatalog([new("002", "A", 1, 1), new("002", "B", 1, 1)]); throw new Exception("Expected duplicate rejection."); }
    catch (ArgumentException) { }
    Equal("Original", session.FindProduct("001")!.Name); Equal(3M, session.Documents[0].Rows[0].Quantity);
});
Check("catalog and document snapshots do not expose mutable list storage", () =>
{
    var session = Example(); var transfer = session.CaptureTransfer("Document"); var source = Catalog(); var staged = session.StageCatalog(source);
    source[0] = new("999", "Changed", 0, 0); Equal("002", staged.Products[0].Code);
    Assert(transfer.Rows is System.Collections.IList { IsReadOnly: true }); Assert(staged.Products is System.Collections.IList { IsReadOnly: true });
});
Check("failed quantity overflow leaves revision and pending transfer valid", () =>
{
    var session = new CollectorSession { Deletion = DeleteAfterSending.Never }; var document = session.Open("Huge", ScanMode.Summed);
    document.Add("001", decimal.MaxValue); var snapshot = session.CaptureTransfer("Huge");
    try { document.Add("001", 1); throw new Exception("Expected overflow."); } catch (OverflowException) { }
    Equal(snapshot.Revision, document.Revision); Assert(session.ConfirmVerifiedSend(snapshot, true));
});
Console.WriteLine($"Completed: {passed} passed, 0 failed.");
