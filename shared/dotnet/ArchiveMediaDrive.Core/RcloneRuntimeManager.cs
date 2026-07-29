using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
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

    public void Validate()
    {
        if (Schema <= 0)
            throw new RcloneRuntimeException("rclone manifest schema must be a positive integer");
        if (string.IsNullOrWhiteSpace(Version))
            throw new RcloneRuntimeException("rclone manifest version is empty");
        if (string.IsNullOrWhiteSpace(ReleaseBaseUrl))
            throw new RcloneRuntimeException("rclone manifest release base URL is empty");
        if (!Uri.TryCreate(ReleaseBaseUrl, UriKind.Absolute, out var uri) || !uri.Scheme.Equals("https", StringComparison.Ordinal))
            throw new RcloneRuntimeException($"rclone manifest release base URL must be an absolute HTTPS URL: {ReleaseBaseUrl}");
        if (Assets is null || Assets.Count == 0)
            throw new RcloneRuntimeException("rclone manifest declares no assets");
        foreach (var asset in Assets)
        {
            if (string.IsNullOrWhiteSpace(asset.Key))
                throw new RcloneRuntimeException("rclone manifest contains an asset with an empty RID");
            asset.Value.Validate();
        }
    }
}

public sealed class RcloneAsset
{
    [JsonPropertyName("filename")]
    public string Filename { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Filename))
            throw new RcloneRuntimeException("rclone asset filename is empty");
        if (string.IsNullOrWhiteSpace(Sha256) || Sha256.Length != 64)
            throw new RcloneRuntimeException($"rclone asset SHA-256 for {Filename} is not a 64-character hex string");
        for (var i = 0; i < Sha256.Length; i++)
        {
            var c = Sha256[i];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                throw new RcloneRuntimeException($"rclone asset SHA-256 for {Filename} contains non-hex characters");
        }
    }
}

public sealed class RcloneReceipt
{
    public string Version { get; set; } = string.Empty;
    public string Rid { get; set; } = string.Empty;
    public string ArchiveSha256 { get; set; } = string.Empty;
    public string ExecutableSha256 { get; set; } = string.Empty;
    public DateTimeOffset InstalledAt { get; set; }
}

public interface IAssetDownloader
{
    Task<Stream> OpenAsync(string filename, CancellationToken cancellationToken);
}

public sealed class HttpAssetDownloader : IAssetDownloader
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public HttpAssetDownloader(HttpClient httpClient, string baseUrl)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<Stream> OpenAsync(string filename, CancellationToken cancellationToken)
    {
        var uri = $"{_baseUrl}/{filename}";
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new RcloneRuntimeException($"download failed for {filename}: {(int)response.StatusCode} {response.StatusCode}");
        return await response.Content.ReadAsStreamAsync();
    }
}

public sealed class RcloneRuntimeException : Exception
{
    public RcloneRuntimeException(string message) : base(message) { }
}

public sealed class RcloneRuntimeManager : IRcloneRuntimeManager
{
    private const long MaxDownloadBytes = 128L * 1024 * 1024;
    private const long MaxExtractedTotalBytes = 256L * 1024 * 1024;
    private const int MaxExtractedFiles = 1024;
    private const int DownloadBufferSize = 8192;

