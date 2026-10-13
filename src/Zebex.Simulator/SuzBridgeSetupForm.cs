using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;

namespace Zebex.Simulator;

internal sealed class SuzBridgeSetupForm : Form
{
    private readonly TextBox report = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly Button close = new() { Text = "Zamknij", Dock = DockStyle.Bottom, Height = 44, Enabled = false };
    private bool busy = true;
    public SuzBridgeSetupForm(bool restore)
    {
        Environment.ExitCode = 1;
        Text = restore ? "Przywracanie oryginalnych narzędzi SUZ" : "Instalacja mostu SUZ — Zebex Simulator";
        Size = new Size(740, 460); StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(report); Controls.Add(close); close.Click += (_, _) => Close();
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
        Shown += async (_, _) =>
        {
            using var mutex = new Mutex(false, "Global\\ZebexSimulator.SuzBridgeSetup");
            var owns = false;
            try
            {
                owns = mutex.WaitOne(0);
                if (!owns) throw new IOException("Instalacja mostu jest już uruchomiona.");
                using var identity = WindowsIdentity.GetCurrent();
                if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) throw new IOException("Potrzebne są uprawnienia administratora.");
                var progress = new Progress<string>(text => report.AppendText(text + "\r\n\r\n"));
                await Task.Run(() => SuzBridgeInstaller.Run(restore, progress)); Environment.ExitCode = 0;
                report.AppendText(restore ? "Oryginały przywrócone." : "Most zainstalowany. Połącz port symulatora i sprawdź połączenie w SUZ.");
            }
            catch (Exception e) { report.AppendText("Nie zakończono operacji: " + e.Message); }
            finally { if (owns) mutex.ReleaseMutex(); busy = false; close.Enabled = true; }
        };
    }
}

internal static class SuzBridgeInstaller
{
    private const string DriverHash = "57ffcd738e18546596305a6fb4472511c76b233553514e87eb65317cf4aa95a8";
    private static readonly Dictionary<string, string> Originals = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zac_dataReader.exe"] = "c769d0f39b7537264ad5444ca000e02dd6104f4010123a60ee06cdcad27fb1f2",
        ["zac_dlookup.exe"] = "965978f7e23a2b10167a8ed0167c643dcd2769488a0a6793adbe3085a653c473"
    };
    private const string Manifest = "zebex-bridge-manifest.json";
    private const string Backup = "zebex-original-tools";
    private static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
    private static Dictionary<string, string> ReadManifest(string path)
    {
        var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? throw new IOException("Nieprawidłowy manifest mostu.");
        if (entries.Count == 0 || entries.Keys.Any(n => n != Path.GetFileName(n) || n is "." or "..")) throw new IOException("Nieprawidłowe ścieżki manifestu.");
        return entries;
    }
    public static void Run(bool restore, IProgress<string> progress)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "InsERT", "SUZ", "Drivers");
        if (!Directory.Exists(root)) throw new IOException("Nie znaleziono katalogu sterowników SUZ: " + root);
        var targets = Directory.GetFiles(root, "InsERT.Devices.Drivers.DC.Text.dll", SearchOption.AllDirectories)
            .Where(file => Hash(file) == DriverHash)
            .Select(file => Path.Combine(Path.GetDirectoryName(file)!, "Binaries", "Elzab", "KolektorPlusZebex"))
            .Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (targets.Length == 0) throw new IOException("Nie znaleziono zgodnego sterownika SUZ 19.2. Nic nie zmieniono.");
        var source = Path.Combine(AppContext.BaseDirectory, "suz-bridge");
        var files = restore ? new Dictionary<string, string>() : ReadManifest(Path.Combine(source, Manifest));
        if (!restore)
        {
            foreach (var (name, hash) in files) if (Hash(Path.Combine(source, name)) != hash) throw new IOException("Nieprawidłowy plik paczki: " + name);
            if (!Originals.Keys.All(files.ContainsKey)) throw new IOException("Paczka nie zawiera obu narzędzi mostu.");
        }
        foreach (var target in targets)
        {
            var installedPath = Path.Combine(target, Manifest);
            var installed = File.Exists(installedPath) ? ReadManifest(installedPath) : new Dictionary<string, string>();
            foreach (var (name, hash) in installed)
                if (!File.Exists(Path.Combine(target, name)) || Hash(Path.Combine(target, name)) != hash)
                    throw new IOException("Pliki SUZ zmieniły się po instalacji mostu. Nic nie nadpisano: " + name);
            var backup = Path.Combine(target, Backup);
            foreach (var (name, hash) in Originals)
            {
                var original = Path.Combine(installed.Count > 0 ? backup : target, name);
                if (!File.Exists(original) || Hash(original) != hash) throw new IOException("Niezgodne oryginalne narzędzie: " + original);
            }
            if (restore && installed.Count == 0) throw new IOException("Nie ma zainstalowanego mostu do przywrócenia: " + target);
            if (!restore) foreach (var name in files.Keys)
                if (File.Exists(Path.Combine(target, name)) && !Originals.ContainsKey(name) && !installed.ContainsKey(name))
                    throw new IOException("Istniejący plik nie należy do mostu; nie zostanie nadpisany: " + name);
        }
        using var service = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == "InsERTDevicesService");
        var restart = service?.Status == ServiceControllerStatus.Running;
        var journal = new List<(string Path, byte[]? Previous)>();
        try
        {
            if (restart) { progress.Report("Zatrzymywanie usługi SUZ na czas zmiany narzędzi…"); service!.Stop(); service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20)); }
            foreach (var target in targets)
            {
                progress.Report(target);
                var backup = Path.Combine(target, Backup); Directory.CreateDirectory(backup);
                if (!File.Exists(Path.Combine(target, Manifest)))
                    foreach (var (name, hash) in Originals)
                    {
                        var saved = Path.Combine(backup, name);
                        if (!File.Exists(saved)) File.Copy(Path.Combine(target, name), saved);
                        if (Hash(saved) != hash) throw new IOException("Nieprawidłowa kopia oryginału: " + name);
                    }
                void Replace(string path, byte[]? bytes)
                {
                    journal.Add((path, File.Exists(path) ? File.ReadAllBytes(path) : null));
                    if (bytes == null) File.Delete(path); else File.WriteAllBytes(path, bytes);
                }
                if (restore)
                {
                    var installed = ReadManifest(Path.Combine(target, Manifest));
                    foreach (var name in installed.Keys)
                        Replace(Path.Combine(target, name), Originals.ContainsKey(name) ? File.ReadAllBytes(Path.Combine(backup, name)) : null);
                    Replace(Path.Combine(target, Manifest), null);
                }
                else
                {
                    foreach (var name in files.Keys) Replace(Path.Combine(target, name), File.ReadAllBytes(Path.Combine(source, name)));
                    Replace(Path.Combine(target, Manifest), File.ReadAllBytes(Path.Combine(source, Manifest)));
                }
            }
        }
        catch
        {
            foreach (var entry in journal.AsEnumerable().Reverse())
            {
                try { if (entry.Previous == null) File.Delete(entry.Path); else File.WriteAllBytes(entry.Path, entry.Previous); }
                catch (Exception rollback) { progress.Report("Błąd przywracania " + entry.Path + ": " + rollback.Message + ". Kopie oryginałów są w " + Backup); }
            }
            throw;
        }
        finally
        {
            if (restart && service != null)
            {
                service.Refresh();
                if (service.Status == ServiceControllerStatus.Stopped) { progress.Report("Uruchamianie usługi SUZ…"); service.Start(); service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20)); }
            }
        }
    }
}
