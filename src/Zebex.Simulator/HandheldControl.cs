using System.Drawing.Drawing2D;

namespace Zebex.Simulator;

internal sealed class HandheldControl : UserControl
{
    private readonly Label lcd = new() { BackColor = Color.FromArgb(238, 244, 239), ForeColor = Color.FromArgb(28, 37, 34), Font = new Font("Consolas", 10), Padding = new Padding(8), AutoSize = false };
    private readonly List<(Button Button, int Row, int Column)> keys = [];
    public event Action<string>? DeviceKey;
    public string DisplayText { set => lcd.Text = value; }

    public HandheldControl()
    {
        DoubleBuffered = true; BackColor = Color.FromArgb(238, 240, 243);
        MinimumSize = new Size(300, 580); Size = new Size(335, 625);
        Controls.Add(lcd);
        string[][] rows = [["▲", "SCAN", "▼"], ["1", "2", "3"], ["4", "5", "6"], ["7", "8", "9"], ["BS", "0", "ENTER"], ["FN", "ESC", "α"], ["", "ON/OFF", ""]];
        for (var row = 0; row < rows.Length; row++) for (var col = 0; col < 3; col++)
        {
            var text = rows[row][col]; if (text == "") continue;
            var button = new Button { Text = text, TabStop = false, FlatStyle = FlatStyle.Flat, BackColor = text == "SCAN" ? Color.FromArgb(255, 202, 40) : Color.FromArgb(213, 216, 219), ForeColor = Color.FromArgb(24, 26, 28), Font = new Font("Segoe UI", text is "ENTER" or "ON/OFF" ? 9 : 12, FontStyle.Bold), Cursor = Cursors.Hand };
            button.FlatAppearance.BorderColor = Color.FromArgb(82, 86, 90);
            button.Click += (_, _) => DeviceKey?.Invoke(text);
            keys.Add((button, row, col)); Controls.Add(button);
        }
        Resize += (_, _) => LayoutDevice();
        LayoutDevice();
    }
    public void Modifiers(bool fn, bool alpha)
    {
        foreach (var key in keys.Where(k => k.Button.Text is "FN" or "α"))
            key.Button.BackColor = (key.Button.Text == "FN" ? fn : alpha) ? Color.FromArgb(255, 202, 40) : Color.FromArgb(213, 216, 219);
    }
    private void LayoutDevice()
    {
        var x = (Width - 286) / 2;
        lcd.SetBounds(x + 18, 62, 250, 192);
        var gap = Math.Max(38, Math.Min(46, (Height - 288) / 7));
        foreach (var (button, row, column) in keys) button.SetBounds(x + 12 + column * 90, 277 + row * gap, 82, gap - 6);
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var x = (Width - 286) / 2;
        using var body = Round(new Rectangle(x, 8, 286, Height - 18), 25);
        using var brush = new LinearGradientBrush(new Rectangle(x, 8, 286, Height - 18), Color.FromArgb(71, 75, 80), Color.FromArgb(27, 30, 34), LinearGradientMode.Horizontal);
        e.Graphics.FillPath(brush, body);
        using var edge = new Pen(Color.FromArgb(12, 14, 17), 3); e.Graphics.DrawPath(edge, body);
        using var bezel = Round(new Rectangle(x + 12, 55, 262, 207), 9);
        using var black = new SolidBrush(Color.FromArgb(13, 16, 19)); e.Graphics.FillPath(black, bezel);
        using var title = new Font("Segoe UI", 11, FontStyle.Bold);
        using var white = new SolidBrush(Color.FromArgb(233, 235, 238));
        e.Graphics.DrawString("ZEBEX", title, white, x + 22, 25);
        using var small = new Font("Segoe UI", 8);
        e.Graphics.DrawString("Z-9000", small, white, x + 210, 28);
        using var green = new SolidBrush(Color.FromArgb(102, 200, 123)); e.Graphics.FillEllipse(green, x + 148, 32, 6, 6);
    }
    private static GraphicsPath Round(Rectangle rect, int radius)
    {
        var p = new GraphicsPath(); var d = radius * 2;
        p.AddArc(rect.X, rect.Y, d, d, 180, 90); p.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        p.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); p.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
    }
}
