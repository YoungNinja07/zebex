using System.ComponentModel;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using Zebex.Core;

namespace Zebex.Simulator;

public sealed class MainForm : Form
{
    private readonly CollectorView collector = new();
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly ComboBox ports = new() { Width = 105, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox baud = new() { Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox parity = new() { Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox bits = new() { Width = 60, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox stops = new() { Width = 80, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox handshake = new() { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox dtr = new() { Text = "DTR", AutoSize = true };
    private readonly CheckBox rts = new() { Text = "RTS", AutoSize = true };
    private readonly Button connect = new() { Text = "Połącz", AutoSize = true };
    private readonly Button createPair = new() { Text = "Utwórz parę COM", AutoSize = true, Padding = new Padding(6) };
    private readonly Label pairInfo = new() { AutoSize = true, Text = "Porty dla SUZ i symulatora można utworzyć przyciskiem obok.", Margin = new Padding(8, 10, 0, 0) };
    private readonly TextBox log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 10) };
    private readonly TextBox transmit = new() { Width = 330 };
    private readonly CheckBox hex = new() { Text = "HEX", AutoSize = true, Checked = true };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 30, Text = "  Port COM zamknięty • wybierz port symulatora i kliknij Połącz", TextAlign = ContentAlignment.MiddleLeft };
    private SerialPort? serial;
    private bool pairSetupRunning;
    private readonly ZacDevice protocol = new();
    private readonly System.Windows.Forms.Timer protocolTimer = new() { Interval = 100 };
    private readonly PhonePanel phones;
    private DateTime lastPhoneArm;

    public MainForm()
    {
        Text = "Zebex Simulator 0.4.3 — Telefon / SUZ / EKP";
        MinimumSize = new Size(1000, 700);
        Size = new Size(1280, 860);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        var heading = new Label { Text = "ZEBEX / SYMULATOR KOLEKTORA", Dock = DockStyle.Top, Height = 60, BackColor = Color.FromArgb(21, 39, 53), ForeColor = Color.White, Font = new Font("Segoe UI", 20, FontStyle.Bold), Padding = new Padding(20, 10, 0, 0) };
        var notice = new Label { Text = "SUZ 19.2: zainstaluj most SUZ w zakładce Diagnostyka, wybierz drugi port pary w SUZ i otwórz port symulatora.", Dock = DockStyle.Top, Height = 46, Padding = new Padding(20, 8, 10, 0), BackColor = Color.FromArgb(255, 237, 192) };
        var scanTab = new TabPage("Z-9000 / EKP — kolektor") { Padding = new Padding(8), AutoScroll = true };
        var serialTab = new TabPage("Diagnostyka SUZ / COM") { Padding = new Padding(16) };
        tabs.TabPages.AddRange([scanTab, serialTab]);
        phones = new PhonePanel(collector); phones.Log += AppendMessage;
        var phoneTab = new TabPage("Telefon Android / Wi-Fi"); phoneTab.Controls.Add(phones); tabs.TabPages.Add(phoneTab);
        scanTab.Controls.Add(collector);
        collector.Dock = DockStyle.None;
        scanTab.Resize += (_, _) => collector.Size = new Size(Math.Max(1080, scanTab.ClientSize.Width - 20), Math.Max(630, scanTab.ClientSize.Height - 20));
        BuildSerial(serialTab);
        protocol.Transmit = bytes => { if (serial?.IsOpen != true) throw new IOException("Port COM jest zamknięty."); serial.Write(bytes, 0, bytes.Length); AppendLog("TX", bytes); };
        protocol.ReceiveStarted = collector.PrepareAutomaticCatalogReception;
        protocol.ReceiveCompleted = collector.FinishAutomaticCatalogReception;
        protocol.DocumentCompleted = snapshot => { AppendMessage("SUZ potwierdził odbiór dokumentu: " + snapshot.Name); collector.CompleteVerifiedTransfer(snapshot); };
        protocol.Failed = reason => { AppendMessage("Transmisja: " + reason); collector.FailCommunication(reason); };
        collector.SendDocumentRequested += snapshot => { EnsureProtocolPort(); protocol.Arm(snapshot); AppendMessage("Gotowość do wysłania: " + snapshot.Name); };
        collector.ReceiveProductsRequested += _ => EnsureProtocolPort();
        collector.CommunicationCancelled += protocol.Cancel;
        protocolTimer.Tick += (_, _) => Guard(() =>
        {
            protocol.Tick(DateTime.UtcNow);
            if (phones.Running && serial?.IsOpen == true && !protocol.IsBusy && DateTime.UtcNow - lastPhoneArm > TimeSpan.FromSeconds(1))
            { lastPhoneArm = DateTime.UtcNow; collector.TryArmPhoneDocument(); }
        });
        protocolTimer.Start();
        Controls.Add(tabs); Controls.Add(notice); Controls.Add(heading); Controls.Add(status);
        FormClosing += (_, e) =>
        {
            if (pairSetupRunning) { e.Cancel = true; MessageBox.Show(this, "Najpierw zakończ konfigurację portów w oknie instalacji."); return; }
            if (collector.HasUnsavedChanges && MessageBox.Show(this, "Nie udało się zapisać bieżącej sesji. Zamknąć program bez zapisania?", "Zamknięcie", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) { e.Cancel = true; return; }
            ClosePort();
            _ = phones.StopAsync();
            protocolTimer.Stop(); protocolTimer.Dispose();
        };
        Shown += (_, _) => { var available = Screen.FromControl(this).WorkingArea; Size = new Size(Math.Min(Width, available.Width), Math.Min(Height, available.Height)); };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (tabs.SelectedIndex == 0 && collector.HandleKeyboard(keyData)) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private Button ActionButton(string title, Action action)
    {
        var button = new Button { Text = title, AutoSize = true, Padding = new Padding(6), Margin = new Padding(4) };
        button.Click += (_, _) => Guard(action);
        return button;
    }
    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception e) { MessageBox.Show(this, e.Message, "Nie udało się wykonać operacji", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 6, 0, 6), WrapContents = true };
        panel.Controls.AddRange(controls);
        return panel;
    }
    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(5, 8, 5, 0) };


    private void BuildSerial(Control parent)
    {
        baud.Items.AddRange(["1200", "2400", "4800", "9600", "19200", "38400", "57600", "115200"]); baud.SelectedItem = "115200";
        parity.Items.AddRange(Enum.GetNames<Parity>()); parity.SelectedItem = "None";
        bits.Items.AddRange(["7", "8"]); bits.SelectedItem = "8";
        stops.Items.AddRange(["One", "Two"]); stops.SelectedItem = "One";
        handshake.Items.AddRange(Enum.GetNames<Handshake>()); handshake.SelectedItem = "None";
        connect.Click += (_, _) => Guard(TogglePort);
        createPair.Click += async (_, _) => await CreatePairAsync();
        parent.Controls.Add(log);
        parent.Controls.Add(Row(transmit, hex, ActionButton("Wyślij bajty", SendBytes), ActionButton("Zapisz log", SaveLog)));
        parent.Controls.Add(Row(Caption("Sterowanie"), handshake, dtr, rts));
        parent.Controls.Add(Row(Caption("Port"), ports, ActionButton("Odśwież", RefreshPorts), Caption("Baud"), baud, Caption("Parity"), parity, Caption("Bity"), bits, Caption("Stop"), stops, connect));
        parent.Controls.Add(Row(createPair, pairInfo));
        parent.Controls.Add(Row(ActionButton("Zainstaluj most SUZ", () => RunBridgeInstaller(false)), ActionButton("Przywróć oryginalne narzędzia SUZ", () => RunBridgeInstaller(true))));
        parent.Controls.Add(new Label { Dock = DockStyle.Top, Height = 106, Text = "1. Zainstaluj most SUZ — zapisze kopie oryginałów i umożliwi pracę bez urządzenia USB.\n2. SUZ: Elzab Kolektor Plus (Zebex), drugi port pary, 115200. Symulator: własny port, 115200 / 8 / None / One.\n3. Połącz port. Test SUZ odbierany jest automatycznie. Dokument: 2. Wyślij na kolektorze, potem odbiór w nexo.\nKatalog produktów odbierany jest automatycznie; potwierdzenie następuje po przyjęciu całego katalogu." });
        RefreshPorts();
        var saved = ComPairInstaller.SavedPair();
        if (saved != null)
        {
            pairInfo.Text = $"Ostatnia para — SUZ: {saved.SuzPort} / symulator: {saved.SimulatorPort}";
            if (ports.Items.Contains(saved.SimulatorPort)) ports.SelectedItem = saved.SimulatorPort;
        }
    }

    private async Task CreatePairAsync()
    {
        if (pairSetupRunning) return;
        if (serial?.IsOpen == true) { MessageBox.Show(this, "Rozłącz port przed konfiguracją pary COM."); return; }
        if (MessageBox.Show(this, "Program zainstaluje sterownik com0com, jeśli go brakuje, i utworzy parę wolnych portów. Windows poprosi o zgodę administratora. Kontynuować?", "Tworzenie portów COM", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
        pairSetupRunning = true;
        createPair.Enabled = false;
        connect.Enabled = false;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                Arguments = "--create-com-pair", Verb = "runas", UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory
            }) ?? throw new InvalidOperationException("Nie uruchomiono konfiguratora.");
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new InvalidOperationException("Konfiguracja nie zakończyła się powodzeniem. Szczegóły były w oknie instalacji portów.");
            var pair = ComPairInstaller.SavedPair() ?? throw new InvalidOperationException("Brak zapisanego wyniku konfiguracji.");
            RefreshPorts();
            if (!ports.Items.Contains(pair.SimulatorPort)) throw new InvalidOperationException("Nowy port nie jest jeszcze widoczny. Odśwież listę po chwili lub uruchom Windows ponownie.");
            ports.SelectedItem = pair.SimulatorPort;
            pairInfo.Text = $"SUZ: {pair.SuzPort} / symulator: {pair.SimulatorPort} • test portów OK";
            AppendMessage($"Para COM gotowa: SUZ {pair.SuzPort}, symulator {pair.SimulatorPort}. Test transmisji dwukierunkowej OK.");
            MessageBox.Show(this, $"Porty są gotowe.\n\nW SUZ wybierz {pair.SuzPort}.\nW symulatorze wybrano {pair.SimulatorPort}.\n\nZainstaluj most SUZ i kliknij Połącz.", "Para COM gotowa");
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223) { pairInfo.Text = "Anulowano zgodę administratora. Nie utworzono portów."; }
        catch (Exception e) { MessageBox.Show(this, e.Message, "Konfiguracja portów COM", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { pairSetupRunning = false; createPair.Enabled = true; connect.Enabled = true; }
    }
    private void RefreshPorts()
    {
        if (serial?.IsOpen == true) return;
        var previous = ports.SelectedItem?.ToString();
        ports.Items.Clear(); ports.Items.AddRange(SerialPort.GetPortNames().Order().ToArray());
        if (previous != null && ports.Items.Contains(previous)) ports.SelectedItem = previous;
        else if (ports.Items.Count > 0) ports.SelectedIndex = 0;
    }
    private void TogglePort()
    {
        if (serial?.IsOpen == true) { ClosePort(); return; }
        if (ports.SelectedItem is not string name) throw new InvalidOperationException("Brak portu COM. Utwórz wirtualną parę portów na Windows i odśwież listę.");
        var port = new SerialPort(name, int.Parse((string)baud.SelectedItem!), Enum.Parse<Parity>((string)parity.SelectedItem!), int.Parse((string)bits.SelectedItem!), Enum.Parse<StopBits>((string)stops.SelectedItem!))
        { Handshake = Enum.Parse<Handshake>((string)handshake.SelectedItem!), DtrEnable = dtr.Checked, RtsEnable = rts.Checked, ReadTimeout = 500, WriteTimeout = 1000 };
        port.DataReceived += (_, _) =>
        {
            try
            {
                var buffer = new byte[port.BytesToRead];
                var received = port.Read(buffer, 0, buffer.Length);
                if (received > 0)
                {
                    var packet = buffer[..received]; AppendLog("RX", packet);
                    BeginInvoke((Action)(() => { if (ReferenceEquals(serial, port)) Guard(() => protocol.Feed(packet, DateTime.UtcNow)); }));
                }
            }
            catch (Exception e) { AppendMessage("COM: " + e.Message); }
        };
        try { port.Open(); serial = port; }
        catch { port.Dispose(); throw; }
        connect.Text = "Rozłącz";
        foreach (var control in new Control[] { ports, baud, parity, bits, stops, handshake, dtr, rts }) control.Enabled = false;
        status.Text = $"  Port {name} otwarty • silnik EKP / SUZ aktywny";
        AppendMessage($"Otwarto {name}, {baud.SelectedItem}/{bits.SelectedItem}/{parity.SelectedItem}/{stops.SelectedItem}");
    }
    private void ClosePort()
    {
        protocol.Cancel(); collector.CancelCommunication();
        var port = serial; serial = null;
        if (port != null) { try { port.Close(); } finally { port.Dispose(); } }
        connect.Text = "Połącz";
        foreach (var control in new Control[] { ports, baud, parity, bits, stops, handshake, dtr, rts }) control.Enabled = true;
        status.Text = "  Port COM zamknięty • wybierz port symulatora i kliknij Połącz";
    }
    private void EnsureProtocolPort()
    {
        if (serial?.IsOpen != true) throw new IOException("Najpierw otwórz port w zakładce Diagnostyka SUZ / COM.");
    }
    private void RunBridgeInstaller(bool restore)
    {
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", Arguments = restore ? "--restore-suz-bridge" : "--install-suz-bridge", WorkingDirectory = AppContext.BaseDirectory });
    }
    private void SendBytes()
    {
        if (protocol.IsBusy) throw new InvalidOperationException("Poczekaj na zakończenie transmisji, zanim wyślesz ręcznie bajty.");
        if (serial?.IsOpen != true) throw new InvalidOperationException("Najpierw połącz port COM.");
        if (!hex.Checked && transmit.Text.Any(c => c > 127)) throw new ArgumentException("Tryb tekstowy obsługuje ASCII. Dla innych bajtów użyj HEX.");
        var bytes = hex.Checked ? WireBytes.ParseHex(transmit.Text) : Encoding.ASCII.GetBytes(transmit.Text);
        if (bytes.Length == 0) throw new ArgumentException("Wpisz dane do wysłania.");
        serial.Write(bytes, 0, bytes.Length); AppendLog("TX", bytes);
    }
    private void AppendLog(string direction, byte[] bytes) => AppendMessage($"{direction} [{bytes.Length}] {WireBytes.Hex(bytes)}");
    private void AppendMessage(string message)
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        try { BeginInvoke((Action)(() => { if (log.TextLength > 500000) log.Clear(); log.AppendText($"{DateTime.Now:HH:mm:ss.fff} {message}\r\n"); })); }
        catch (InvalidOperationException) { /* Window closed while serial callback was pending. */ }
    }
    private void SaveLog()
    {
        using var dialog = new SaveFileDialog { Filter = "Log tekstowy|*.txt", FileName = "suz-com.txt" };
        if (dialog.ShowDialog(this) == DialogResult.OK) File.WriteAllText(dialog.FileName, log.Text, Encoding.UTF8);
    }
}
