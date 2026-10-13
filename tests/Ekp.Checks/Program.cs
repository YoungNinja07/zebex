using System.Globalization;
using Zebex.Core;

var passed = 0;
void Check(string name, Action test) { test(); Console.WriteLine("PASS " + name); passed++; }
void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}."); }
void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected rejection."); }
DocumentTransferSnapshot Capture(string name, ScanMode mode, params Scan[] rows)
{
    var session = new CollectorSession(); var document = session.Open(name, mode);
    foreach (var row in rows) document.Add(row.Code, row.Quantity);
    return session.CaptureTransfer(name);
}
string Encode(string name, ScanMode mode, params Scan[] rows) => EkpDataFiles.EncodeReaderOutput(Capture(name, mode, rows));

Check("built-in Skanuj has three TAB fields and preserves repeated scans", () =>
{
    Equal("000123\t----------------\t2\r\n000123\t----------------\t3.125\r\n",
        Encode("Skanuj", ScanMode.Separate, new("000123", 2), new("000123", 3.125M)));
});
Check("built-in Skanuj+ has two TAB fields and preserves zero and negative totals", () =>
{
    Equal("001\t0\r\n002\t-1.5\r\n", Encode("Skanuj+", ScanMode.Summed,
        new("001", 2), new("001", -2), new("002", -1.5M)));
});
Check("built-in Szybki skan has two TAB fields and one I:1 per repeated scan", () =>
{
    Equal("000123\tI:1\r\n000123\tI:1\r\n", Encode("Szybki skan", ScanMode.Quick,
        new("000123", 7), new("000123", 99)));
});
Check("named separate document has four TAB fields with a 16-hyphen marker", () =>
{
    Equal("Dok.1 (PZ)\t001\t----------------\t1\r\nDok.1 (PZ)\t001\t----------------\t2.5\r\n",
        Encode("Dok.1 (PZ)", ScanMode.Separate, new("001", 1), new("001", 2.5M)));
});
Check("named summed document has three TAB fields", () =>
{
    Equal("Inventory\t001\t3.75\r\n", Encode("Inventory", ScanMode.Summed, new("001", 1.5M), new("001", 2.25M)));
});
Check("named quick document has three TAB fields and preserves repetitions", () =>
{
    Equal("QuickDocument\t001\tI:1\r\nQuickDocument\t001\tI:1\r\n",
        Encode("QuickDocument", ScanMode.Quick, new("001", 20), new("001", 30)));
});
Check("reader decimal format is invariant under Polish culture", () =>
{
    var original = CultureInfo.CurrentCulture;
    try
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
        Equal("001\t0.125\r\n002\t-3.75\r\n", Encode("Skanuj+", ScanMode.Summed, new("001", 0.125M), new("002", -3.75M)));
    }
    finally { CultureInfo.CurrentCulture = original; }
});
Check("reader encoding leaves live session and frozen snapshot unchanged", () =>
{
    var session = new CollectorSession { Deletion = DeleteAfterSending.Automatic };
    session.LoadProducts([new("001", "Product", 8, 4)]); var document = session.Open("Skanuj", ScanMode.Separate);
    document.Add("001", 2); var snapshot = session.CaptureTransfer("Skanuj"); var state = session.ToJson(); var revision = document.Revision;
    Equal("001\t----------------\t2\r\n", EkpDataFiles.EncodeReaderOutput(snapshot));
    Equal(state, session.ToJson()); Equal(revision, document.Revision); Equal(2M, snapshot.Rows[0].Quantity);
    // Encoding must not consume or fake remote acceptance of the pending transfer.
    Assert(session.ConfirmVerifiedSend(snapshot, true)); Equal(0, session.Documents.Count);
});
Check("reader rejects Unicode fields rather than losing characters during ASCII conversion", () =>
{
    Reject(() => Encode("Skanuj", ScanMode.Separate, new Scan("Ż001", 1)));
    Reject(() => Encode("Zażółć", ScanMode.Separate, new Scan("001", 1)));
    Reject(() => Encode("Skanuj", ScanMode.Separate, new Scan("😀", 1)));
});
Check("reader rejects quoted fields rather than corrupting vendor record parsing", () =>
{
    Reject(() => Encode("Skanuj", ScanMode.Separate, new Scan("00\"1", 1)));
    Reject(() => Encode("Document\"Name", ScanMode.Summed, new Scan("001", 1)));
});
Check("control characters cannot become fields in a document sent to the reader", () =>
{
    foreach (var control in new[] { '\0', '\t', '\r', '\n', '\x1b', '\x7f' })
    {
        Reject(() => Encode("Skanuj", ScanMode.Separate, new Scan("00" + control + "1", 1)));
        Reject(() => Encode("Doc" + control + "Name", ScanMode.Separate, new Scan("001", 1)));
    }
});
Check("supported punctuation stays literal in TAB-delimited reader fields", () =>
{
    Equal("Comma,semicolon;\tA,B;C\t----------------\t1\r\n",
        Encode("Comma,semicolon;", ScanMode.Separate, new Scan("A,B;C", 1)));
});
Check("lookup parses the five-column default file without a header and preserves code text", () =>
{
    var products = EkpDataFiles.ParseLookupFile("000123\tTowar demonstracyjny\t12.5\t9.99\t0\r\n001\tDrugi\t-2\t0.125\t0\r\n");
    Equal(2, products.Count); Equal(new Product("000123", "Towar demonstracyjny", 12.5M, 9.99M), products[0]);
    Equal(new Product("001", "Drugi", -2, 0.125M), products[1]);
});
Check("lookup supports UTF BOM names and final record without newline", () =>
{
    var product = EkpDataFiles.ParseLookupFile("\uFEFF000123\tZażółć gęślą\t1\t2.5\t0").Single();
    Equal(new Product("000123", "Zażółć gęślą", 1, 2.5M), product);
});
Check("lookup accepts the BOM-prefixed SUZ connection-check record with empty name", () =>
{
    Equal(new Product("kod_testowy", "", 0, 0), EkpDataFiles.ParseLookupFile("\uFEFFkod_testowy\t\t0\t0\t0\r\n").Single());
});
Check("lookup does not mistake vendor schema widths for enforced save limits", () =>
{
    var code = new string('1', 21); var name = new string('N', 33);
    Equal(new Product(code, name, 1, 2), EkpDataFiles.ParseLookupFile(code + "\t" + name + "\t1\t2\t0").Single());
});
Check("empty lookup is a valid complete empty catalog", () =>
{
    Equal(0, EkpDataFiles.ParseLookupFile("").Count); Equal(0, EkpDataFiles.ParseLookupFile("\uFEFF\r\n\n").Count);
});
Check("lookup rejects headers incorrect column count and incorrect final field", () =>
{
    foreach (var input in new[]
    {
        "Kod\tNazwa\tStan\tCena\t0", "001\tName\t1\t2", "001\tName\t1\t2\t0\textra",
        "001\tName\t1\t2\t1", "001\tName\t1\t2\t00", "001\tName\t1\t2\t", "001;Name;1;2;0"
    }) Reject(() => EkpDataFiles.ParseLookupFile(input));
});
Check("lookup requires invariant numeric syntax even under Polish culture", () =>
{
    var original = CultureInfo.CurrentCulture;
    try
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
        Equal(2.5M, EkpDataFiles.ParseLookupFile("001\tName\t2.5\t1.125\t0").Single().Stock);
        foreach (var number in new[] { "1,25", "1,000.25", "1e2", "NaN", "Infinity", " 1", "1 ", "", "79228162514264337593543950336" })
        {
            Reject(() => EkpDataFiles.ParseLookupFile("001\tName\t" + number + "\t2\t0"));
            Reject(() => EkpDataFiles.ParseLookupFile("001\tName\t1\t" + number + "\t0"));
        }
    }
    finally { CultureInfo.CurrentCulture = original; }
});
Check("lookup rejects malformed code name control characters and duplicate codes", () =>
{
    foreach (var input in new[]
    {
        "\tName\t1\t2\t0", " 001\tName\t1\t2\t0", "001 \tName\t1\t2\t0",
        "00\0\tName\t1\t2\t0", "001\tNa\x1bme\t1\t2\t0", "001\tNa\rme\t1\t2\t0",
        "001\tA\t1\t2\t0\r\n001\tB\t3\t4\t0", new string('1', 129) + "\tName\t1\t2\t0",
        "001\t" + new string('N', 257) + "\t1\t2\t0"
    }) Reject(() => EkpDataFiles.ParseLookupFile(input));
});
Check("malformed later lookup rows expose no partial catalog and do not mutate a session", () =>
{
    var session = new CollectorSession(); session.LoadProducts([new("009", "Original", 4, 8)]);
    var document = session.Open("Skanuj+", ScanMode.Summed); document.Add("009", 5); var state = session.ToJson();
    Reject(() =>
    {
        var products = EkpDataFiles.ParseLookupFile("001\tValid first\t1\t2\t0\r\n002\tInvalid later\tnot-a-number\t3\t0");
        session.LoadProducts(products);
    });
    Equal(state, session.ToJson()); Equal(5M, document.Rows[0].Quantity); Equal("Original", session.FindProduct("009")!.Name);
});
Check("lookup parse has no mutation before explicit verified catalog commit", () =>
{
    var session = new CollectorSession(); session.LoadProducts([new("009", "Original", 4, 8)]);
    var document = session.Open("Skanuj+", ScanMode.Summed); document.Add("009", 5); var baseline = session.CaptureCatalogBaseline(); var state = session.ToJson();
    var decoded = EkpDataFiles.ParseLookupFile("001\tIncoming\t1\t2\t0"); Equal(state, session.ToJson());
    var staged = session.StageCatalog(decoded, baseline); Equal(state, session.ToJson());
    Assert(session.CommitVerifiedCatalog(staged, true)); Equal(null, session.FindProduct("009")); Equal("Incoming", session.FindProduct("001")!.Name);
    Equal(0M, document.Rows[0].Quantity);
});
Check("lookup results have read-only storage and ordinal code identity", () =>
{
    var products = EkpDataFiles.ParseLookupFile("abc\tA\t1\t2\t0\nABC\tB\t3\t4\t0");
    Equal(2, products.Count); Assert(products is System.Collections.IList { IsReadOnly: true });
});
Console.WriteLine($"Completed: {passed} passed, 0 failed. These checks validate intermediate EKP data files, not COM interoperability.");
