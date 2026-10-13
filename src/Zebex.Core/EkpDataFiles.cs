using System.Globalization;
using System.Text;

namespace Zebex.Core;

/// <summary>
/// Data files consumed or produced by the SUZ EKP native utilities.
/// These records are not serial frames; COM handshakes and acknowledgements need a separate engine.
/// </summary>
public static class EkpDataFiles
{
    private const string SeparateMarker = "----------------";

    /// <summary>Encode a frozen document in the format read by the SUZ Zebex data container.</summary>
    public static string EncodeReaderOutput(DocumentTransferSnapshot document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateReaderField(document.Name, "Nazwa dokumentu");
        var output = new StringBuilder();
        foreach (var row in document.Rows)
        {
            ValidateReaderField(row.Code, "Kod produktu");
            var quantity = document.Mode == ScanMode.Quick ? "I:1" : row.Quantity.ToString(CultureInfo.InvariantCulture);
            var fields = (document.Mode, document.Name) switch
            {
                (ScanMode.Separate, "Skanuj") => new[] { row.Code, SeparateMarker, quantity },
                (ScanMode.Summed, "Skanuj+") or (ScanMode.Quick, "Szybki skan") => new[] { row.Code, quantity },
                (ScanMode.Separate, _) => new[] { document.Name, row.Code, SeparateMarker, quantity },
                _ => new[] { document.Name, row.Code, quantity }
            };
            output.AppendJoin('\t', fields).Append("\r\n");
        }
        return output.ToString();
    }

    /// <summary>
    /// Decode the default five-column lookup file handed by SUZ to zac_dlookup.
    /// Call only for a complete payload; this method neither changes a session nor acknowledges delivery.
    /// </summary>
    public static IReadOnlyList<Product> ParseLookupFile(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var products = new List<Product>();
        using var reader = new StringReader(text.TrimStart('\uFEFF'));
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (line.Length == 0) continue;
            var fields = line.Split('\t');
            if (fields.Length != 5 || fields[4] != "0")
                throw new ArgumentException($"Wiersz {lineNumber}: katalog EKP wymaga pięciu pól oddzielonych TAB i końcowego pola 0.");
            if (fields[0].Length == 0 || fields[0] != fields[0].Trim() || fields.Take(2).Any(f => f.Any(char.IsControl)))
                throw new ArgumentException($"Wiersz {lineNumber}: nieprawidłowy kod lub nazwa produktu w katalogu EKP.");
            if (!TryDecimal(fields[2], out var stock) || !TryDecimal(fields[3], out var price))
                throw new ArgumentException($"Wiersz {lineNumber}: stan i cena w katalogu EKP muszą być liczbami z kropką dziesiętną.");
            products.Add(new(fields[0], fields[1], stock, price));
        }
        // Validate the entire catalog before exposing it to the reception/commit layer.
        var validation = new CollectorSession();
        validation.LoadProducts(products);
        return Array.AsReadOnly(validation.Products.ToArray());
    }

    private static bool TryDecimal(string text, out decimal value) => decimal.TryParse(text,
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);

    private static void ValidateReaderField(string text, string label)
    {
        if (string.IsNullOrWhiteSpace(text) || text != text.Trim() || text.Any(c => c < ' ' || c > '~' || c == '"'))
            throw new ArgumentException(label + ": format odczytu SUZ wymaga znaków ASCII bez TAB, końców wiersza i cudzysłowów. Zmień wartość przed wysłaniem.");
    }
}
