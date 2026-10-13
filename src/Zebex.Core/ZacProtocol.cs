using System.Globalization;
using System.Text;

namespace Zebex.Core;

public static class ZacFrames
{
    public static byte[] Command(string value) => Encoding.ASCII.GetBytes(value + "\r");
    public static byte[] Lookup(string line)
    {
        if (line.Any(c => c > 127 || c is '\r' or '\n')) throw new ArgumentException("Nieprawidłowy rekord ASCII.");
        var payload = Encoding.ASCII.GetBytes(line);
        var sum = payload.Aggregate(0, (s, b) => (s + b) & 65535);
        return [.. payload, Escape((byte)(sum >> 8)), Escape((byte)sum), 13];
    }
    public static byte[] Record(string line) => [2, .. Lookup(line)];
    private static byte Escape(byte value) => value == 13 ? (byte)14 : value;
    public static string Decode(byte[] frame, bool record = false)
    {
        var offset = record ? 1 : 0;
        if (frame.Length < offset + 4 || frame[^1] != 13 || (record && frame[0] != 2)) throw new ArgumentException("Nieprawidłowa ramka.");
        var payload = frame[offset..^3];
        if (payload.Any(b => b > 127 || b is 10 or 13)) throw new ArgumentException("Nieprawidłowe dane ramki.");
        var line = Encoding.ASCII.GetString(payload);
        var expected = Lookup(line);
        if (expected[^3] != frame[^3] || expected[^2] != frame[^2]) throw new ArgumentException("Nieprawidłowa suma kontrolna.");
        return line;
    }
}

/// <summary>Single-owner-thread device endpoint. Feed serial fragments, including split commands.</summary>
public sealed class ZacDevice
{
    private enum State { Idle, Ready, Record, End, Lookup }
    private State state;
    private readonly List<byte> buffer = [];
    private readonly List<string> lookup = [];
    private readonly HashSet<string> lookupCodes = new(StringComparer.Ordinal);
    private string[] records = [];
    private DocumentTransferSnapshot? document;
    private byte[]? lastLookup;
    private byte[]? lastSent;
    private int index, retries, badFrames, lookupSize;
    private DateTime lastActivity;
    private bool completedLookup;
    public Action<byte[]>? Transmit { get; set; }
    public Func<bool>? ReceiveStarted { get; set; }
    public Func<IReadOnlyList<Product>, bool, bool>? ReceiveCompleted { get; set; }
    public Action<DocumentTransferSnapshot>? DocumentCompleted { get; set; }
    public Action<string>? Failed { get; set; }
    public bool IsBusy => state != State.Idle;

