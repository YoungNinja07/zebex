using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Zebex.Core;

namespace Zebex.PhoneLink;

public sealed record PhoneProductPage(int Offset, int Total, string Revision, bool More, IReadOnlyList<Product> Products);

/// <summary>Small TLS-only LAN endpoint. Each connection handles one bounded JSON request.</summary>
public sealed class PhoneServer : IAsyncDisposable
{
    public const int DefaultPort = 18790;
    private readonly TcpListener listener;
    private readonly X509Certificate2 certificate;
    private readonly PhoneAccess access;
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim slots = new(8);
    private Task? acceptLoop;
    public Func<Guid, PhoneUpload, Task<PhoneReceipt>>? Upload { get; set; }
    public Func<Guid, Guid, Task<PhoneReceipt?>>? Status { get; set; }
    public Func<int, int, string?, Task<PhoneProductPage>>? Products { get; set; }
    public Action<string>? Log { get; set; }
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public PhoneServer(X509Certificate2 certificate, PhoneAccess access, IPAddress? address = null, int port = DefaultPort)
    { this.certificate = certificate; this.access = access; listener = new(address ?? IPAddress.Any, port); }
    public void Start() { listener.Start(8); acceptLoop = Task.Run(Accept); }
    /// <summary>Exercise the actual listener and private key before showing pairing QR.</summary>
    public async Task VerifyTlsAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, Port, timeout.Token);
        var expected = SHA256.HashData(certificate.RawData);
        await using var tls = new SslStream(client.GetStream(), false, (_, peer, _, _) =>
            peer != null && CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(peer.GetRawCertData())));
        await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        { TargetHost = "localhost", EnabledSslProtocols = SslProtocols.Tls12 }, timeout.Token);
    }
    private async Task Accept()
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(stop.Token); }
            catch (Exception e) when (e is OperationCanceledException or SocketException && stop.IsCancellationRequested) { break; }
            if (!await slots.WaitAsync(0)) { client.Dispose(); continue; }
            _ = Handle(client);
        }
    }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 16 };
    private async Task Handle(TcpClient client)
    {
        using (client)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            await using var tls = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            try
            {
                var protocols = SslProtocols.Tls12;
                if (!OperatingSystem.IsWindows() || OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) protocols |= SslProtocols.Tls13;
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate, EnabledSslProtocols = protocols }, timeout.Token);
                await Route(tls, timeout.Token);
            }
            catch (AuthenticationException e) { Log?.Invoke("Telefon TLS: " + e.Message + (e.InnerException == null ? "" : " / " + e.InnerException.GetType().Name + ": " + e.InnerException.Message)); }
            catch (Exception e) when (e is IOException or SocketException or OperationCanceledException) { Log?.Invoke("Telefon: połączenie zakończone (" + e.GetType().Name + ")."); }
            catch (Exception e) { Log?.Invoke("Telefon: " + e.Message); }
            finally { slots.Release(); }
        }
    }
    private async Task Route(SslStream tls, CancellationToken cancellation)
    {
        var raw = new List<byte>(); var single = new byte[1];
        while (raw.Count < 16384)
        {
            if (await tls.ReadAsync(single, cancellation) == 0) return;
            raw.Add(single[0]);
            if (raw.Count >= 4 && raw[^4] == 13 && raw[^3] == 10 && raw[^2] == 13 && raw[^1] == 10) break;
        }
        var lines = Encoding.ASCII.GetString(raw.ToArray()).Split("\r\n");
        var request = lines[0].Split(' '); var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (raw.Count >= 16384 || request.Length != 3 || request[2] != "HTTP/1.1" || !request[1].StartsWith('/') || request[1].StartsWith("//")) { await Reply(tls, 400, new { error = "Nieprawidłowe żądanie." }, cancellation); return; }
        foreach (var line in lines.Skip(1).Where(l => l.Length > 0))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0 || !headers.TryAdd(line[..colon].Trim(), line[(colon + 1)..].Trim())) { await Reply(tls, 400, new { error = "Nieprawidłowe nagłówki." }, cancellation); return; }
        }
        if (headers.ContainsKey("Transfer-Encoding") || headers.ContainsKey("Origin")) { await Reply(tls, 400, new { error = "Użyj aplikacji Android." }, cancellation); return; }
        var length = 0;
        if (headers.TryGetValue("Content-Length", out var contentLength) && (!int.TryParse(contentLength, out length) || length < 0 || length > 2097152)) { await Reply(tls, 413, new { error = "Za duży dokument." }, cancellation); return; }
        if (request[0] == "POST" && (!headers.ContainsKey("Content-Length") || !headers.GetValueOrDefault("Content-Type", "").StartsWith("application/json", StringComparison.OrdinalIgnoreCase))) { await Reply(tls, 400, new { error = "Oczekiwano JSON." }, cancellation); return; }
        var uri = new Uri("https://localhost" + request[1]);
        Guid? clientId = null;
        if (uri.AbsolutePath != "/v1/pair")
        {
            var auth = headers.GetValueOrDefault("Authorization", "");
            clientId = access.Authenticate(auth.StartsWith("Bearer ", StringComparison.Ordinal) ? auth[7..] : null);
            if (clientId == null) { await Reply(tls, 401, new { error = "Sparuj telefon z komputerem." }, cancellation); return; }
        }
        var body = new byte[length];
        await tls.ReadExactlyAsync(body, cancellation);
        try
        {
            object result;
            if (request[0] == "POST" && uri.AbsolutePath == "/v1/pair")
                result = access.Pair(JsonSerializer.Deserialize<PairRequest>(body, Json) ?? throw new ArgumentException("Brak danych parowania."), DateTime.UtcNow);
            else if (request[0] == "POST" && uri.AbsolutePath == "/v1/documents")
                result = await (Upload ?? throw new IOException("Odbiór niedostępny."))(clientId!.Value, JsonSerializer.Deserialize<PhoneUpload>(body, Json) ?? throw new ArgumentException("Brak dokumentu."));
            else if (request[0] == "GET" && uri.AbsolutePath.StartsWith("/v1/documents/", StringComparison.Ordinal) && Guid.TryParse(uri.AbsolutePath[14..], out var id))
            {
                var found = await (Status ?? throw new IOException("Status niedostępny."))(clientId!.Value, id);
                if (found == null) { await Reply(tls, 404, new { error = "Dokument nie został przyjęty przez ten komputer." }, cancellation); return; }
                result = found;
            }
            else if (request[0] == "GET" && uri.AbsolutePath == "/v1/products")
            {
                var q = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "");
                if (!int.TryParse(q.GetValueOrDefault("offset", "0"), out var offset) || !int.TryParse(q.GetValueOrDefault("limit", "500"), out var limit) || offset < 0 || limit is < 1 or > 1000) throw new ArgumentException("Nieprawidłowa strona katalogu.");
                result = await (Products ?? throw new IOException("Katalog niedostępny."))(offset, limit, q.GetValueOrDefault("revision"));
            }
            else { await Reply(tls, 404, new { error = "Nieznana operacja." }, cancellation); return; }
            await Reply(tls, 200, result, cancellation);
        }
        catch (UnauthorizedAccessException e) { await Reply(tls, 401, new { error = e.Message }, cancellation); }
        catch (Exception e) when (e is ArgumentException or JsonException or FormatException or OverflowException) { await Reply(tls, 400, new { error = "Błędne dane: " + e.Message }, cancellation); }
        catch (InvalidOperationException e) { await Reply(tls, 409, new { error = e.Message }, cancellation); }
        catch (IOException e) { Log?.Invoke(e.Message); await Reply(tls, 503, new { error = "Komputer nie zapisał danych. Dokument pozostaje na telefonie." }, cancellation); }
    }
    private static async Task Reply(Stream stream, int status, object value, CancellationToken cancellation)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Result\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
        await stream.WriteAsync(header, cancellation); await stream.WriteAsync(body, cancellation); await stream.FlushAsync(cancellation);
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); listener.Stop();
        if (acceptLoop != null) await acceptLoop;
        // Requests observe stop.Token; keep their semaphore/certificate alive until they unwind.
    }
}
