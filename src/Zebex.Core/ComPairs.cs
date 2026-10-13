using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Zebex.Core;

public sealed record ComPair(int Number, string SuzPort, string SimulatorPort);

public static partial class ComPairs
{
    // COM# makes Windows allocate a free number and register a Ports-class device.
    public static IReadOnlyList<ComPair> ParseList(string output)
    {
        var ends = new Dictionary<(int, char), string>();
        foreach (var line in output.Split('\n'))
        {
            var match = PortLine().Match(line);
            if (!match.Success || !int.TryParse(match.Groups[2].Value, out var number)) continue;
            var fields = match.Groups[3].Value.Split(',').Select(s => s.Trim().Split('=', 2))
                .Where(s => s.Length == 2).GroupBy(s => s[0], StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last()[1].Trim(), StringComparer.OrdinalIgnoreCase);
            if (!fields.TryGetValue("PortName", out var name)) continue;
            if (name.Equals("COM#", StringComparison.OrdinalIgnoreCase))
                name = fields.GetValueOrDefault("RealPortName", "");
            var com = ComName().Match(name);
            if (!com.Success || !int.TryParse(com.Groups[1].Value, out var portNumber) || portNumber > 65535) continue;
            ends[(number, char.ToUpperInvariant(match.Groups[1].Value[0]))] = "COM" + portNumber;
        }
        return ends.Where(e => e.Key.Item2 == 'A' && ends.ContainsKey((e.Key.Item1, 'B')))
            .Select(e => new ComPair(e.Key.Item1, e.Value, ends[(e.Key.Item1, 'B')]))
            .Where(p => p.SuzPort != p.SimulatorPort).OrderBy(p => p.Number).ToArray();
    }

    public static ComPair NewlyCreated(IReadOnlyList<ComPair> before, IReadOnlyList<ComPair> after)
    {
        var known = before.Select(p => p.Number).ToHashSet();
        var created = after.Where(p => !known.Contains(p.Number)).ToArray();
        return created.Length == 1 ? created[0] : throw new InvalidOperationException(
            "Nie udało się jednoznacznie wykryć nowej pary COM. Sprawdź raport instalacji; nie twórz kolejnej pary w ciemno.");
    }

    [GeneratedRegex(@"^\s*CNC([AB])(\d+)\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex PortLine();
    [GeneratedRegex(@"^COM([1-9]\d*)$", RegexOptions.IgnoreCase)]
    private static partial Regex ComName();
}

public static class ArtifactIntegrity
{
    public static void Verify(byte[] data, string expectedSha256)
    {
        var actual = Convert.ToHexString(SHA256.HashData(data));
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Pobrany pakiet nie zgadza się z sumą SHA-256 producenta. Instalacja została zatrzymana.");
    }
}
