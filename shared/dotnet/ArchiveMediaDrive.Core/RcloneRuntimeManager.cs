using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArchiveMediaDrive.Core;

public sealed class RcloneManifest
{
    [JsonPropertyName("schema")]
    public int Schema { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("releaseBaseUrl")]
    public string ReleaseBaseUrl { get; set; } = string.Empty;

    [JsonPropertyName("assets")]
    public Dictionary<string, RcloneAsset> Assets { get; set; } = new();
}

public sealed class RcloneAsset
{
    [JsonPropertyName("filename")]
    public string Filename { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class RcloneReceipt
{
    public string Version { get; set; } = string.Empty;
    public string Rid { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public DateTimeOffset InstalledAt { get; set; }
}

public interface IAssetDownloader
{
    Task<Stream> OpenAsync(string filename, CancellationToken cancellationToken);
}

public sealed class RcloneRuntimeException : Exception
{
    public RcloneRuntimeException(string message) : base(message) { }
}

public sealed class RcloneRuntimeManager : IRcloneRuntimeManager
{
    private readonly string _dataDirectory;
    private readonly RcloneManifest _manifest;
    private readonly IAssetDownloader _downloader;
    private readonly string _rid;

    public RcloneRuntimeManager(string dataDirectory, RcloneManifest manifest, IAssetDownloader downloader, string rid)
    {
        _dataDirectory = dataDirectory;
        _manifest = manifest;
        _downloader = downloader;
        _rid = rid;
    }

    public string RuntimeDirectory => Path.Combine(_dataDirectory, $"rclone-{_manifest.Version}");
    public string ExecutablePath => Path.Combine(RuntimeDirectory, RcloneExecutableName);
    public string ReceiptPath => Path.Combine(RuntimeDirectory, "receipt.json");

    private static string RcloneExecutableName => IsWindows ? "rclone.exe" : "rclone";

    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public async Task<string> EnsureInstalledAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(ExecutablePath) && File.Exists(ReceiptPath))
            return ExecutablePath;

        return await InstallCoreAsync(cancellationToken);
    }

    public async Task RepairAsync(CancellationToken cancellationToken)
    {
        await InstallCoreAsync(cancellationToken);
    }

    private async Task<string> InstallCoreAsync(CancellationToken cancellationToken)
    {

        if (!_manifest.Assets.TryGetValue(_rid, out var asset))
            throw new RcloneRuntimeException($"unsupported architecture: no rclone package for RID '{_rid}'");

        Directory.CreateDirectory(RuntimeDirectory);
        var tempArchive = Path.Combine(RuntimeDirectory, ".download.zip");
        var tempExtract = Path.Combine(RuntimeDirectory, ".extract");
        try
        {
            using (var stream = await _downloader.OpenAsync(asset.Filename, cancellationToken))
            using (var file = File.Create(tempArchive))
            {
                await stream.CopyToAsync(file);
            }

            var actualHash = ComputeSha256(tempArchive);
            if (!string.Equals(actualHash, asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new RcloneRuntimeException($"checksum mismatch for {asset.Filename}: expected {asset.Sha256}, got {actualHash}");

            if (Directory.Exists(tempExtract))
                Directory.Delete(tempExtract, true);
            Directory.CreateDirectory(tempExtract);

            using (var archive = new ZipArchive(File.OpenRead(tempArchive), ZipArchiveMode.Read))
            {
                ValidateArchiveSafety(archive);
                var rcloneEntry = FindRcloneEntry(archive)
                    ?? throw new RcloneRuntimeException($"archive {asset.Filename} does not contain a rclone executable");

                var fullTarget = Path.GetFullPath(Path.Combine(tempExtract, "rclone"));
                var entryFull = Path.GetFullPath(Path.Combine(tempExtract, rcloneEntry.FullName));
                if (!entryFull.StartsWith(fullTarget, StringComparison.Ordinal))
                    throw new RcloneRuntimeException($"rclone entry escapes extraction directory: {rcloneEntry.FullName}");

                ExtractEntryTo(rcloneEntry, fullTarget);
            }

            var finalExe = ExecutablePath;
            if (File.Exists(finalExe))
                File.Delete(finalExe);
            File.Move(sourceFileName: Path.Combine(tempExtract, "rclone"), destFileName: finalExe);
            SetExecutablePermissions(finalExe);

            var receipt = new RcloneReceipt
            {
                Version = _manifest.Version,
                Rid = _rid,
                Sha256 = asset.Sha256,
                InstalledAt = DateTimeOffset.UtcNow,
            };
            File.WriteAllText(ReceiptPath, JsonSerializer.Serialize(receipt, ArchiveMediaDriveJson.Options));

            return finalExe;
        }
        catch (RcloneRuntimeException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new RcloneRuntimeException($"rclone bootstrap failed: {ex.Message}");
        }
        finally
        {
            try { if (File.Exists(tempArchive)) File.Delete(tempArchive); } catch { }
            try { if (Directory.Exists(tempExtract)) Directory.Delete(tempExtract, true); } catch { }
        }
    }

    public Task VerifyAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(ExecutablePath) || !File.Exists(ReceiptPath))
            throw new RcloneRuntimeException("rclone runtime is not installed");
        return Task.CompletedTask;
    }

    public Task RemoveAsync(CancellationToken cancellationToken)
    {
        if (Directory.Exists(RuntimeDirectory))
            Directory.Delete(RuntimeDirectory, true);
        return Task.CompletedTask;
    }

    private static ZipArchiveEntry? FindRcloneEntry(ZipArchive archive)
    {
        foreach (var entry in archive.Entries)
        {
            var name = Path.GetFileName(entry.FullName);
            if (string.IsNullOrEmpty(name)) continue;
            if (name.Equals("rclone", StringComparison.Ordinal) || name.Equals("rclone.exe", StringComparison.Ordinal))
                return entry;
        }
        return null;
    }

    private static void ValidateArchiveSafety(ZipArchive archive)
    {
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.Length == 0) continue;
            if (entry.FullName.Contains("..", StringComparison.Ordinal))
                throw new RcloneRuntimeException($"archive contains path traversal entry: {entry.FullName}");
            if (Path.IsPathRooted(entry.FullName))
                throw new RcloneRuntimeException($"archive contains absolute path entry: {entry.FullName}");
        }
    }

    private static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        var hash = sha.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static void ExtractEntryTo(ZipArchiveEntry entry, string destinationPath)
    {
        using var es = entry.Open();
        using var fs = File.Create(destinationPath);
        es.CopyTo(fs);
    }

    private static void SetExecutablePermissions(string path)
    {
        if (IsWindows) return;
        try
        {
            var psi = new ProcessStartInfo("chmod", $"u+rx,go+r \"{path}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit();
        }
        catch (Win32Exception)
        {
        }
    }
}
