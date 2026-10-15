using System.Globalization;
using System.Numerics;

namespace Zebex.Core;

/// <summary>Reject decimal rounding rather than changing a phone's recorded quantities.</summary>
public static class PhoneQuantity
{
    private static readonly BigInteger Max = BigInteger.Parse("79228162514264337593543950335", CultureInfo.InvariantCulture);
    public static decimal Parse(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 128) throw new ArgumentException("Nieprawidłowa ilość.");
        var negative = text[0] == '-'; var digits = text[0] is '-' or '+' ? text[1..] : text;
        var point = digits.IndexOf('.'); var scale = point < 0 ? 0 : digits.Length - point - 1;
        if (point == 0 || point == digits.Length - 1 || digits.Length == 0 || digits.Where((c, i) => i != point).Any(c => c is < '0' or > '9')) throw new ArgumentException("Ilość musi być liczbą z kropką dziesiętną.");
        var mantissa = BigInteger.Parse(digits.Replace(".", ""), CultureInfo.InvariantCulture);
        return Create(negative ? -mantissa : mantissa, scale);
    }
    public static decimal Add(decimal first, decimal second)
    {
        static (BigInteger Mantissa, int Scale) Parts(decimal n)
        {
            var bits = decimal.GetBits(n);
            var mantissa = (BigInteger)(uint)bits[0] | (BigInteger)(uint)bits[1] << 32 | (BigInteger)(uint)bits[2] << 64;
            return (bits[3] < 0 ? -mantissa : mantissa, (bits[3] >> 16) & 255);
        }
        var a = Parts(first); var b = Parts(second); var scale = Math.Max(a.Scale, b.Scale);
        return Create(a.Mantissa * BigInteger.Pow(10, scale - a.Scale) + b.Mantissa * BigInteger.Pow(10, scale - b.Scale), scale);
    }
    private static decimal Create(BigInteger mantissa, int scale)
    {
        while (scale > 0 && mantissa % 10 == 0) { mantissa /= 10; scale--; }
        var abs = BigInteger.Abs(mantissa);
        if (scale > 28 || abs > Max) throw new ArgumentException("Ilość lub suma wymaga zaokrąglenia. Zmniejsz liczbę cyfr.");
        return new decimal(unchecked((int)(uint)(abs & uint.MaxValue)), unchecked((int)(uint)((abs >> 32) & uint.MaxValue)), unchecked((int)(uint)((abs >> 64) & uint.MaxValue)), mantissa.Sign < 0, (byte)scale);
    }
}
