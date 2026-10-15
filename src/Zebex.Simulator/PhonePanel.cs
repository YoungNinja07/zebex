using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using QRCoder;
using Zebex.PhoneLink;

namespace Zebex.Simulator;

internal sealed class PhonePanel : UserControl
{
    private readonly CollectorView collector;
    private readonly ComboBox addresses = new() { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly PictureBox qr = new() { Width = 370, Height = 370, SizeMode = PictureBoxSizeMode.CenterImage, BackColor = Color.White };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(720, 0), Text = "Włącz połączenie z telefonem, a następnie zeskanuj QR w aplikacji Android." };
    private readonly Label pairing = new() { AutoSize = true, Font = new Font("Segoe UI", 15, FontStyle.Bold) };
    private readonly Button start = new() { Text = "Włącz telefony przez Wi-Fi", AutoSize = true, Padding = new Padding(8) };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZebexSimulator", "phone-link");
    private X509Certificate2? certificate;
    private PhoneAccess? access;
    private PhoneServer? server;
    private string lastQr = "";
    public bool Running => server != null;
    public event Action<string>? Log;
    public PhonePanel(CollectorView collector)
    {
        this.collector = collector; Dock = DockStyle.Fill; AutoScroll = true;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(20) };
        layout.Controls.Add(new Label { AutoSize = true, Text = "Telefon jako kolektor • Android + wspólne Wi-Fi", Font = new Font("Segoe UI", 19, FontStyle.Bold) });
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(900, 0), Text = "1. Zainstaluj APK na Androidzie.  2. Zezwól na Wi-Fi i włącz połączenie.\n3. Wybierz adres komputera w sieci Wi-Fi i zeskanuj QR przyciskiem Połącz z komputerem w aplikacji.\n4. Po skanowaniu wyślij dokument z telefonu; w nexo uruchom odbiór danych z kolektora." });
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
        buttons.Controls.Add(start);
        var firewall = new Button { Text = "Zezwól na Wi-Fi w Windows", AutoSize = true, Padding = new Padding(8) };
        firewall.Click += (_, _) => Guard(() => Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", Arguments = "--setup-phone-network" }));
        buttons.Controls.Add(firewall);
        var refresh = new Button { Text = "Nowy kod QR", AutoSize = true, Padding = new Padding(8) };
        refresh.Click += (_, _) => { access?.Rotate(); UpdatePairing(); }; buttons.Controls.Add(refresh);
        var revoke = new Button { Text = "Rozłącz sparowane telefony", AutoSize = true, Padding = new Padding(8) };
        revoke.Click += (_, _) => Guard(() => { access?.RevokeAll(); UpdatePairing(); }); buttons.Controls.Add(revoke);
        layout.Controls.Add(buttons);
        layout.Controls.Add(addresses); layout.Controls.Add(pairing); layout.Controls.Add(qr); layout.Controls.Add(status);
        layout.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(900, 0), Text = "Komputer musi pozostać włączony, a port SUZ połączony. Połączenie telefonu jest szyfrowane i przypisane kodem QR.\nKod QR jest ważny 5 minut. Sparowany telefon nie potrzebuje kolejnego QR przy każdym uruchomieniu.\nSkany pozostają na telefonie również bez Wi-Fi. „Odebrane przez SUZ” oznacza przekazanie odczytów; wynik importu sprawdź w nexo.\nZapora dopuszcza wyłącznie lokalną podsieć w sieci Prywatnej lub Domenowej Windows." });
        Controls.Add(layout);
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up).OrderBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1))
            foreach (var ip in nic.GetIPProperties().UnicastAddresses.Select(a => a.Address).Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && IsPrivate(a)))
                if (!addresses.Items.Contains(ip.ToString())) addresses.Items.Add(ip.ToString());
        if (addresses.Items.Count > 0) addresses.SelectedIndex = 0;
        addresses.SelectedIndexChanged += (_, _) => UpdatePairing();
        start.Click += async (_, _) =>
        {
            start.Enabled = false;
            try { if (server != null) await StopAsync(); else Start(); }
            catch (Exception e) { status.Text = e.Message; Log?.Invoke("Telefon: " + e.Message); }
            finally { start.Enabled = true; }
        };
        timer.Tick += (_, _) => { if (server == null) return; if (DateTime.UtcNow >= access!.Pairing.Expires) access.Rotate(); UpdatePairing(); };
        timer.Start();
    }
    private static bool IsPrivate(IPAddress address)
    {
        var b = address.GetAddressBytes(); return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
    }
    private void Guard(Action action) { try { action(); } catch (Exception e) { status.Text = e.Message; } }
    private string? ReadProtected(string name)
    {
        var path = Path.Combine(folder, name);
        return File.Exists(path) ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser)) : null;
    }
    private void WriteProtected(string name, string text)
    {
        Directory.CreateDirectory(folder); var path = Path.Combine(folder, name);
        File.WriteAllBytes(path + ".tmp", ProtectedData.Protect(Encoding.UTF8.GetBytes(text), null, DataProtectionScope.CurrentUser)); File.Move(path + ".tmp", path, true);
    }
    private void Start()
    {
        if (addresses.SelectedItem == null) throw new IOException("Brak adresu lokalnej sieci. Połącz komputer i telefon z tym samym Wi-Fi, następnie uruchom aplikację ponownie.");
        access ??= new PhoneAccess(ReadProtected("phones.dat"), text => WriteProtected("phones.dat", text));
        if (certificate == null)
        {
            var saved = ReadProtected("certificate.dat");
            if (saved != null) certificate = new X509Certificate2(Convert.FromBase64String(saved), "", X509KeyStorageFlags.EphemeralKeySet);
            else
            {
                using var rsa = RSA.Create(2048); var req = new CertificateRequest("CN=Zebex Phone Local", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                using var generated = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
                var pfx = generated.Export(X509ContentType.Pfx, ""); WriteProtected("certificate.dat", Convert.ToBase64String(pfx));
                certificate = new X509Certificate2(pfx, "", X509KeyStorageFlags.EphemeralKeySet);
            }
        }
        var newServer = new PhoneServer(certificate, access);
        newServer.Upload = (client, upload) => OnUi(() => collector.AcceptPhoneDocument(client, upload));
        newServer.Status = (client, id) => OnUi(() => collector.PhoneDocumentStatus(client, id));
        newServer.Products = (offset, limit, revision) => OnUi(() => collector.PhoneProducts(offset, limit, revision));
        newServer.Log = text => Log?.Invoke(text);
        newServer.Start(); server = newServer; start.Text = "Zatrzymaj połączenie telefonu";
        UpdatePairing(); Log?.Invoke("Telefon: serwer HTTPS aktywny, port " + PhoneServer.DefaultPort);
    }
    private Task<T> OnUi<T>(Func<T> action)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try { BeginInvoke((Action)(() => { try { result.TrySetResult(action()); } catch (Exception e) { result.TrySetException(e); } })); }
        catch (InvalidOperationException e) { result.TrySetException(e); }
        return result.Task;
    }
    private void UpdatePairing()
    {
        if (server == null || access == null || certificate == null) return;
        var code = access.Pairing;
        var payload = System.Text.Json.JsonSerializer.Serialize(new { v = 1, url = "https://" + addresses.SelectedItem + ":" + PhoneServer.DefaultPort,
            pin = Convert.ToHexString(SHA256.HashData(certificate.RawData)), code = code.Code });
        pairing.Text = $"Kod: {code.Code} • ważny jeszcze {Math.Max(0, (int)(code.Expires - DateTime.UtcNow).TotalSeconds)} s • telefony: {access.Count}";
        if (payload == lastQr) return;
        using var generator = new QRCodeGenerator(); using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        using var encoder = new PngByteQRCode(data); using var stream = new MemoryStream(encoder.GetGraphic(5)); using var image = Image.FromStream(stream);
        var previous = qr.Image; qr.Image = new Bitmap(image); previous?.Dispose(); lastQr = payload;
        status.Text = "HTTPS aktywny. Zeskanuj QR w aplikacji Android. Przy wielu adresach wybierz adres komputera z tej samej sieci Wi-Fi co telefon.";
    }
    public async Task StopAsync()
    {
        var current = server; server = null; if (current != null) await current.DisposeAsync();
        start.Text = "Włącz telefony przez Wi-Fi"; qr.Image?.Dispose(); qr.Image = null; pairing.Text = "Połączenie zatrzymane"; lastQr = "";
    }
    protected override void Dispose(bool disposing) { if (disposing) { timer.Stop(); timer.Dispose(); _ = StopAsync(); } base.Dispose(disposing); }
}

