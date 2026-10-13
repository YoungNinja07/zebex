using System.Text;
using Zebex.Core;
using System.Security.Cryptography;
using System.Text.Json;
using System.IO.Ports;

if (args.Length == 3 && args[0] == "--serial-device")
{
    var session = new CollectorSession { Deletion = DeleteAfterSending.Never };
    session.LoadProducts([new("001", "Original", 5, 2)]);
    var doc = session.Open("Skanuj", ScanMode.Separate); doc.Add("001", 2); doc.Add("001", 3.5M);
    var device = new ZacDevice(); CatalogTransferBaseline? baseline = null;
    using var port = new SerialPort(args[1], 115200) { ReadTimeout = 100, WriteTimeout = 3000 };
    port.Open();
    device.Transmit = bytes => port.Write(bytes, 0, bytes.Length);
    device.ReceiveStarted = () => { baseline = session.CaptureCatalogBaseline(); return true; };
    device.ReceiveCompleted = (products, check) =>
    {
        if (!check && !session.CommitVerifiedCatalog(session.StageCatalog(products, baseline!), true)) return false;
        File.WriteAllText(args[2], session.ToJson()); return true;
    };
    device.DocumentCompleted = snapshot => { session.ConfirmVerifiedSend(snapshot, true); File.WriteAllText(args[2], session.ToJson()); device.Arm(session.CaptureTransfer("Skanuj")); };
    device.Failed = message => Console.WriteLine("ERROR " + message);
    device.Arm(session.CaptureTransfer("Skanuj")); Console.WriteLine("READY");
    while (true)
    {
        try { var b = port.ReadByte(); device.Feed([(byte)b], DateTime.UtcNow); }
        catch (TimeoutException) { device.Tick(DateTime.UtcNow); }
    }
}