    public void Arm(DocumentTransferSnapshot snapshot)
    {
        Cancel();
        records = EkpDataFiles.EncodeReaderOutput(snapshot).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        document = snapshot;
    }
    public void Cancel()
    {
        state = State.Idle; buffer.Clear(); lookup.Clear(); lookupCodes.Clear(); lastLookup = null; completedLookup = false;
        lastSent = null; document = null; records = []; index = retries = badFrames = lookupSize = 0;
    }
    public void Feed(ReadOnlySpan<byte> bytes, DateTime now)
    {
        foreach (var b in bytes)
        {
            if (buffer.Count >= 65536) { Abort("Za duża ramka transmisji."); return; }
            buffer.Add(b);
            if (b != 13) continue;
            var frame = buffer.ToArray(); buffer.Clear();
            lastActivity = now;
            Process(frame, now);
        }
    }
    private void Send(byte[] frame, DateTime now)
    {
        lastSent = frame; lastActivity = now; Transmit?.Invoke(frame);
    }
    private void Process(byte[] frame, DateTime now)
    {
        var command = Encoding.ASCII.GetString(frame);
        if (state == State.Idle && completedLookup && command == "OVER\r") { Transmit?.Invoke(ZacFrames.Command("ACK")); return; }
        if (command == "READ\r")
        {
            if (state == State.Lookup || document == null) { Transmit?.Invoke(ZacFrames.Command("NAK")); return; }
            index = retries = 0; state = State.Ready; Send(ZacFrames.Command("ACK"), now); return;
        }
        if (command == "CIPHER\r")
        {
            if (state == State.Lookup && lookup.Count == 0) { Send(ZacFrames.Command("ACK"), now); return; }
            if (state != State.Idle || ReceiveStarted?.Invoke() != true)
            { Transmit?.Invoke(ZacFrames.Command("NAK")); return; }
            lookup.Clear(); lookupCodes.Clear(); lastLookup = null; completedLookup = false; lookupSize = badFrames = 0;
            state = State.Lookup; Send(ZacFrames.Command("ACK"), now); return;
        }
        if (state == State.Lookup)
        {
            if (command == "OVER\r")
            {
                try
                {
                    var products = EkpDataFiles.ParseLookupFile(string.Join("\r\n", lookup));
                    var check = products.Count == 1 && products[0].Code == "kod_testowy" && products[0].Name.Length == 0;
                    if (ReceiveCompleted?.Invoke(products, check) != true) throw new InvalidOperationException("Nie zapisano katalogu; dane kolektora zachowano.");
                    var armed = document; var armedRecords = records;
                    Cancel();
                    if (check) { document = armed; records = armedRecords; }
                    completedLookup = true; Transmit?.Invoke(ZacFrames.Command("ACK"));
                }
                catch (Exception e) { Transmit?.Invoke(ZacFrames.Command("NAK")); Abort(e.Message); }
                return;
            }
            try
            {
                var line = ZacFrames.Decode(frame);
                var product = EkpDataFiles.ParseLookupFile(line).Single();
                if (lastLookup != null && frame.SequenceEqual(lastLookup)) { Send(ZacFrames.Command("ACK"), now); return; }
                if (!lookupCodes.Add(product.Code)) throw new ArgumentException("Powtórzony kod produktu.");
                if (lookup.Count >= 100000 || (lookupSize += frame.Length) > 16777216) throw new ArgumentException("Katalog przekracza limit pamięci.");
                lookup.Add(line); lastLookup = frame; badFrames = 0; Send(ZacFrames.Command("ACK"), now);
            }
            catch (Exception e) { Transmit?.Invoke(ZacFrames.Command("NAK")); if (++badFrames >= 5) Abort(e.Message); }
            return;
        }
        if (state is State.Ready or State.Record)
        {
            if (command == "NAK\r") { Retry(now); return; }
            if (command != "ACK\r") { Abort("Nieprawidłowe potwierdzenie odbioru."); return; }
            if (state == State.Record) index++;
            retries = 0;
            if (index < records.Length) { state = State.Record; Send(ZacFrames.Record(records[index]), now); }
            else { state = State.End; Send(ZacFrames.Command("OVER"), now); }
            return;
        }
        if (state == State.End)
        {
            if (command == "NAK\r") { Retry(now); return; }
            if (command.Length != 19 || !command.StartsWith("Time", StringComparison.Ordinal)
                || !DateTime.TryParseExact(command[4..^1], "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            { Abort("Brak końcowego potwierdzenia Time od SUZ."); return; }
            var completed = document!; Cancel(); DocumentCompleted?.Invoke(completed);
        }
    }
    public void Tick(DateTime now)
    {
        if (state == State.Idle || now - lastActivity < TimeSpan.FromSeconds(2)) return;
        if (state == State.Lookup) { Abort("Przekroczono czas odbioru katalogu. Dane zachowano."); return; }
        Retry(now);
    }
    private void Retry(DateTime now)
    {
        if (++retries > 4 || lastSent == null) { Abort("SUZ nie potwierdził transmisji. Dane zachowano."); return; }
        Send(lastSent, now);
    }
    private void Abort(string reason) { Cancel(); Failed?.Invoke(reason); }
}

public interface IZacTransport
{
    void Write(byte[] bytes);
    byte[] ReadFrame(int timeoutMilliseconds);
}

/// <summary>Host endpoint used by the SUZ helper; explicit COM, without physical USB enumeration.</summary>
public static class ZacHost
{
    public static void SendLookup(IZacTransport transport, string text)
    {
        var products = EkpDataFiles.ParseLookupFile(text);
        var lines = products.Select(p => string.Join('\t', p.Code, p.Name, p.Stock.ToString(CultureInfo.InvariantCulture), p.Price.ToString(CultureInfo.InvariantCulture), "0")).ToArray();
        Exchange(transport, ZacFrames.Command("CIPHER"), 500, 30);
        foreach (var line in lines) Exchange(transport, ZacFrames.Lookup(line), 3000, 4);
        Exchange(transport, ZacFrames.Command("OVER"), 3000, 4);
    }
    private static void Exchange(IZacTransport transport, byte[] frame, int timeout, int attempts)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            transport.Write(frame);
            try
            {
                var result = Encoding.ASCII.GetString(transport.ReadFrame(timeout));
                if (result == "ACK\r") return;
                if (result != "NAK\r") throw new IOException("Nieprawidłowa odpowiedź kolektora.");
            }
            catch (TimeoutException) { }
        }
        throw new IOException("Kolektor nie potwierdził odbioru.");
    }
    public static string ReadDocument(IZacTransport transport, Action<string>? saveBeforeAcknowledgement = null)
    {
        Exchange(transport, ZacFrames.Command("READ"), 500, 30);
        transport.Write(ZacFrames.Command("ACK"));
        var output = new StringBuilder(); var failures = 0;
        while (true)
        {
            byte[] frame;
            try { frame = transport.ReadFrame(3000); }
            catch (TimeoutException)
            {
                if (++failures > 4) throw;
                transport.Write(ZacFrames.Command("NAK")); continue;
            }
            if (frame.SequenceEqual(ZacFrames.Command("OVER")))
            {
                saveBeforeAcknowledgement?.Invoke(output.ToString());
                transport.Write(ZacFrames.Command("Time" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)));
                return output.ToString();
            }
            string line;
            try { line = ZacFrames.Decode(frame, record: true); }
            catch (ArgumentException)
            {
                if (++failures > 4) throw new IOException("Uszkodzone ramki dokumentu.");
                transport.Write(ZacFrames.Command("NAK")); continue;
            }
            failures = 0;
            if (output.Length + line.Length > 16777216) throw new IOException("Dokument przekracza limit pamięci.");
            output.Append(line).Append("\r\n"); transport.Write(ZacFrames.Command("ACK"));
        }
    }
}