internal sealed class PhoneNetworkSetupForm : Form
{
    private bool busy = true;
    public PhoneNetworkSetupForm()
    {
        Text = "Zebex Phone — konfiguracja Wi-Fi"; Size = new Size(650, 300);
        var output = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true }; Controls.Add(output);
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
        Shown += async (_, _) =>
        {
            try
            {
                var name = "Zebex Phone HTTPS LAN";
                async Task<bool> Run(params string[] args)
                {
                    var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netsh.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    foreach (var arg in args) start.ArgumentList.Add(arg);
                    using var p = Process.Start(start)!; var text = await p.StandardOutput.ReadToEndAsync(); var error = await p.StandardError.ReadToEndAsync(); await p.WaitForExitAsync();
                    if (p.ExitCode == 0 || args.Contains("add")) output.AppendText(text + error + "\r\n");
                    return p.ExitCode == 0;
                }
                var updated = await Run("advfirewall", "firewall", "set", "rule", "name=" + name, "new", "dir=in", "action=allow", "protocol=TCP", "localport=" + PhoneServer.DefaultPort, "program=" + Environment.ProcessPath!, "profile=private,domain", "remoteip=localsubnet");
                if (!updated && !await Run("advfirewall", "firewall", "add", "rule", "name=" + name, "dir=in", "action=allow", "protocol=TCP", "localport=" + PhoneServer.DefaultPort, "program=" + Environment.ProcessPath!, "profile=private,domain", "remoteip=localsubnet")) throw new IOException("Zapora nie przyjęła reguły. Sprawdź uprawnienia administratora.");
                output.AppendText("Gotowe. Zamknij okno i włącz połączenie telefonu. Sieć Windows musi być Prywatna lub Domenowa.");
            }
            catch (Exception e) { output.AppendText(e.Message); }
            finally { busy = false; }
        };
    }
}
