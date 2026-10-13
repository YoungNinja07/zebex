using System.Globalization;
using System.Text;

namespace Zebex.Core;

public sealed record Scan(string Code, decimal Quantity);

public sealed class Inventory
{
    private readonly List<Scan> scans = [];
    public IReadOnlyList<Scan> Scans => scans.AsReadOnly();

    public void Add(string code, decimal quantity)
    {
        code = code.Trim();
        if (code.Length is 0 or > 128 || code.Any(char.IsControl))
            throw new ArgumentException("Kod musi mieć od 1 do 128 znaków, bez znaków sterujących.");
        if (quantity <= 0) throw new ArgumentException("Ilość musi być większa od zera.");
        var index = scans.FindIndex(s => s.Code == code);
        if (index < 0) scans.Add(new(code, quantity));
        else scans[index] = scans[index] with { Quantity = checked(scans[index].Quantity + quantity) };
    }

    public void Remove(string code) => scans.RemoveAll(s => s.Code == code);
    public void Clear() => scans.Clear();

    // Generic interchange formats. These are not the Z-9000 wire protocol.
    public string Export(char separator, bool header)
    {
        if (separator is not (';' or '\t')) throw new ArgumentException("Nieobsługiwany separator.");
        var output = new StringBuilder();
        if (header) output.Append("Kod").Append(separator).Append("Ilosc\r\n");
        foreach (var scan in scans)
        {
            var code = scan.Code;
            if (code.Contains(separator) || code.Contains('"')) code = "\"" + code.Replace("\"", "\"\"") + "\"";
            output.Append(code).Append(separator).Append(scan.Quantity.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        }
        return output.ToString();
    }
}

public static class WireBytes
{
    public static byte[] ParseHex(string value)
    {
        var compact = new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (compact.Length == 0 || compact.Length % 2 != 0)
            throw new ArgumentException("Wpisz pełne bajty HEX, np. 02 30 03.");
        try { return Convert.FromHexString(compact); }
        catch (FormatException) { throw new ArgumentException("Nieprawidłowe znaki HEX."); }
    }
    public static string Hex(byte[] bytes) => string.Join(" ", bytes.Select(b => b.ToString("X2")));
}
