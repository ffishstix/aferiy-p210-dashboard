using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BatteryWatch;

internal sealed record UpdateRelease(Version Version, string Architecture, string Sha256);

internal static class UpdateService
{
    private const int MaxManifestBytes = 16 * 1024;
    private const long MaxExecutableBytes = 25 * 1024 * 1024;
    private static readonly string[] Architectures = ["win-x64", "win-arm64", "win-x86"];
    private static readonly string UpdateFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AferiyMonitor", "updates");
    private static readonly Uri Site = new(Program.DashboardUrl);
    public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    private static string Canonical(string version, IReadOnlyDictionary<string, string> hashes) =>
        version + "\n" + string.Join("", Architectures.Select(arch => $"{arch}:{hashes[arch]}\n"));

    public static UpdateRelease VerifyManifest(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        string versionText = root.GetProperty("version").GetString() ?? "";
        if (!Regex.IsMatch(versionText, @"^\d+\.\d+\.\d+$") || !Version.TryParse(versionText, out var version))
            throw new InvalidDataException("Invalid update version");
        var files = root.GetProperty("sha256");
        var hashes = new Dictionary<string, string>();
        foreach (var arch in Architectures)
        {
            string hash = files.GetProperty(arch).GetString()?.ToUpperInvariant() ?? "";
            if (!Regex.IsMatch(hash, "^[0-9A-F]{64}$")) throw new InvalidDataException("Invalid update checksum");
            hashes[arch] = hash;
        }
        var signature = Convert.FromBase64String(root.GetProperty("signature").GetString() ?? "");
        using var keyStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BatteryWatch.update-public.pem")
            ?? throw new InvalidDataException("Update verification key is missing");
        using var reader = new StreamReader(keyStream);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(reader.ReadToEnd());
        if (!rsa.VerifyData(Encoding.UTF8.GetBytes(Canonical(versionText, hashes)), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            throw new InvalidDataException("Update signature is invalid");
        string archName = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.Arm64 => "win-arm64",
            Architecture.X86 => "win-x86",
            _ => throw new PlatformNotSupportedException("Unsupported Windows architecture")
        };
        return new UpdateRelease(version, archName, hashes[archName]);
    }

    public static async Task<string?> CheckAndStageAsync(Version? installedVersion = null, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        using var manifestRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(Site, "/updates/latest.json"));
        manifestRequest.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        using var manifestResponse = await client.SendAsync(manifestRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        manifestResponse.EnsureSuccessStatusCode();
        if (manifestResponse.Content.Headers.ContentLength is > MaxManifestBytes) throw new InvalidDataException("Update manifest is too large");
        await using var manifestStream = await manifestResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var manifestBuffer = new MemoryStream();
        var chunk = new byte[4096];
        while (true)
        {
            int read = await manifestStream.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (manifestBuffer.Length + read > MaxManifestBytes) throw new InvalidDataException("Update manifest is too large");
            manifestBuffer.Write(chunk, 0, read);
        }
        var manifestBytes = manifestBuffer.ToArray();
        var release = VerifyManifest(Encoding.UTF8.GetString(manifestBytes));
        if (release.Version <= (installedVersion ?? CurrentVersion)) return null;

        Directory.CreateDirectory(UpdateFolder);
        var ready = Path.Combine(UpdateFolder, $"BatteryWatch-{release.Version}-{release.Architecture}-{release.Sha256}.exe");
        if (File.Exists(ready) && new FileInfo(ready).Length <= MaxExecutableBytes &&
            string.Equals(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(ready, cancellationToken))), release.Sha256, StringComparison.Ordinal))
        {
            try { await RunSelfTestAsync(ready, cancellationToken); }
            catch { File.Delete(ready); throw; }
            return ready;
        }
        var temporary = ready + ".part";
        try
        {
            using var response = await client.GetAsync(new Uri(Site, $"/downloads/{release.Architecture}/BatteryWatch.exe"), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaxExecutableBytes) throw new InvalidDataException("Update download is too large");
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long total = 0;
            while (true)
            {
                int read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                total += read;
                if (total > MaxExecutableBytes) throw new InvalidDataException("Update download is too large");
                digest.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            if (!string.Equals(Convert.ToHexString(digest.GetHashAndReset()), release.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException("Update checksum does not match the signed release");
            await destination.FlushAsync(cancellationToken);
            destination.Close();
            File.Move(temporary, ready, true);
            try { await RunSelfTestAsync(ready, cancellationToken); }
            catch { File.Delete(ready); throw; }
            return ready;
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    private static async Task RunSelfTestAsync(string executable, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--self-test");
        using var process = Process.Start(start) ?? throw new InvalidDataException("The downloaded app could not start");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill();
            throw new InvalidDataException("The downloaded app did not pass its startup check");
        }
        if (process.ExitCode != 0) throw new InvalidDataException("The downloaded app did not pass its startup check");
    }

    public static void ReplaceAfterExit(string staged, bool smokeTest = false)
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable) || !Path.GetFileName(executable).StartsWith("BatteryWatch", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The running app file cannot be identified");
        string fullStaged = Path.GetFullPath(staged);
        string expectedFolder = Path.GetFullPath(UpdateFolder) + Path.DirectorySeparatorChar;
        if (!fullStaged.StartsWith(expectedFolder, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullStaged))
            throw new InvalidOperationException("The verified update file is missing");
        var match = Regex.Match(Path.GetFileName(fullStaged), @"^BatteryWatch-\d+\.\d+\.\d+-win-(?:x64|arm64|x86)-([0-9A-F]{64})\.exe$", RegexOptions.IgnoreCase);
        if (!match.Success) throw new InvalidOperationException("The verified update file name is invalid");
        string expectedHash = match.Groups[1].Value.ToUpperInvariant();
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        var log = Path.Combine(UpdateFolder, "last-update.log");
        string restartArgument = smokeTest ? "-ArgumentList '--self-test' " : "";
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $target = {{Quote(executable)}}
            $staged = {{Quote(fullStaged)}}
            $backup = {{Quote(executable + ".previous")}}
            try {
              Wait-Process -Id {{Environment.ProcessId}} -ErrorAction SilentlyContinue
              $sha = [System.Security.Cryptography.SHA256]::Create()
              try { $actualHash = [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($staged))).Replace('-', '') }
              finally { $sha.Dispose() }
              if ($actualHash -ne {{Quote(expectedHash)}}) { throw 'Update checksum changed after download' }
              Copy-Item -LiteralPath $target -Destination $backup -Force
              Copy-Item -LiteralPath $staged -Destination $target -Force
              try { Start-Process -FilePath $target {{restartArgument}}-WindowStyle Hidden -ErrorAction Stop }
              catch { Copy-Item -LiteralPath $backup -Destination $target -Force; throw }
              Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
              Remove-Item -LiteralPath $staged -Force -ErrorAction SilentlyContinue
              Remove-Item -LiteralPath {{Quote(log)}} -Force -ErrorAction SilentlyContinue
            } catch {
              Add-Content -LiteralPath {{Quote(log)}} -Value $_.Exception.Message -ErrorAction SilentlyContinue
              if (Test-Path -LiteralPath $target) { Start-Process -FilePath $target {{restartArgument}}-WindowStyle Hidden -ErrorAction SilentlyContinue }
            }
            """;
        var helper = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        };
        helper.ArgumentList.Add("-NoProfile"); helper.ArgumentList.Add("-NonInteractive");
        helper.ArgumentList.Add("-WindowStyle"); helper.ArgumentList.Add("Hidden");
        helper.ArgumentList.Add("-EncodedCommand"); helper.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var process = Process.Start(helper) ?? throw new InvalidOperationException("Could not start the update helper");
    }

    public static void DeleteStagedFiles()
    {
        if (!Directory.Exists(UpdateFolder)) return;
        foreach (var file in Directory.GetFiles(UpdateFolder, "BatteryWatch-*.exe")) { try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        foreach (var file in Directory.GetFiles(UpdateFolder, "BatteryWatch-*.exe.part")) { try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
