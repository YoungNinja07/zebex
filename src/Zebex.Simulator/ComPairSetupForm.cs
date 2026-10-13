using System.Security.Principal;

namespace Zebex.Simulator;

internal sealed class ComPairSetupForm : Form
{
    private readonly TextBox report = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly Button close = new() { Text = "Zamknij", Dock = DockStyle.Bottom, Height = 44, Enabled = false };
    private bool busy = true;

    public ComPairSetupForm()
    {
        Environment.ExitCode = 1;
        Text = "Zebex Simulator — tworzenie pary COM";
        Size = new Size(720, 420);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 11);
        Controls.Add(report); Controls.Add(close);
        close.Click += (_, _) => Close();
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
        Shown += async (_, _) =>
        {
            using var mutex = new Mutex(false, "Global\\ZebexSimulator.ComSetup");
            var ownsMutex = false;
            try
            {
                ownsMutex = mutex.WaitOne(0);
                if (!ownsMutex) throw new InvalidOperationException("Konfiguracja portów jest już uruchomiona w innym oknie.");
                using var identity = WindowsIdentity.GetCurrent();
                if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                    throw new InvalidOperationException("Do instalacji portów potrzebne są uprawnienia administratora.");
                var progress = new Progress<string>(message => report.AppendText(message + "\r\n\r\n"));
                await ComPairInstaller.CreateAsync(progress);
                Environment.ExitCode = 0;
            }
            catch (Exception e) { report.AppendText("Nie udało się zakończyć konfiguracji:\r\n" + e.Message); }
            finally { if (ownsMutex) mutex.ReleaseMutex(); busy = false; close.Enabled = true; }
        };
    }
}
