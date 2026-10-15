using System.Globalization;
using System.IO.Ports;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Zebex.Core;
using Zebex.PhoneLink;

var passed = 0;
void Assert(bool value) { if (!value) throw new Exception("Assertion failed"); }
void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception($"Expected {expected}, actual {actual}"); }
void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or OverflowException) { return; } throw new Exception("Expected rejection"); }
void Check(string name, Action action) { action(); Console.WriteLine("PASS " + name); passed++; }
PhoneUpload Upload(Guid? id = null) => new(id ?? Guid.NewGuid(), "Magazyn", "summed", [new("000123", "0.1"), new("000123", "0.2"), new("002", "-1")]);
var clientId = Guid.NewGuid();

if (args.Length == 3 && args[0] == "--fixture")
{
    var session = new CollectorSession { Deletion = DeleteAfterSending.Never }; var sync = new object();
    session.LoadProducts([new("000123", "Test produktu", 12.5M, 9.99M), new("002", "Drugi", 5, 2)]);
    using var rsa = RSA.Create(2048); var request = new CertificateRequest("CN=Phone integration", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    var access = new PhoneAccess(null, _ => { });
    var device = new ZacDevice(); Guid? active = null;
    using var serial = new SerialPort(args[2], 115200) { ReadTimeout = 100, WriteTimeout = 3000 }; serial.Open();
    device.Transmit = bytes => serial.Write(bytes, 0, bytes.Length);
    device.DocumentCompleted = snap => { session.MarkPhoneReceivedBySuz(snap); session.ConfirmVerifiedSend(snap, true); active = null; };
    device.ReceiveStarted = () => true; device.ReceiveCompleted = (_, check) => check;
    await using var server = new PhoneServer(cert, access);
    server.Upload = (owner, upload) =>
    {
        lock (sync)
        {
            var receipt = session.ImportPhoneDocument(owner, upload);
            if (active == null && receipt.Status == "queued") { active = receipt.Id; device.Arm(session.CaptureTransfer(receipt.DocumentName)); }
            return Task.FromResult(receipt);
        }
    };
    server.Status = (owner, id) => { lock (sync) return Task.FromResult(session.GetPhoneReceipt(id, owner)); };
    server.Products = (offset, limit, revision) => { lock (sync) return Task.FromResult(new PhoneProductPage(offset, session.Products.Count, session.CatalogVersion, offset + limit < session.Products.Count, session.Products.Skip(offset).Take(limit).ToArray())); };
    server.Start();
    var ip = Dns.GetHostAddresses(Dns.GetHostName()).First(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
    File.WriteAllText(args[1], JsonSerializer.Serialize(new { v = 1, url = "https://" + ip + ":" + server.Port, pin = Convert.ToHexString(SHA256.HashData(cert.RawData)), code = access.Pairing.Code }));
    Console.WriteLine("FIXTURE_READY");
    while (true)
    {
        lock (sync)
        {
            try { if (serial.BytesToRead > 0) { var bytes = new byte[serial.BytesToRead]; var n = serial.Read(bytes,0,bytes.Length); device.Feed(bytes.AsSpan(0,n), DateTime.UtcNow); } else device.Tick(DateTime.UtcNow); }
            catch (TimeoutException) { }
        }
        await Task.Delay(5);
    }
}

Check("phone import preserves exact decimals and leading zeros", () =>
{
    var s = new CollectorSession(); var r = s.ImportPhoneDocument(clientId, Upload()); Equal("queued",r.Status);
    Equal(0.3M,s.Documents.Single().Rows[0].Quantity); Equal("000123",s.Documents.Single().Rows[0].Code); Equal(-1M,s.Documents.Single().Rows[1].Quantity);
});
Check("identical upload retries create one durable document even after restart", () =>
{
    var s = new CollectorSession(); var body = Upload(); string? saved = null;
    var first = s.ImportPhoneDocument(clientId,body,json=>saved=json); Equal(first,s.ImportPhoneDocument(clientId,body)); Equal(1,s.Documents.Count);
    s = CollectorSession.FromJson(saved!); Equal(first,s.ImportPhoneDocument(clientId,body)); Equal(1,s.Documents.Count);
});
Check("save failure accepts neither receipt nor document and preserves active transfer", () =>
{
    var s = new CollectorSession { Deletion = DeleteAfterSending.Never }; s.Open("Local",ScanMode.Summed).Add("001",2); var snap=s.CaptureTransfer("Local"); var before=s.ToJson();
    Reject(()=>s.ImportPhoneDocument(clientId,Upload(),_=>throw new IOException("Disk full"))); Equal(before,s.ToJson()); Assert(s.ConfirmVerifiedSend(snap,true));
});
Check("receiving a phone document does not invalidate another document awaiting SUZ", () =>
{
    var s = new CollectorSession { Deletion = DeleteAfterSending.Never }; s.Open("Local",ScanMode.Separate).Add("001",2); var snap=s.CaptureTransfer("Local");
    s.ImportPhoneDocument(clientId,Upload(),_=>{}); Assert(s.ConfirmVerifiedSend(snap,true)); Equal(2,s.Documents.Count);
});
Check("same request with altered content or another client is rejected atomically", () =>
{
    var s=new CollectorSession();var body=Upload();s.ImportPhoneDocument(clientId,body);var before=s.ToJson();
    Reject(()=>s.ImportPhoneDocument(clientId,body with {Name="Changed"})); Reject(()=>s.ImportPhoneDocument(Guid.NewGuid(),body)); Equal(before,s.ToJson()); Assert(s.GetPhoneReceipt(body.Id,Guid.NewGuid())==null);
});
Check("equivalent decimal spellings do not create a conflict", () =>
{
    var s=new CollectorSession(); var body=Upload(); var first=s.ImportPhoneDocument(clientId,body); var retry=body with {Rows=[new("000123","0.10"),new("000123","0.200"),new("002","-1.0")]}; Equal(first,s.ImportPhoneDocument(clientId,retry));
});
Check("phone quantities reject implicit rounding including a summed precision loss", () =>
{
    Equal(1M,PhoneQuantity.Parse("1.0000000000000000000000000000000000"));
    Equal(0.3M,PhoneQuantity.Add(0.1M,0.2M));
    Reject(()=>PhoneQuantity.Parse("1.00000000000000000000000000005"));
    Reject(()=>PhoneQuantity.Add(10000000000000000000000000M,0.00001M));
    Reject(()=>PhoneQuantity.Parse("1.2.3"));Reject(()=>PhoneQuantity.Parse(" 1"));Reject(()=>PhoneQuantity.Parse("1e2"));
});
Check("phone documents are frozen and catalog reception cannot zero their quantities", () =>
{
    var s=new CollectorSession();s.ImportPhoneDocument(clientId,Upload());var doc=s.Documents[0];
    Reject(()=>doc.Add("000123",2));Reject(doc.RemoveLast);s.LoadProducts([new("000123","Product",1,1)]);Equal(0.3M,doc.Rows[0].Quantity);
});
Check("deleted phone document keeps its receipt and is not recreated by retry", () =>
{
    var s=new CollectorSession();var body=Upload();var receipt=s.ImportPhoneDocument(clientId,body);s.DeleteDocument(receipt.DocumentName);s=CollectorSession.FromJson(s.ToJson());
    Equal("removed",s.ImportPhoneDocument(clientId,body).Status);Equal(0,s.Documents.Count);
});
Check("verified SUZ delivery survives automatic deletion and retry after restart", () =>
{
    var s=new CollectorSession {Deletion=DeleteAfterSending.Automatic};var body=Upload();var r=s.ImportPhoneDocument(clientId,body);var snap=s.CaptureTransfer(r.DocumentName);
    Assert(s.MarkPhoneReceivedBySuz(snap));Assert(s.ConfirmVerifiedSend(snap,true));s=CollectorSession.FromJson(s.ToJson()); Equal("suz",s.ImportPhoneDocument(clientId,body).Status);Equal(0,s.Documents.Count);
});
Check("foreign or stale snapshot cannot mark a phone document received by SUZ", () =>
{
    var s=new CollectorSession();var r=s.ImportPhoneDocument(clientId,Upload());var snap=s.CaptureTransfer(r.DocumentName);var other=CollectorSession.FromJson(s.ToJson());Assert(!other.MarkPhoneReceivedBySuz(snap));Equal("queued",other.PhoneReceipts.Single().Status);
});
Check("malformed phone documents are rejected without partial changes", () =>
{
    var s=new CollectorSession();var before=s.ToJson();var b=Upload();
    foreach(var bad in new[]{b with {Id=Guid.Empty},b with {Name="Zażółć"},b with {Mode="unknown"},b with {Rows=[]},b with {Rows=[new("01\t02","1")]},b with {Rows=[new("001","0")]},b with {Rows=[new("001","1,5")]},b with {Rows=[new("001","79228162514264337593543950336")]},b with {Mode="quick",Rows=[new("001","2")]}}) Reject(()=>s.ImportPhoneDocument(clientId,bad));
    Equal(before,s.ToJson());
});
Check("old version1 sessions load and the new format preserves phone receipts", () =>
{
    var s=CollectorSession.FromJson("{\"Version\":1,\"Documents\":[],\"Products\":[],\"Deletion\":0,\"Feedback\":1}");var r=s.ImportPhoneDocument(clientId,Upload());Equal(r,CollectorSession.FromJson(s.ToJson()).PhoneReceipts.Single());
});
Check("catalog version changes on load and on session restart", () =>
{
    var s=new CollectorSession();var before=s.CatalogVersion;s.LoadProducts([]);Assert(before!=s.CatalogVersion);Assert(s.CatalogVersion!=CollectorSession.FromJson(s.ToJson()).CatalogVersion);
});
Check("pairing issues a random token and persists only its hash", () =>
{
    string? saved=null;var access=new PhoneAccess(null,json=>saved=json);var result=access.Pair(new(access.Pairing.Code,clientId,"Phone"),DateTime.UtcNow);
    Equal(clientId,access.Authenticate(result.Token));Equal(64,result.Token.Length);Assert(!saved!.Contains(result.Token));Equal(clientId,new PhoneAccess(saved,_=>{}).Authenticate(result.Token));
});
Check("expired and wrong pairing codes are rejected and attempts are bounded", () =>
{
    var access=new PhoneAccess(null,_=>{});Reject(()=>access.Pair(new("000000",clientId,"Phone"),DateTime.UtcNow));Reject(()=>access.Pair(new(access.Pairing.Code,clientId,"Phone"),DateTime.UtcNow.AddMinutes(6)));
    access=new PhoneAccess(null,_=>{});var now=DateTime.UtcNow;for(var i=0;i<10;i++)Reject(()=>access.Pair(new("bad",clientId,"Phone"),now));Reject(()=>access.Pair(new(access.Pairing.Code,clientId,"Phone"),now));Assert(access.Pair(new(access.Pairing.Code,clientId,"Phone"),now.AddSeconds(61)).Token.Length==64);
});
Check("repair revokes the previous token without affecting another phone", () =>
{
    var access=new PhoneAccess(null,_=>{});var code=access.Pairing.Code;var first=access.Pair(new(code,clientId,"A"),DateTime.UtcNow);var other=access.Pair(new(code,Guid.NewGuid(),"B"),DateTime.UtcNow);var again=access.Pair(new(code,clientId,"A"),DateTime.UtcNow);
    Assert(access.Authenticate(first.Token)==null);Equal(clientId,access.Authenticate(again.Token));Assert(access.Authenticate(other.Token)!=null);access.RevokeAll();Assert(access.Authenticate(again.Token)==null);
});
Check("failed pairing persistence does not issue an accepted token", () =>
{
    var access=new PhoneAccess(null,_=>throw new IOException("Disk full"));Reject(()=>access.Pair(new(access.Pairing.Code,clientId,"A"),DateTime.UtcNow));Equal(0,access.Count);
});

using(var rsa=RSA.Create(2048))
{
    var req=new CertificateRequest("CN=Phone checks",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
    using var cert=req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1));
    var access=new PhoneAccess(null,_=>{});var session=new CollectorSession();
    await using var server=new PhoneServer(cert,access,IPAddress.Loopback,0);
    server.Upload=(owner,doc)=>Task.FromResult(session.ImportPhoneDocument(owner,doc));server.Status=(owner,id)=>Task.FromResult(session.GetPhoneReceipt(id,owner));
    server.Products=(o,l,r)=>Task.FromResult(new PhoneProductPage(o,1,"revision",false,[new("000123","Towar",1,2)]));server.Start();
    using var handler=new HttpClientHandler {UseProxy=false,ServerCertificateCustomValidationCallback=(_,c,_,_)=>c!=null&&CryptographicOperations.FixedTimeEquals(SHA256.HashData(c.RawData),SHA256.HashData(cert.RawData))};
    using var http=new HttpClient(handler){BaseAddress=new Uri("https://127.0.0.1:"+server.Port),Timeout=TimeSpan.FromSeconds(5)};
    Task<HttpResponseMessage> Post<T>(string path,T value) => http.PostAsync(path,new StringContent(JsonSerializer.Serialize(value,new JsonSerializerOptions(JsonSerializerDefaults.Web)),Encoding.UTF8,"application/json"));
    Equal(HttpStatusCode.Unauthorized,(await http.GetAsync("/v1/products")).StatusCode);Console.WriteLine("PASS TLS endpoint denies unpaired requests");passed++;
    var pair=await Post("/v1/pair",new PairRequest(access.Pairing.Code,clientId,"Test Android"));pair.EnsureSuccessStatusCode();var paired=(await pair.Content.ReadFromJsonAsync<PairResult>())!;
    http.DefaultRequestHeaders.Authorization=new("Bearer",paired.Token);
    var body=Upload();var response=await Post("/v1/documents",body);response.EnsureSuccessStatusCode();var received=(await response.Content.ReadFromJsonAsync<PhoneReceipt>())!;Equal(clientId,received.ClientId);
    var retry=await Post("/v1/documents",body);retry.EnsureSuccessStatusCode();Equal(1,session.Documents.Count);Equal(0.3M,session.Documents[0].Rows[0].Quantity);Console.WriteLine("PASS real HTTPS upload and retry retain one exact document");passed++;
    var status=await http.GetFromJsonAsync<PhoneReceipt>("/v1/documents/"+body.Id);Equal("queued",status!.Status);Console.WriteLine("PASS real HTTPS receipt lookup");passed++;
    var conflict=await Post("/v1/documents",body with {Name="different"});Equal(HttpStatusCode.Conflict,conflict.StatusCode);Console.WriteLine("PASS real HTTPS changed retry reports conflict");passed++;
    var products=await http.GetFromJsonAsync<PhoneProductPage>("/v1/products?offset=0&limit=500");Equal("000123",products!.Products.Single().Code);Console.WriteLine("PASS real HTTPS catalog response preserves code text");passed++;
    var other=access.Pair(new(access.Pairing.Code,Guid.NewGuid(),"Other"),DateTime.UtcNow);http.DefaultRequestHeaders.Authorization=new("Bearer",other.Token);Equal(HttpStatusCode.NotFound,(await http.GetAsync("/v1/documents/"+body.Id)).StatusCode);Console.WriteLine("PASS real HTTPS owner isolation");passed++;
    using var wrongHandler=new HttpClientHandler{UseProxy=false,ServerCertificateCustomValidationCallback=(_,_,_,_)=>false};using var wrong=new HttpClient(wrongHandler);
    try {await wrong.GetAsync(http.BaseAddress+"v1/products");throw new Exception("Expected certificate rejection");}catch(HttpRequestException){}Console.WriteLine("PASS real HTTPS wrong certificate is rejected");passed++;
    access.RevokeAll();Equal(HttpStatusCode.Unauthorized,(await http.GetAsync("/v1/products")).StatusCode);Console.WriteLine("PASS real HTTPS revoked token cannot access catalog");passed++;
}
Console.WriteLine($"Completed: {passed} passed, 0 failed.");
