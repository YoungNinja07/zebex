using System.Diagnostics;
using System.IO.Compression;
using System.IO.Ports;
using System.Security.Cryptography;
using System.Text.Json;
using Zebex.Core;

namespace Zebex.Simulator;

internal static class ComPairInstaller
{
    // Hash published by the original SourceForge project, not a mirror-specific hash.
    private const string PackageHash = "6e5d4359865277430d4ae88c73fb7e648a0ed8e81aea5002478179cfcb0bb0e1";
    private const string PackageUrl = "https://sourceforge.net/projects/com0com/files/com0com/3.0.0.0/com0com-3.0.0.0-i386-and-x64-signed.zip/download";
    private static readonly string StateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ZebexSimulator");
    internal static string StatePath => Path.Combine(StateDirectory, "com-pair.json");

    public static ComPair? SavedPair()
    {
        try { return JsonSerializer.Deserialize<ComPair>(File.ReadAllText(StatePath)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public static async Task<ComPair> CreateAsync(IProgress<string> progress)
    {
        Directory.CreateDirectory(StateDirectory);
        var setup = FindSetup();
        if (setup == null)
        {
            progress.Report("Pobieranie podpisanego pakietu sterownika com0com…");
            var archive = await DownloadPackageAsync();
            progress.Report("Suma SHA-256 pakietu jest poprawna. Instalowanie sterownika…");
            var unpacked = Path.Combine(StateDirectory, "install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(unpacked);
            try
            {
                ZipFile.ExtractToDirectory(archive, unpacked);
                var candidates = Directory.GetFiles(unpacked, "*.exe", SearchOption.AllDirectories)
                    .Where(p => Path.GetFileName(p).Contains("x64", StringComparison.OrdinalIgnoreCase)
                        && Path.GetFileName(p).Contains("signed", StringComparison.OrdinalIgnoreCase)
                        && !Path.GetFileName(p).Contains("unsigned", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (candidates.Length != 1) throw new InvalidDataException("Nie znaleziono jednoznacznego instalatora x64 w zweryfikowanym pakiecie.");
                var installed = await RunAsync(candidates[0], "/S");
                if (installed.ExitCode is not (0 or 3010))
                    throw new InvalidOperationException($"Instalator sterownika zakończył się kodem {installed.ExitCode}. Windows mógł odmówić instalacji; zabezpieczenia pozostają włączone.");
                if (installed.ExitCode == 3010) throw new InvalidOperationException("Sterownik wymaga ponownego uruchomienia Windows. Uruchom komputer ponownie i ponów konfigurację.");
                setup = FindSetup() ?? throw new InvalidOperationException("Instalator nie udostępnił narzędzia com0com. Sprawdź komunikat instalacji i zgodność sterownika z Windows.");
            }
            finally
            {
                try { Directory.Delete(unpacked, true); }
                catch (IOException) { /* Installer may retain a file until reboot. */ }
                catch (UnauthorizedAccessException) { }
            }
        }

        progress.Report("Sprawdzanie istniejących portów…");
        var before = await ListAsync(setup);
        var saved = SavedPair();
        var pair = saved == null ? null : before.FirstOrDefault(p => p == saved);
        if (pair == null)
        {
            progress.Report("Tworzenie pary. Windows dobierze wolne numery COM…");
            var result = await RunAsync(setup, "--silent", "--wait", "20", "install", "PortName=COM#", "PortName=COM#");
            await File.WriteAllTextAsync(Path.Combine(StateDirectory, "last-setup.log"), result.Output);
            if (result.ExitCode != 0)
                throw new InvalidOperationException($"Nie utworzono portów COM (kod {result.ExitCode}). Windows może blokować ten sterownik. Raport: {Path.Combine(StateDirectory, "last-setup.log")}");
            pair = ComPairs.NewlyCreated(before, await ListAsync(setup));
            // Retain identity even if a port is temporarily busy; retry must not create duplicates.
            await SaveAsync(pair);
        }
        else progress.Report("Znaleziono wcześniej utworzoną parę; nie dodaję duplikatu.");

        progress.Report($"Test przesyłania danych: {pair.SuzPort} ↔ {pair.SimulatorPort}…");
        await Task.Run(() => TestPair(pair));
        await SaveAsync(pair);
        progress.Report($"Gotowe. SUZ: {pair.SuzPort}. Symulator: {pair.SimulatorPort}.");
        return pair;
    }

    private static async Task SaveAsync(ComPair pair)
    {
        var temporary = StatePath + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(pair));
        File.Move(temporary, StatePath, true);
    }

    private static string? FindSetup()
    {
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var path = Path.Combine(Environment.GetFolderPath(folder), "com0com", "setupc.exe");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private static async Task<string> DownloadPackageAsync()
    {
        var cache = Path.Combine(StateDirectory, "com0com-signed.zip");
        if (File.Exists(cache))
        {
            try { ArtifactIntegrity.Verify(await File.ReadAllBytesAsync(cache), PackageHash); return cache; }
            catch (InvalidDataException) { File.Delete(cache); }
        }
        var partial = cache + ".partial";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ZebexSimulator/0.2");
            using var response = await client.GetAsync(PackageUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("Źródło pakietu nie używa HTTPS. Instalacja została zatrzymana.");
            const int limit = 5 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Pakiet sterownika jest zbyt duży; pobieranie przerwane.");
            await using (var source = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var destination = new FileStream(partial, FileMode.Create, FileAccess.Write))
            {
                var buffer = new byte[16384];
                var total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    total = checked(total + read);
                    if (total > limit) throw new InvalidDataException("Pakiet sterownika jest zbyt duży; pobieranie przerwane.");
                    await destination.WriteAsync(buffer.AsMemory(0, read));
                }
            }
            ArtifactIntegrity.Verify(await File.ReadAllBytesAsync(partial), PackageHash);
            File.Move(partial, cache, true);
            return cache;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }

    private sealed record CommandResult(int ExitCode, string Output);
    private static async Task<CommandResult> RunAsync(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Nie udało się uruchomić konfiguratora portów.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new(process.ExitCode, await stdout + "\n" + await stderr);
    }
    private static async Task<IReadOnlyList<ComPair>> ListAsync(string setup)
    {
        var result = await RunAsync(setup, "--silent", "list");
        if (result.ExitCode != 0) throw new InvalidOperationException("Nie udało się odczytać konfiguracji sterownika com0com.");
        return ComPairs.ParseList(result.Output);
    }

    private static void TestPair(ComPair pair)
    {
        using var suz = new SerialPort(pair.SuzPort, 115200, Parity.None, 8, StopBits.One) { ReadTimeout = 2000, WriteTimeout = 2000 };
        using var simulator = new SerialPort(pair.SimulatorPort, 115200, Parity.None, 8, StopBits.One) { ReadTimeout = 2000, WriteTimeout = 2000 };
        try
        {
            suz.Open(); simulator.Open();
            var request = RandomNumberGenerator.GetBytes(16);
            var response = RandomNumberGenerator.GetBytes(16);
            suz.Write(request, 0, request.Length);
            ReadExpected(simulator, request);
            simulator.Write(response, 0, response.Length);
            ReadExpected(suz, response);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or TimeoutException or InvalidOperationException)
        {
            throw new InvalidOperationException($"Para została skonfigurowana, ale test transmisji nie przeszedł. Zamknij programy używające {pair.SuzPort}/{pair.SimulatorPort} i spróbuj ponownie. Jeśli Windows blokuje sterownik, nie wyłączaj zabezpieczeń. Szczegóły: {e.Message}", e);
        }
    }
    private static void ReadExpected(SerialPort port, byte[] expected)
    {
        var received = new byte[expected.Length];
        var offset = 0;
        while (offset < received.Length) offset += port.Read(received, offset, received.Length - offset);
        if (!received.AsSpan().SequenceEqual(expected)) throw new InvalidOperationException("Porty nie przesłały poprawnych danych testowych.");
    }
}
