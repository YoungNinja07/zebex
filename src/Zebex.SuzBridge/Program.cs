using System.IO.Ports;
using System.Text;
using Zebex.Core;

// Independent replacements for the two SUZ communication processes. No vendor binaries are distributed.
try
{
    if (args.Length is not (4 or 6)) throw new ArgumentException("Oczekiwane argumenty SUZ: COM, prędkość, opcje, ścieżka pliku.");
    var read = args.Length == 6;
    if (!args[0].StartsWith("COM", StringComparison.OrdinalIgnoreCase) && !args[0].StartsWith("/dev/", StringComparison.Ordinal))
        throw new ArgumentException("Wybierz port COM w SUZ; AUTO wymaga prawdziwego kolektora.");
    using var port = new SerialPort(args[0], int.Parse(args[1]), Parity.None, 8, StopBits.One)
    { DtrEnable = false, RtsEnable = false, Handshake = Handshake.None, WriteTimeout = 3000 };
    port.Open();
    var transport = new SerialTransport(port);
    if (read)
    {
        Console.WriteLine("# Start receive data.");
        var encoding = args[2] switch { "0" => Encoding.ASCII, "1" => Encoding.BigEndianUnicode, "2" => Encoding.Unicode, "3" => Encoding.UTF8, "4" => Encoding.UTF32, _ => throw new ArgumentException("Nieprawidłowe kodowanie.") };
        ZacHost.ReadDocument(transport, text =>
        {
            if (args[3] == "1") File.AppendAllText(args[5], text, encoding);
            else
            {
                var temporary = args[5] + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.WriteAllText(temporary, text, encoding); File.Move(temporary, args[5], overwrite: true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        });
        Console.WriteLine("# Receive data completed !!");
    }
    else
    {
        Console.WriteLine("# Start sending a lookup table.");
        ZacHost.SendLookup(transport, File.ReadAllText(args[3], Encoding.UTF8));
        Console.WriteLine("# Send lookup table completed!!");
    }
    return 0;
}
catch (Exception e) { Console.WriteLine("# " + e.Message); return 1; }

sealed class SerialTransport(SerialPort port) : IZacTransport
{
    public void Write(byte[] bytes) => port.Write(bytes, 0, bytes.Length);
    public byte[] ReadFrame(int timeoutMilliseconds)
    {
        var bytes = new List<byte>(); var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (true)
        {
            var remaining = deadline - Environment.TickCount64;
            if (remaining <= 0) throw new TimeoutException("Przekroczono czas odpowiedzi kolektora.");
            port.ReadTimeout = (int)remaining;
            var b = port.ReadByte(); bytes.Add((byte)b);
            if (bytes.Count > 65536) throw new IOException("Za duża ramka.");
            if (b == 13) return bytes.ToArray();
        }
    }
}
