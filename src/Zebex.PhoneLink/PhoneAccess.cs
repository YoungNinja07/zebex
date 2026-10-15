using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Zebex.PhoneLink;

public sealed record PairedPhone(Guid ClientId, string Name, string TokenHash);
public sealed record PairRequest(string Code, Guid ClientId, string Name);
public sealed record PairResult(string Token, Guid ClientId);

public sealed class PhoneAccess
{
    private readonly object sync = new();
    private readonly Action<string> persist;
    private List<PairedPhone> phones;
    private string code = "";
    private DateTime expires, attemptWindow;
    private int attempts;
    public PhoneAccess(string? state, Action<string> persist)
    {
        this.persist = persist;
        phones = state == null ? [] : JsonSerializer.Deserialize<List<PairedPhone>>(state) ?? throw new ArgumentException("Uszkodzona lista telefonów.");
        if (phones.Count > 32 || phones.Select(p => p.ClientId).Distinct().Count() != phones.Count || phones.Any(p => p.ClientId == Guid.Empty || p.TokenHash is not { Length: 64 } || p.TokenHash.Any(c => !Uri.IsHexDigit(c)))) throw new ArgumentException("Uszkodzona lista telefonów.");
        Rotate();
    }
    public (string Code, DateTime Expires) Pairing { get { lock (sync) return (code, expires); } }
    public int Count { get { lock (sync) return phones.Count; } }
    public void Rotate() { lock (sync) { code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(); expires = DateTime.UtcNow.AddMinutes(5); } }
    public void RevokeAll() { lock (sync) { persist("[]"); phones = []; Rotate(); } }
    public PairResult Pair(PairRequest request, DateTime now)
    {
        lock (sync)
        {
            if (now - attemptWindow > TimeSpan.FromMinutes(1)) { attemptWindow = now; attempts = 0; }
            if (++attempts > 10 || now >= expires || request.Code == null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(code), Encoding.UTF8.GetBytes(request.Code))) throw new UnauthorizedAccessException("Kod parowania wygasł lub jest nieprawidłowy. Odśwież QR na komputerze.");
            if (request.ClientId == Guid.Empty || string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 64 || request.Name.Any(char.IsControl)) throw new ArgumentException("Nieprawidłowa nazwa telefonu.");
            var next = phones.Where(p => p.ClientId != request.ClientId).ToList();
            if (next.Count >= 32) throw new InvalidOperationException("Limit 32 telefonów. Usuń parowania na komputerze.");
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            next.Add(new(request.ClientId, request.Name, Hash(token)));
            try { persist(JsonSerializer.Serialize(next)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException) { throw new IOException("Nie zapisano parowania na komputerze.", e); }
            phones = next;
            return new(token, request.ClientId);
        }
    }
    public Guid? Authenticate(string? token)
    {
        if (token is not { Length: 64 }) return null;
        var hash = Encoding.ASCII.GetBytes(Hash(token));
        lock (sync)
        {
            foreach (var phone in phones)
                if (CryptographicOperations.FixedTimeEquals(hash, Encoding.ASCII.GetBytes(phone.TokenHash))) return phone.ClientId;
        }
        return null;
    }
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