    private readonly string _dataDirectory;
    private readonly RcloneManifest _manifest;
    private readonly IAssetDownloader _downloader;
    private readonly string _rid;
    private readonly SemaphoreSlim _installLock = new(1, 1);

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
        await _installLock.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(ExecutablePath) && File.Exists(ReceiptPath))
            {
                try
                {
                    await VerifyAsync(cancellationToken);
                    return ExecutablePath;
                }
                catch (RcloneRuntimeException)
                {
                }
            }

            return await InstallCoreAsync(cancellationToken);
        }
        finally
        {
            _installLock.Release();
        }
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
        var tempArchive = Path.Combine(RuntimeDirectory, $".download-{Guid.NewGuid():N}.zip");
        var tempExtract = Path.Combine(RuntimeDirectory, $".extract-{Guid.NewGuid():N}");
        try
        {
            using (var stream = await _downloader.OpenAsync(asset.Filename, cancellationToken))
            using (var file = File.Create(tempArchive))
            {
                await CopyWithLimitAsync(stream, file, MaxDownloadBytes, cancellationToken);
            }

            var actualHash = ComputeSha256(tempArchive);
            if (!string.Equals(actualHash, asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new RcloneRuntimeException($"checksum mismatch for {asset.Filename}: expected {asset.Sha256}, got {actualHash}");

            if (Directory.Exists(tempExtract))
                Directory.Delete(tempExtract, true);
            Directory.CreateDirectory(tempExtract);

            using (var archive = new ZipArchive(File.OpenRead(tempArchive), ZipArchiveMode.Read))
            {
                var rcloneEntry = ValidateArchiveSafety(archive);

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

            var exeHash = ComputeSha256(finalExe);
            var receipt = new RcloneReceipt
            {
                Version = _manifest.Version,
                Rid = _rid,
                ArchiveSha256 = asset.Sha256,
                ExecutableSha256 = exeHash,
                InstalledAt = DateTimeOffset.UtcNow,
            };
            WriteReceipt(receipt, ReceiptPath);

            CleanupOldVersions();

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

        var receipt = JsonSerializer.Deserialize<RcloneReceipt>(File.ReadAllText(ReceiptPath), ArchiveMediaDriveJson.Options);
        if (receipt is null)
            throw new RcloneRuntimeException("rclone receipt is invalid");

        if (!_manifest.Assets.TryGetValue(_rid, out var asset))
            throw new RcloneRuntimeException($"unsupported architecture: no rclone package for RID '{_rid}'");

        if (!string.Equals(receipt.Version, _manifest.Version, StringComparison.Ordinal))
            throw new RcloneRuntimeException($"rclone version mismatch: receipt has {receipt.Version}, manifest has {_manifest.Version}");

        if (!string.Equals(receipt.Rid, _rid, StringComparison.Ordinal))
            throw new RcloneRuntimeException($"rclone RID mismatch: receipt has {receipt.Rid}, expected {_rid}");

        if (!string.Equals(receipt.ArchiveSha256, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new RcloneRuntimeException($"rclone archive checksum mismatch: receipt has {receipt.ArchiveSha256}, expected {asset.Sha256}");

        var actualExeHash = ComputeSha256(ExecutablePath);
        if (!string.Equals(actualExeHash, receipt.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new RcloneRuntimeException($"rclone executable checksum mismatch: receipt has {receipt.ExecutableSha256}, got {actualExeHash}");

        return Task.CompletedTask;
    }

    public Task RemoveAsync(CancellationToken cancellationToken)
    {
        if (Directory.Exists(RuntimeDirectory))
            Directory.Delete(RuntimeDirectory, true);
        return Task.CompletedTask;
    }

    private static async Task CopyWithLimitAsync(Stream source, FileStream destination, long maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[DownloadBufferSize];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            total += read;
            if (total > maxBytes)
                throw new RcloneRuntimeException($"download exceeds maximum size of {maxBytes} bytes");
            await destination.WriteAsync(buffer, 0, read, cancellationToken);
        }
    }

    private static ZipArchiveEntry ValidateArchiveSafety(ZipArchive archive)
    {
        var count = 0;
        long totalSize = 0;
        ZipArchiveEntry? rcloneEntry = null;

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.Length == 0) continue;
            if (entry.FullName.IndexOf("..", StringComparison.Ordinal) >= 0)
                throw new RcloneRuntimeException($"archive contains path traversal entry: {entry.FullName}");
            if (Path.IsPathRooted(entry.FullName))
                throw new RcloneRuntimeException($"archive contains absolute path entry: {entry.FullName}");

            count++;
            totalSize += entry.Length;
            if (count > MaxExtractedFiles)
                throw new RcloneRuntimeException($"archive contains more than {MaxExtractedFiles} entries");
            if (totalSize > MaxExtractedTotalBytes)
                throw new RcloneRuntimeException($"archive extracted size exceeds {MaxExtractedTotalBytes} bytes");

            var name = Path.GetFileName(entry.FullName);
            if (!string.IsNullOrEmpty(name) &&
                (name.Equals("rclone", StringComparison.Ordinal) || name.Equals("rclone.exe", StringComparison.Ordinal)))
            {
                rcloneEntry = entry;
            }
        }

        return rcloneEntry ?? throw new RcloneRuntimeException("archive does not contain a rclone executable");
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

    private static void WriteReceipt(RcloneReceipt receipt, string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var json = JsonSerializer.Serialize(receipt, ArchiveMediaDriveJson.Options);
        var temp = Path.Combine(directory, $"receipt.json.new-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(temp, json);
            if (File.Exists(path))
            {
                var backup = path + ".previous";
                if (File.Exists(backup))
                    File.Delete(backup);
                File.Replace(temp, path, backup);
            }
            else
            {
                File.Move(temp, path);
            }
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private void CleanupOldVersions()
    {
        var current = Path.GetFullPath(RuntimeDirectory);
        var others = Directory.GetDirectories(_dataDirectory, "rclone-*")
            .Select(Path.GetFullPath)
            .Where(d => !d.Equals(current, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => new DirectoryInfo(d).LastWriteTimeUtc)
            .ToList();

        foreach (var dir in others.Skip(1))
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