if (args.Length == 2 && args[0] == "--manifest")
{
    var folder = Path.GetFullPath(args[1]);
    var manifest = Directory.GetFiles(folder).Where(f => Path.GetFileName(f) != "zebex-bridge-manifest.json")
        .Order().ToDictionary(f => Path.GetFileName(f), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))).ToLowerInvariant());
    File.WriteAllText(Path.Combine(folder, "zebex-bridge-manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("Manifest SHA256 zapisany: " + manifest.Count + " plików.");
    return;
}

var passed = 0;
void Check(string name, Action action) { action(); Console.WriteLine("PASS " + name); passed++; }
void Assert(bool value) { if (!value) throw new Exception("Assertion failed"); }
void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception($"Expected {expected}, actual {actual}"); }
void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or IOException or TimeoutException) { return; } throw new Exception("Expected rejection"); }
CollectorSession Session(ScanMode mode = ScanMode.Summed, string name = "Skanuj+")
{
    var s = new CollectorSession { Deletion = DeleteAfterSending.Automatic }; s.LoadProducts([new("001", "Original", 5, 2)]);
    var d = s.Open(name, mode); d.Add("001", 2); d.Add("001", mode == ScanMode.Summed ? -1 : 2); return s;
}
Check("lookup sum matches escaped big-endian additive checksum", () =>
{
    Equal("303031094E09310932093001960D", Convert.ToHexString(ZacFrames.Lookup("001\tN\t1\t2\t0")));
    Equal((byte)14, ZacFrames.Lookup(new string('A', 205))[^2]);
    Equal((byte)14, ZacFrames.Lookup(new string('A', 52))[^3]);
});
Check("framing checksum rejects corruption and handles escaped CR checksum", () =>
{
    var text = new string('A', 197); // 197*65 = 12805; low checksum byte 5.
    Equal(text, ZacFrames.Decode(ZacFrames.Lookup(text)));
    var escaped = ZacFrames.Lookup(new string('A', 205));
    Assert(escaped[^2] != 13); Equal(new string('A', 205), ZacFrames.Decode(escaped));
    var broken = ZacFrames.Lookup("001\tName\t1\t2\t0"); broken[0] ^= 1; Reject(() => ZacFrames.Decode(broken));
});
foreach (var (mode, name) in new[] { (ScanMode.Separate, "Skanuj"), (ScanMode.Summed, "Skanuj+"), (ScanMode.Quick, "Szybki skan"), (ScanMode.Separate, "Dok.1 (PZ)"), (ScanMode.Summed, "Stock"), (ScanMode.Quick, "QuickDoc") })
Check("document end-to-end fragmented transfer " + mode + " " + name, () =>
{
    var s = Session(mode, name); var snapshot = s.CaptureTransfer(name); var device = new ZacDevice(); var transport = new DeviceTransport(device, 1);
    var completed = 0; device.DocumentCompleted = sent => { Assert(ReferenceEquals(snapshot, sent)); Assert(s.ConfirmVerifiedSend(sent, true)); completed++; };
    device.Arm(snapshot); var expected = EkpDataFiles.EncodeReaderOutput(snapshot); Equal(expected, ZacHost.ReadDocument(transport));
    Equal(1, completed); Equal(0, s.Documents.Count); Assert(!device.IsBusy);
});
Check("no document acceptance until final Time acknowledgement", () =>
{
    var s = Session(); var device = new ZacDevice(); var transport = new DeviceTransport(device);
    var completed = 0; var snapshot = s.CaptureTransfer("Skanuj+"); device.DocumentCompleted = _ => completed++; device.Arm(snapshot);
    transport.Write(ZacFrames.Command("READ")); Equal("ACK\r", Encoding.ASCII.GetString(transport.ReadFrame(1)));
    transport.Write(ZacFrames.Command("ACK")); transport.ReadFrame(1); transport.Write(ZacFrames.Command("ACK"));
    Equal("OVER\r", Encoding.ASCII.GetString(transport.ReadFrame(1))); Equal(0, completed); Equal(1, s.Documents.Count);
    transport.Write(ZacFrames.Command("Time20261013123456")); Equal(1, completed);
});
Check("failed host persistence sends no final confirmation and preserves document", () =>
{
    var s = Session(); var device = new ZacDevice(); var transport = new DeviceTransport(device); var completed = 0;
    device.Arm(s.CaptureTransfer("Skanuj+")); device.DocumentCompleted = _ => completed++;
    Reject(() => ZacHost.ReadDocument(transport, _ => throw new IOException("Disk full")));
    Equal(0, completed); Equal(1, s.Documents.Count); device.Cancel();
});
Check("corrupted document retransmits on NAK without duplicate rows", () =>
{
    var s = Session(ScanMode.Separate, "Skanuj"); var device = new ZacDevice(); var transport = new DeviceTransport(device) { CorruptFirstRecord = true };
    var snap = s.CaptureTransfer("Skanuj"); device.Arm(snap); Equal(EkpDataFiles.EncodeReaderOutput(snap), ZacHost.ReadDocument(transport)); Assert(transport.Naks > 0);
});
Check("lookup commits all records only after OVER", () =>
{
    var s = Session(); var old = s.ToJson(); CatalogTransferBaseline? baseline = null; var device = new ZacDevice(); var transport = new DeviceTransport(device, 2); var received = 0;
    device.ReceiveStarted = () => { baseline = s.CaptureCatalogBaseline(); return true; };
    device.ReceiveCompleted = (p, check) => { Assert(!check); Equal(old, s.ToJson()); received++; return s.CommitVerifiedCatalog(s.StageCatalog(p, baseline!), true); };
    ZacHost.SendLookup(transport, "0002\tSecond\t3.5\t4.25\t0\r\n0003\tThird\t0\t2\t0\r\n");
    Equal(1, received); Equal(2, s.Products.Count); Equal(0M, s.Documents[0].Rows[0].Quantity); Equal("Second", s.FindProduct("0002")!.Name);
});
Check("SUZ kod_testowy status check never replaces catalog or resets quantities", () =>
{
    var s = Session(); var old = s.ToJson(); var device = new ZacDevice(); var transport = new DeviceTransport(device); var checks = 0;
    device.ReceiveStarted = () => true;
    device.ReceiveCompleted = (p, check) => { Assert(check); Equal("kod_testowy", p.Single().Code); checks++; return true; };
    ZacHost.SendLookup(transport, "\uFEFFkod_testowy\t\t0\t0\t0\r\n"); Equal(old, s.ToJson()); Equal(1, checks);
});
Check("bad lookup checksum yields NAK without partial commit", () =>
{
    var device = new ZacDevice(); var transport = new DeviceTransport(device); var commits = 0;
    device.ReceiveStarted = () => true; device.ReceiveCompleted = (_, _) => { commits++; return true; };
    transport.Write(ZacFrames.Command("CIPHER")); transport.ReadFrame(1);
    var frame = ZacFrames.Lookup("001\tName\t1\t2\t0"); frame[0] ^= 1; transport.Write(frame);
    Equal("NAK\r", Encoding.ASCII.GetString(transport.ReadFrame(1))); Equal(0, commits);
});
Check("status probe while document is armed preserves the pending document transfer", () =>
{
    var s = Session(); var snapshot = s.CaptureTransfer("Skanuj+"); var device = new ZacDevice(); var transport = new DeviceTransport(device);
    device.Arm(snapshot); device.ReceiveStarted = () => true; device.ReceiveCompleted = (_, check) => check;
    ZacHost.SendLookup(transport, "kod_testowy\t\t0\t0\t0");
    Equal(EkpDataFiles.EncodeReaderOutput(snapshot), ZacHost.ReadDocument(transport)); Equal(1, s.Documents.Count);
});
Check("changed session refuses received catalog and host reports failure", () =>
{
    var s = Session(); var device = new ZacDevice(); var transport = new DeviceTransport(device); CatalogTransferBaseline? baseline = null;
    device.ReceiveStarted = () => { baseline = s.CaptureCatalogBaseline(); return true; };
    device.ReceiveCompleted = (p, _) => { s.Documents[0].Add("001", 2); return s.CommitVerifiedCatalog(s.StageCatalog(p, baseline!), true); };
    Reject(() => ZacHost.SendLookup(transport, "002\tNew\t1\t2\t0")); Equal("Original", s.FindProduct("001")!.Name); Equal(3M, s.Documents[0].Rows[0].Quantity);
});
Check("duplicate acknowledged lookup frame and repeated OVER are idempotent", () =>
{
    var device = new ZacDevice(); var transport = new DeviceTransport(device); var commits = 0;
    device.ReceiveStarted = () => true; device.ReceiveCompleted = (p, _) => { Equal(1, p.Count); commits++; return true; };
    foreach (var f in new[] { ZacFrames.Command("CIPHER"), ZacFrames.Lookup("001\tName\t1\t2\t0"), ZacFrames.Lookup("001\tName\t1\t2\t0"), ZacFrames.Command("OVER"), ZacFrames.Command("OVER") })
    { transport.Write(f); Equal("ACK\r", Encoding.ASCII.GetString(transport.ReadFrame(1))); }
    Equal(1, commits);
});
Check("timeout cancellation leaves source document untouched", () =>
{
    var s = Session(); var old = s.ToJson(); var device = new ZacDevice(); var transport = new DeviceTransport(device); var failed = 0;
    device.Arm(s.CaptureTransfer("Skanuj+")); device.Failed = _ => failed++;
    var now = DateTime.UtcNow; device.Feed(ZacFrames.Command("READ"), now);
    for (var i = 1; i <= 5; i++) device.Tick(now.AddSeconds(i * 3));
    Equal(1, failed); Equal(old, s.ToJson()); Assert(!device.IsBusy);
});
Check("READ without an armed document refuses to fabricate records", () =>
{
    var transport = new DeviceTransport(new ZacDevice()); transport.Write(ZacFrames.Command("READ")); Equal("NAK\r", Encoding.ASCII.GetString(transport.ReadFrame(1)));
});
Check("overlong input aborts rather than growing unbounded", () =>
{
    var device = new ZacDevice(); var failed = 0; device.Failed = _ => failed++;
    device.Feed(new byte[65537], DateTime.UtcNow); Equal(1, failed);
});
Console.WriteLine($"Completed: {passed} passed, 0 failed.");

sealed class DeviceTransport : IZacTransport
{
    private readonly ZacDevice device; private readonly int fragment; private readonly Queue<byte[]> replies = new();
    public bool CorruptFirstRecord { get; set; }
    public int Naks { get; private set; }
    public DeviceTransport(ZacDevice device, int fragment = 100000)
    {
        this.device = device; this.fragment = fragment;
        device.Transmit = bytes => { var copy = bytes.ToArray(); if (CorruptFirstRecord && copy[0] == 2) { copy[1] ^= 1; CorruptFirstRecord = false; } replies.Enqueue(copy); };
    }
    public void Write(byte[] bytes)
    {
        if (bytes.SequenceEqual(ZacFrames.Command("NAK"))) Naks++;
        for (var i = 0; i < bytes.Length; i += fragment) device.Feed(bytes.AsSpan(i, Math.Min(fragment, bytes.Length - i)), DateTime.UtcNow);
    }
    public byte[] ReadFrame(int timeoutMilliseconds) => replies.Count > 0 ? replies.Dequeue() : throw new TimeoutException();
}
