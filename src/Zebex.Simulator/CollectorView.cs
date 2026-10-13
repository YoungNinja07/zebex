using System.Globalization;
using System.Media;
using System.Text;
using Zebex.Core;

namespace Zebex.Simulator;

internal sealed class CollectorView : UserControl
{
    private enum Page { Home, ScanMenu, SumStart, Scan, Other, Settings, DeleteSetting, FeedbackSetting, BrowseMenu, Documents, Products, DeleteMenu, SendMenu, Sending, Load, Test }
    private sealed record Option(int Digit, string Label, Action Action);
    private CollectorSession session = new();
    private CollectorDocument? current;
    private readonly HandheldControl device = new() { Dock = DockStyle.Fill };
    private readonly TextBox code = new() { Width = 260, MaxLength = 128 };
    private readonly TextBox quantity = new() { Width = 90, Text = "1" };
    private readonly ComboBox docs = new() { Width = 280, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DataGridView grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
    private readonly Label help = new() { Dock = DockStyle.Top, Height = 76, Padding = new Padding(4), Text = "Wybierz opcję klawiszami kolektora." };
    private readonly Label detail = new() { Dock = DockStyle.Top, Height = 42, Font = new Font("Segoe UI", 13, FontStyle.Bold) };
    private readonly Label message = new() { AutoSize = true, MaximumSize = new Size(700, 70), Text = "Menu i działanie oparte na dostarczonej instrukcji EKP." };
    private readonly string stateFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZebexSimulator", "session.json");
    private Page page = Page.Home;
    private int selected;
    private bool powered = true, fn, alpha, refreshing, canAutoSave = true;
    private bool enteringQuantity;
    private string lastCode = "";
    private string lastError = "";
    private DateTime lastTap;
    private string lastDigit = "";
    private int tapIndex;
    private TextBox? lastTypedInto;
    private readonly System.Windows.Forms.Timer feedbackTimer = new() { Interval = 500 };
    private string flashText = "";
    private string communicationStatus = "Oczekiwanie na PC";
    private DocumentTransferSnapshot? pendingSend;
    private CatalogTransferBaseline? pendingReceive;
    public bool HasUnsavedChanges { get; private set; }
    public event Action<DocumentTransferSnapshot>? SendDocumentRequested;
    public event Action<CatalogTransferBaseline>? ReceiveProductsRequested;
    public event Action? CommunicationCancelled;

    public CollectorView()
    {
        Dock = DockStyle.Fill; BackColor = Color.FromArgb(238, 240, 243);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(device, 0, 0);
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 6, 8, 6) };
        right.Controls.Add(grid);
        right.Controls.Add(Row(message));
        right.Controls.Add(Row(ActionButton("Eksport CSV", () => Export(';')), ActionButton("Eksport TSV", () => Export('\t'))));
        right.Controls.Add(Row(ActionButton("SCAN", ScanCode), ActionButton("Zatwierdź ilość", CommitScan), ActionButton("Lokalny katalog CSV", LoadProducts), ActionButton("Przykładowy katalog", ExampleProducts)));
        right.Controls.Add(Row(Caption("Kod"), code, Caption("Ilość"), quantity));
        right.Controls.Add(Row(Caption("Dokument"), docs, ActionButton("Zapisz sesję", SaveSession), ActionButton("Wczytaj sesję", LoadSession), ActionButton("Reset systemu", Reset)));
        right.Controls.Add(help); right.Controls.Add(detail);
        layout.Controls.Add(right, 1, 0); Controls.Add(layout);
        device.DeviceKey += key => Guard(() => Key(key));
        code.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Guard(ScanCode); } };
        quantity.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Guard(CommitScan); } };
        code.GotFocus += (_, _) => enteringQuantity = false;
        quantity.GotFocus += (_, _) => enteringQuantity = true;
        docs.SelectedIndexChanged += (_, _) =>
        {
            if (refreshing || docs.SelectedItem is not string name) return;
            CancelCommunication();
            current = session.Documents.FirstOrDefault(d => d.Name == name); page = Page.Scan; selected = 0; enteringQuantity = false; code.Clear(); quantity.Text = "1"; RefreshView(); code.Focus();
        };
        feedbackTimer.Tick += (_, _) => { feedbackTimer.Stop(); flashText = ""; RefreshDisplay(); };
        if (File.Exists(stateFile))
        {
            try { session = CollectorSession.FromJson(File.ReadAllText(stateFile)); }
            catch (Exception e) { canAutoSave = false; lastError = "Nie wczytano poprzedniej sesji: " + e.Message + " Plik zachowano. Zapis automatyczny jest wstrzymany."; }
        }
        RefreshView();
    }
    private Button ActionButton(string label, Action action)
    {
        var button = new Button { Text = label, AutoSize = true, Padding = new Padding(5), Margin = new Padding(3) };
        button.Click += (_, _) => Guard(action); return button;
    }
    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 5, 0, 5) }; row.Controls.AddRange(controls); return row;
    }
    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 7, 3, 0) };
    private void Guard(Action action) { try { action(); } catch (Exception e) { MessageBox.Show(this, e.Message, "Kolektor", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
    private void Navigate(Page next)
    {
        CancelCommunication();
        if (next == Page.Load && !ConfirmCatalogChange()) return;
        page = next; selected = 0; fn = false; enteringQuantity = false; feedbackTimer.Stop(); flashText = ""; RefreshView();
        if (next == Page.Sending) BeginSend();
        else if (next == Page.Load) BeginReceiveProducts();
    }
    private void Shortcut(int number)
    {
        switch (number) { case 1: Navigate(Page.ScanMenu); break; case 2: StartSend(); break; case 3: Navigate(Page.Load); break; case 4: Navigate(Page.BrowseMenu); break; case 5: Navigate(Page.DeleteMenu); break; }
    }
    private List<Option> Options() => page switch
    {
        Page.Home => [new(1, "Skanuj", () => Navigate(Page.ScanMenu)), new(2, "Wyślij", StartSend), new(3, "Inne", () => Navigate(Page.Other))],
        Page.ScanMenu => [new(1, "Skanuj", () => Open("Skanuj", ScanMode.Separate)), new(2, "Skanuj+", () => { current = session.Open("Skanuj+", ScanMode.Summed); Persist(); Navigate(Page.SumStart); }), new(3, "Szybki skan", () => Open("Szybki skan", ScanMode.Quick)), new(4, "Dokumenty", () => NewDocument(null)), new(6, "Dok.1 (PZ)", () => NewDocument("Dok.1 (PZ)")), new(7, "Dok.2", () => NewDocument("Dok.2")), new(8, "Dok.3", () => NewDocument("Dok.3"))],
        Page.SumStart => [new(1, "Kontynuuj skanowanie", () => Navigate(Page.Scan)), new(2, "Wczytaj produkty", () => Navigate(Page.Load)), new(3, "Usuń produkty / zeruj", DeleteProducts)],
        Page.Other => [new(1, "Ustawienia", () => Navigate(Page.Settings)), new(2, "Przegląd", () => Navigate(Page.BrowseMenu)), new(3, "Usuwanie", () => Navigate(Page.DeleteMenu)), new(4, "Test skanera", () => Navigate(Page.Test)), new(6, "Ładuj aplikację", () => MessageBox.Show(this, "Według instrukcji aplikacja jest ładowana fabrycznie; ta opcja jest niedostępna.")), new(8, "Ładuj produkty", () => Navigate(Page.Load))],
        Page.Settings => [new(3, "Transmisja: 115200", () => MessageBox.Show(this, "Instrukcja EKP wymaga prędkości 115200. Ustaw taką samą prędkość w SUZ i diagnostyce COM.")), new(5, "Usuwanie po wysłaniu", () => Navigate(Page.DeleteSetting)), new(6, "Co pokazać po skanie", () => Navigate(Page.FeedbackSetting))],
        Page.DeleteSetting => [new(1, "Ręcznie", () => SetDeletion(DeleteAfterSending.Manual)), new(2, "Automatycznie", () => SetDeletion(DeleteAfterSending.Automatic)), new(3, "Nie usuwaj", () => SetDeletion(DeleteAfterSending.Never))],
        Page.FeedbackSetting => [new(1, "Nie pokazuj", () => SetFeedback(ScanFeedback.None)), new(2, "Pokaż ilość", () => SetFeedback(ScanFeedback.Count)), new(3, "Pokaż dane", () => SetFeedback(ScanFeedback.Data))],
        Page.BrowseMenu => [new(1, "Dokumenty", () => Navigate(Page.Documents)), new(2, "Produkty", () => Navigate(Page.Products))],
        Page.Documents => session.Documents.Select((d, i) => new Option(i + 1, d.Name, () => { current = d; Navigate(Page.Scan); })).ToList(),
        Page.SendMenu => session.Documents.Where(d => d.Rows.Count > 0).Select((d, i) => new Option(i + 1, d.Name, () => { current = d; Navigate(Page.Sending); })).ToList(),
        Page.DeleteMenu => [new(1, "Usuń dokument", DeleteCurrent), new(2, "Usuń ostatnią pozycję", RemoveLast), new(3, "Usuń produkty / zeruj", DeleteProducts)],
        _ => []
    };
    private void Key(string key)
    {
        if (key == "ON/OFF") { CancelCommunication(); powered = !powered; code.Clear(); quantity.Text = "1"; fn = false; enteringQuantity = false; feedbackTimer.Stop(); flashText = ""; page = Page.Home; selected = 0; RefreshView(); return; }
        if (!powered) return;
        if (key == "FN") { fn = !fn; RefreshDisplay(); return; }
        if (key == "α") { alpha = !alpha; lastDigit = ""; RefreshDisplay(); return; }
        if (key.Length == 1 && char.IsDigit(key[0]) && fn) { fn = false; Shortcut(key[0] - '0'); return; }
        if (key == "ESC")
        {
            code.Clear(); quantity.Text = "1";
            Navigate(page switch { Page.Scan or Page.SumStart => Page.ScanMenu, Page.Home or Page.Other or Page.ScanMenu or Page.SendMenu => Page.Home, Page.Sending => Page.SendMenu, Page.DeleteSetting or Page.FeedbackSetting => Page.Settings, Page.Documents or Page.Products => Page.BrowseMenu, _ => Page.Other }); return;
        }
        if (key is "▲" or "▼")
        {
            if (page is Page.Scan or Page.Test) { if (!enteringQuantity && page == Page.Scan && current?.Mode != ScanMode.Quick) quantity.Focus(); else code.Focus(); }
            else if (Options().Count > 0) selected = (selected + (key == "▲" ? -1 : 1) + Options().Count) % Options().Count;
            RefreshDisplay(); return;
        }
        if (key == "ENTER")
        {
            if (page == Page.Scan) { if (!enteringQuantity) ScanCode(); else CommitScan(); }
            else if (page == Page.Test) ScanCode();
            else if (page == Page.Load) BeginReceiveProducts();
            else if (page == Page.Sending) BeginSend();
            else if (Options().Count > 0) Options()[Math.Min(selected, Options().Count - 1)].Action();
            return;
        }
        if (key == "SCAN")
        {
            if (page is Page.Scan or Page.Test) ScanCode();
            else if (Options().Count > 0) Options()[Math.Min(selected, Options().Count - 1)].Action();
            return;
        }
        if (page is Page.Scan or Page.Test)
        {
            var input = enteringQuantity && page == Page.Scan ? quantity : code;
            if (key == "BS") { if (input.SelectionLength > 0) input.SelectedText = ""; else if (input.Text.Length > 0) input.Text = input.Text[..^1]; }
            else if (key.Length == 1 && char.IsDigit(key[0]))
            {
                var symbols = key switch { "1" => "-./1", "2" => "ABC2", "3" => "DEF3", "4" => "GHI4", "5" => "JKL5", "6" => "MNO6", "7" => "PQRS7", "8" => "TUV8", "9" => "WXYZ9", _ => ". 0" };
                if (alpha)
                {
                    var repeated = ReferenceEquals(lastTypedInto, input) && lastDigit == key && (DateTime.Now - lastTap).TotalMilliseconds < 900 && input.Text.Length > 0;
                    tapIndex = repeated ? (tapIndex + 1) % symbols.Length : 0;
                    if (repeated) input.Text = input.Text[..^1];
                    if (repeated) input.SelectionStart = input.TextLength;
                    input.SelectedText = symbols[tapIndex].ToString();
                }
                else input.SelectedText = key;
                lastDigit = key; lastTap = DateTime.Now; lastTypedInto = input;
            }
            input.Focus(); input.SelectionStart = input.TextLength; return;
        }
        if (key.Length == 1 && char.IsDigit(key[0])) Options().FirstOrDefault(o => o.Digit == key[0] - '0')?.Action();
    }
    public bool HandleKeyboard(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if (keyData == (Keys.Control | Keys.R)) { Guard(Reset); return true; }
        if (key is >= Keys.F1 and <= Keys.F5) { if (powered) Guard(() => Shortcut(key - Keys.F1 + 1)); return true; }
        if (key == Keys.Escape) { Guard(() => Key("ESC")); return true; }
        if (key is Keys.Up or Keys.Down) { Guard(() => Key(key == Keys.Up ? "▲" : "▼")); return true; }
        if (!code.Focused && !quantity.Focused)
        {
            if (key == Keys.Enter) { Guard(() => Key("ENTER")); return true; }
            if (key is >= Keys.D0 and <= Keys.D9) { Guard(() => Key(((int)key - (int)Keys.D0).ToString())); return true; }
            if (key is >= Keys.NumPad0 and <= Keys.NumPad9) { Guard(() => Key(((int)key - (int)Keys.NumPad0).ToString())); return true; }
        }
        return false;
    }
    private void Open(string name, ScanMode mode) { CancelCommunication(); current = session.Open(name, mode); code.Clear(); lastCode = ""; quantity.Text = "1"; Persist(); Navigate(Page.Scan); code.Focus(); }
    private void NewDocument(string? fixedName)
    {
        var existing = session.Documents.FirstOrDefault(d => d.Name == fixedName);
        if (existing != null) { current = existing; Navigate(Page.Scan); code.Focus(); return; }
        using var dialog = new DocumentDialog(fixedName);
        if (dialog.ShowDialog(this) == DialogResult.OK) Open(dialog.DocumentName, dialog.Mode);
    }
    private void ScanCode()
    {
        if (!powered || page is not (Page.Scan or Page.Test)) throw new InvalidOperationException("Najpierw wybierz 1. Skanuj i tryb pracy na ekranie kolektora.");
        lastCode = CollectorDocument.ValidateCode(code.Text);
        SystemSounds.Beep.Play();
        if (page == Page.Test) { message.Text = "Test skanera: " + lastCode; RefreshDisplay(); return; }
        if (current?.Mode == ScanMode.Quick) { quantity.Text = "1"; CommitScan(); }
        else { quantity.Focus(); quantity.SelectAll(); RefreshDisplay(); }
    }
    private void CommitScan()
    {
        if (!powered || page != Page.Scan || current == null) throw new InvalidOperationException("Najpierw wybierz dokument i tryb skanowania.");
        var scanned = CollectorDocument.ValidateCode(code.Text);
        var amount = 1M;
        if (current.Mode != ScanMode.Quick && !decimal.TryParse(quantity.Text.Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount)) throw new ArgumentException("Wpisz ilość, np. 1, 2.5 lub -1 dla korekty Skanuj+.");
        CancelCommunication(); current.Add(scanned, amount); lastCode = scanned;
        message.Text = session.Feedback switch { ScanFeedback.None => "Odczyt zapisany.", ScanFeedback.Count => $"poz: {current.Rows.Count}", _ => $"{scanned} / ilość: {(current.Mode == ScanMode.Quick ? 1 : amount)}" };
        code.Clear(); quantity.Text = "1"; Persist(); RefreshView(); code.Focus();
        flashText = session.Feedback switch { ScanFeedback.None => "", ScanFeedback.Count => $"ODCZYT ZAPISANY\n\npoz: {current.Rows.Count}", _ => $"ODCZYT ZAPISANY\n\n{scanned}\nIlość: {(current.Mode == ScanMode.Quick ? 1 : amount)}" };
        if (flashText.Length > 0) { feedbackTimer.Stop(); feedbackTimer.Start(); RefreshDisplay(); }
    }
    private void SetDeletion(DeleteAfterSending value) { CancelCommunication(); session.Deletion = value; Persist(); Navigate(Page.Settings); }
    private void SetFeedback(ScanFeedback value) { CancelCommunication(); session.Feedback = value; Persist(); Navigate(Page.Settings); }
    private void Reset() { CancelCommunication(); session.Deletion = DeleteAfterSending.Manual; session.Feedback = ScanFeedback.Count; powered = true; alpha = fn = false; code.Clear(); quantity.Text = "1"; Persist(); Navigate(Page.Home); message.Text = "Reset systemu: ustawienia domyślne, dokumenty i produkty zachowane."; }
    private void DeleteProducts()
    {
        CancelCommunication();
        if (MessageBox.Show(this, "Usunąć produkty i wyzerować ilości sumaryczne? Zwykłe dokumenty pozostaną bez zmian.", "Usuwanie produktów", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        session.DeleteProducts(); Persist(); Navigate(Page.ScanMenu); message.Text = "Produkty usunięte. Ilości sumaryczne wyzerowano.";
    }
    private void DeleteCurrent()
    {
        if (current == null) throw new InvalidOperationException("Wybierz dokument z listy po prawej.");
        CancelCommunication();
        if (MessageBox.Show(this, "Usunąć dokument „" + current.Name + "” wraz z pozycjami?", "Usuwanie", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        session.DeleteDocument(current.Name); current = null; Persist(); RefreshView();
    }
    private void RemoveLast() { if (current == null) throw new InvalidOperationException("Wybierz dokument z listy po prawej."); CancelCommunication(); current.RemoveLast(); Persist(); RefreshView(); }
    private void LoadProducts()
    {
        if (!powered) throw new InvalidOperationException("Włącz kolektor.");
        CancelCommunication();
        using var dialog = new OpenFileDialog { Filter = "Katalog produktów CSV|*.csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var products = ProductCsv.Parse(File.ReadAllText(dialog.FileName));
        if (!ConfirmCatalogChange()) return;
        session.LoadProducts(products); Persist(); message.Text = $"Wczytano {products.Count} produktów. Ilości sumaryczne wyzerowane. To lokalny katalog, nie transmisja z SUZ."; Navigate(Page.Products);
    }
    private bool ConfirmCatalogChange() => !session.Documents.Any(d => d.Mode == ScanMode.Summed && d.Rows.Any(r => r.Quantity != 0)) || MessageBox.Show(this, "Wczytanie produktów wyzeruje istniejące ilości sumaryczne, zgodnie z instrukcją. Kontynuować?", "Ładuj produkty", MessageBoxButtons.YesNo) == DialogResult.Yes;
    private void ExampleProducts()
    {
        if (!powered) throw new InvalidOperationException("Włącz kolektor.");
        CancelCommunication();
        if (!ConfirmCatalogChange()) return;
        session.LoadProducts([new("5901234123457", "Towar demonstracyjny", 12, 9.99M), new("000123", "Kod z zerami", 5, 24.50M)]);
        Persist(); Navigate(Page.Products); message.Text = "Wczytano przykładowy katalog. To dane demonstracyjne.";
    }
    private void StartSend()
    {
        if (!powered) throw new InvalidOperationException("Włącz kolektor.");
        var available = session.Documents.Where(d => d.Rows.Count > 0).ToArray();
        if (available.Length == 1) { current = available[0]; Navigate(Page.Sending); }
        else Navigate(Page.SendMenu);
    }
    private void BeginSend()
    {
        if (!powered || page != Page.Sending || current == null || pendingSend != null) return;
        pendingSend = session.CaptureTransfer(current.Name);
        ShowCommunicationStatus("Dokument gotowy. Uruchom odbiór odczytów w Subiekcie przez SUZ.");
        try { SendDocumentRequested?.Invoke(pendingSend); }
        catch { CancelCommunication(); throw; }
    }
    private void BeginReceiveProducts()
    {
        if (!powered || page != Page.Load || pendingReceive != null) return;
        pendingReceive = session.CaptureCatalogBaseline();
        ShowCommunicationStatus("Kolektor gotowy. Uruchom wysyłanie produktów w Subiekcie przez SUZ.");
        try { ReceiveProductsRequested?.Invoke(pendingReceive); }
        catch { CancelCommunication(); throw; }
    }

    /// <summary>Called by the protocol bridge on the UI thread after complete verified remote acceptance.</summary>
    public void CompleteVerifiedTransfer(DocumentTransferSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!ReferenceEquals(pendingSend, snapshot)) { session.CancelTransfer(snapshot); return; }
        var live = session.Documents.FirstOrDefault(d => d.DocumentId == snapshot.DocumentId);
        if (snapshot.SessionId != session.SessionId || live == null || live.Revision != snapshot.Revision)
        {
            session.CancelTransfer(snapshot); pendingSend = null;
            Navigate(Page.Home); ShowCommunicationStatus("Odczyty zmieniły się podczas transmisji. Dokument zachowano bez usuwania."); return;
        }
        var delete = snapshot.DeletionPolicy == DeleteAfterSending.Manual
            && MessageBox.Show(this, "SUZ potwierdził odbiór dokumentu „" + snapshot.Name + "”. Usunąć go z kolektora?", "Odbiór potwierdzony", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        // A serial failure or cancellation may arrive while the manual-deletion dialog pumps messages.
        if (!ReferenceEquals(pendingSend, snapshot)) { session.CancelTransfer(snapshot); return; }
        var accepted = session.ConfirmVerifiedSend(snapshot, true, delete); pendingSend = null;
        if (current != null && !session.Documents.Contains(current)) current = null;
        if (accepted) Persist();
        Navigate(Page.Home);
        ShowCommunicationStatus(accepted ? "SUZ potwierdził odbiór dokumentu „" + snapshot.Name + "”." : "Nie zastosowano starego potwierdzenia. Dane kolektora zachowano.");
    }

    /// <summary>Called only after a complete, verified product transfer; rejected or stale input preserves data.</summary>
    public bool CompleteVerifiedCatalog(CatalogTransferBaseline baseline, IEnumerable<Product> products)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        if (!ReferenceEquals(pendingReceive, baseline) || baseline.SessionId != session.SessionId) return false;
        CatalogTransferSnapshot? staged = null;
        var previous = session.ToJson(); var previousName = current?.Name;
        var committed = false;
        try
        {
            if (!canAutoSave) throw new IOException("Zapis sesji jest wstrzymany. Najpierw wczytaj lub zresetuj sesję.");
            staged = session.StageCatalog(products, baseline);
            var accepted = session.CommitVerifiedCatalog(staged, true); pendingReceive = null;
            if (!accepted) { Navigate(Page.Home); ShowCommunicationStatus("Dane zmieniły się podczas odbioru. Poprzedni katalog i odczyty zachowano."); return false; }
            committed = true; Persist();
            if (HasUnsavedChanges) throw new IOException("Nie udało się trwale zapisać katalogu. Poprzednie dane zachowano.");
            Navigate(Page.Products);
            ShowCommunicationStatus($"Odebrano {staged.Products.Count} produktów przez SUZ. Ilości sumaryczne wyzerowano."); return true;
        }
        catch (Exception e)
        {
            if (committed) { session = CollectorSession.FromJson(previous); current = session.Documents.FirstOrDefault(d => d.Name == previousName); HasUnsavedChanges = true; }
            if (staged != null) session.CancelCatalog(staged);
            FailCommunication("Katalog nie został zapisany. Dane zachowano. " + e.Message); return false;
        }
    }

    public void CancelCommunication()
    {
        var wasActive = pendingSend != null || pendingReceive != null;
        if (pendingSend != null) session.CancelTransfer(pendingSend);
        pendingSend = null; pendingReceive = null;
        if (!wasActive) return;
        communicationStatus = "Anulowano; dane zachowano";
        message.Text = "Transmisja anulowana. Dane kolektora zachowano.";
        CommunicationCancelled?.Invoke(); RefreshDisplay();
    }

    public void FailCommunication(string failure)
    {
        CancelCommunication();
        if (page is Page.Sending or Page.Load) Navigate(Page.Home);
        ShowCommunicationStatus(failure);
    }

    public void ShowCommunicationStatus(string text)
    {
        communicationStatus = text; message.Text = text; RefreshDisplay();
    }

    public bool PrepareAutomaticCatalogReception()
    {
        if (!powered) return false;
        pendingReceive ??= session.CaptureCatalogBaseline();
        ShowCommunicationStatus("Odbiór katalogu / test połączenia SUZ…");
        return true;
    }
    public bool FinishAutomaticCatalogReception(IReadOnlyList<Product> products, bool connectionCheck)
    {
        if (pendingReceive == null || !powered) return false;
        if (connectionCheck)
        {
            pendingReceive = null;
            ShowCommunicationStatus("Test połączenia SUZ OK. Katalog i odczyty zachowano.");
            return true;
        }
        if (pendingSend != null) { pendingReceive = null; ShowCommunicationStatus("Najpierw zakończ wysyłanie dokumentu. Katalog i odczyty zachowano."); return false; }
        return CompleteVerifiedCatalog(pendingReceive, products);
    }
    private void Export(char separator)
    {
        if (current == null) throw new InvalidOperationException("Wybierz dokument do eksportu.");
        using var dialog = new SaveFileDialog { Filter = separator == ';' ? "CSV|*.csv" : "TSV|*.tsv", FileName = "odczyty" + (separator == ';' ? ".csv" : ".tsv") };
        if (dialog.ShowDialog(this) == DialogResult.OK) File.WriteAllText(dialog.FileName, current.Export(separator), new UTF8Encoding(true));
    }
    private void Persist()
    {
        HasUnsavedChanges = true;
        if (!canAutoSave) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
            File.WriteAllText(stateFile + ".tmp", session.ToJson(), Encoding.UTF8);
            File.Move(stateFile + ".tmp", stateFile, true); HasUnsavedChanges = false;
        }
        catch (Exception e) { lastError = "Zapis automatyczny nie powiódł się: " + e.Message; message.Text = lastError; }
    }
    private void SaveSession()
    {
        using var dialog = new SaveFileDialog { Filter = "Sesja kolektora|*.json", FileName = "sesja-zebex.json" };
        if (dialog.ShowDialog(this) == DialogResult.OK) { File.WriteAllText(dialog.FileName, session.ToJson(), Encoding.UTF8); HasUnsavedChanges = false; }
    }
    private void LoadSession()
    {
        CancelCommunication();
        using var dialog = new OpenFileDialog { Filter = "Sesja kolektora|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var restored = CollectorSession.FromJson(File.ReadAllText(dialog.FileName));
        if (MessageBox.Show(this, "Zastąpić bieżącą sesję wczytaną sesją?", "Sesja", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        session = restored; current = null; Persist(); Navigate(Page.Home);
    }
    private void RefreshView()
    {
        refreshing = true;
        try
        {
            docs.Items.Clear(); docs.Items.AddRange(session.Documents.Select(d => d.Name).ToArray());
            if (current != null && docs.Items.Contains(current.Name)) docs.SelectedItem = current.Name;
            grid.Rows.Clear(); grid.Columns.Clear();
            if (page == Page.Products)
            {
                foreach (var title in new[] { "Kod", "Nazwa", "Stan", "Cena" }) grid.Columns.Add(title, title);
                foreach (var product in session.Products) grid.Rows.Add(product.Code, product.Name, product.Stock, product.Price);
            }
            else
            {
                foreach (var title in new[] { "Kod", "Nazwa produktu", "Ilość" }) grid.Columns.Add(title, title);
                if (current != null) foreach (var row in current.Rows) grid.Rows.Add(row.Code, session.FindProduct(row.Code)?.Name ?? "", row.Quantity);
            }
            code.Enabled = powered && (page is Page.Scan or Page.Test);
            quantity.Enabled = powered && page == Page.Scan && current?.Mode != ScanMode.Quick;
            detail.Text = page == Page.Products ? $"Produkty / {session.Products.Count}" : current == null ? "Kolektor Z-9000 / EKP" : $"{current.Name} / {ModeLabel(current.Mode)} / poz: {current.Rows.Count}";
            help.Text = page switch
            {
                Page.Scan => current?.Mode == ScanMode.Quick ? "Szybki skan: wpisz kod i naciśnij SCAN lub Enter. Ilość zawsze 1; powtórzenia pozostają osobnymi pozycjami." : "Wpisz kod → SCAN / Enter → ilość → Enter. Skanuj zachowuje osobne pozycje; Skanuj+ sumuje kody i pozwala korygować ilości wartością ujemną.",
                Page.Sending => "Dokument gotowy do transmisji. Uruchom odbiór odczytów w Subiekcie przez SUZ. Usuwanie nastąpi wyłącznie po rzeczywistym potwierdzeniu odbioru. ESC anuluje i zachowuje dane.",
                Page.Load => "Kolektor gotowy do odbioru katalogu. Uruchom wysyłanie produktów w Subiekcie przez SUZ. Pełny katalog zostanie zapisany po weryfikacji; wtedy ilości sumaryczne zostaną wyzerowane. ESC anuluje.",
                _ => "Klawisze kolektora: 1–9 wybierają opcję, ▲/▼ zmieniają wybór, ENTER/SCAN zatwierdzają, ESC wraca. FN+1…5: skróty; na komputerze F1…F5. α włącza litery i znaki klawiatury kolektora."
            };
            if (lastError.Length > 0) message.Text = lastError;
            RefreshDisplay();
        }
        finally { refreshing = false; }
    }
    private static string ModeLabel(ScanMode mode) => mode switch { ScanMode.Separate => "Skanuj", ScanMode.Summed => "Skanuj+", _ => "Szybki skan" };
    private void RefreshDisplay()
    {
        device.Modifiers(fn, alpha);
        if (!powered) { device.DisplayText = "\n\n     WYŁĄCZONY\n\n ON/OFF — włącz\n\n Dane zachowane"; return; }
        if (flashText.Length > 0) { device.DisplayText = flashText; return; }
        var heading = page switch { Page.Home => "ELZAB KOLEKTOR PLUS", Page.ScanMenu => "1. SKANUJ", Page.SumStart => "SKANUJ+ / PRODUKTY", Page.Scan => current?.Name ?? "SKANUJ", Page.Other => "3. INNE", Page.Settings => "USTAWIENIA", Page.DeleteSetting => "PO WYSŁANIU / USUWANIE", Page.FeedbackSetting => "CO POKAZAĆ", Page.BrowseMenu => "PRZEGLĄD", Page.Documents => "DOKUMENTY", Page.Products => "PRODUKTY", Page.SendMenu => "2. WYŚLIJ", Page.Sending => "OCZEKIWANIE NA PC", Page.Load => "ŁADUJ PRODUKTY", Page.Test => "TEST SKANERA", _ => "USUWANIE" };
        var text = new StringBuilder(heading + "\n" + (fn ? "[FN] " : "") + (alpha ? "[α] " : "") + "EKP / symulacja\n\n");
        if (page == Page.Scan && current != null)
        {
            text.Append("Kod: ").Append(lastCode.Length > 0 ? lastCode : "—").Append('\n');
            var product = session.FindProduct(lastCode);
            if (product != null) text.Append(product.Name[..Math.Min(product.Name.Length, 25)]).Append('\n').Append($"Stan:{product.Stock:0.###} Cena:{product.Price:0.##}\n");
            text.Append("Ilość: ").Append(quantity.Text).Append('\n');
            if (current.Mode == ScanMode.Summed) text.Append("W sumie: ").Append(current.Rows.FirstOrDefault(r => r.Code == lastCode)?.Quantity ?? 0).Append('\n');
            text.Append("poz: ").Append(current.Rows.Count).Append("\nSCAN → ilość → ENTER");
        }
        else if (page == Page.Sending) text.Append(current?.Name).Append("\nTransmisja: 115200\n\n").Append(pendingSend == null ? "ENTER — gotowość" : "Odbierz dane na PC").Append("\n").Append(communicationStatus[..Math.Min(communicationStatus.Length, 25)]).Append("\nESC — anuluj");
        else if (page == Page.Load) text.Append("Odbiór produktów z PC\nTransmisja: 115200\n\n").Append(pendingReceive == null ? "ENTER — gotowość" : "Wyślij produkty na PC").Append("\n").Append(communicationStatus[..Math.Min(communicationStatus.Length, 25)]).Append("\nESC — anuluj");
        else if (page == Page.Test) text.Append("Wpisz kod po prawej\ni naciśnij SCAN.\n\n").Append(lastCode);
        else if (page == Page.Products) text.Append("Liczba: ").Append(session.Products.Count).Append("\nLista po prawej\n\nESC — powrót");
        else
        {
            var options = Options(); selected = Math.Min(selected, Math.Max(0, options.Count - 1));
            foreach (var (option, index) in options.Select((o, i) => (o, i)).Skip(Math.Max(0, selected - 5)).Take(7)) text.Append(index == selected ? ">" : " ").Append(option.Digit).Append('.').Append(option.Label).Append('\n');
            if (options.Count == 0) text.Append("Brak dokumentów\nESC — powrót");
        }
        device.DisplayText = text.ToString();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { CancelCommunication(); feedbackTimer.Dispose(); }
        base.Dispose(disposing);
    }
}

internal sealed class DocumentDialog : Form
{
    private readonly TextBox name = new() { Width = 320 };
    private readonly CheckBox summed = new() { Text = "Sklejać pozycje (sumować)?", AutoSize = true };
    private readonly CheckBox quick = new() { Text = "Szybkie skanowanie (ilość 1)?", AutoSize = true };
    public string DocumentName => name.Text;
    public ScanMode Mode => summed.Checked ? ScanMode.Summed : quick.Checked ? ScanMode.Quick : ScanMode.Separate;
    public DocumentDialog(string? fixedName)
    {
        Text = "Nowy dokument"; Size = new Size(430, 240); StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 10); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        name.Text = fixedName ?? ""; name.ReadOnly = fixedName != null;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(18), WrapContents = false };
        layout.Controls.Add(new Label { Text = "Nazwa dokumentu", AutoSize = true }); layout.Controls.Add(name); layout.Controls.Add(summed); layout.Controls.Add(quick);
        var button = new Button { Text = "ENTER / Utwórz", AutoSize = true, DialogResult = DialogResult.OK }; layout.Controls.Add(button); Controls.Add(layout); AcceptButton = button;
        var cancel = new Button { Text = "ESC / Anuluj", AutoSize = true, DialogResult = DialogResult.Cancel }; layout.Controls.Add(cancel); CancelButton = cancel;
        summed.CheckedChanged += (_, _) => { quick.Enabled = !summed.Checked; if (summed.Checked) quick.Checked = false; };
    }
}
