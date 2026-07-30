using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArchiveMediaDrive.Core;
using Xunit;

namespace ArchiveMediaDrive.Core.Tests;

public sealed class RcloneRuntimeManagerTests
{
    private sealed class FakeAssetDownloader : IAssetDownloader
    {
        private readonly Func<string, Stream> _open;
        public FakeAssetDownloader(Func<string, Stream> open) => _open = open;
        public Task<Stream> OpenAsync(string filename, CancellationToken cancellationToken) => Task.FromResult(_open(filename));
    }

    private static byte[] MakeZip(params (string path, byte[] content, bool executable)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: false))
        {
            foreach (var (path, content, _) in entries)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.Fastest);
                using var es = entry.Open();
                es.Write(content, 0, content.Length);
            }
        }
        return ms.ToArray();
    }

    private static RcloneManifest ManifestFor(string rid, byte[] archive, string filename)
    {
        var sha = SHA256.HashData(archive);
        return new RcloneManifest
        {
            Schema = 1,
            Version = "1.74.4",
            ReleaseBaseUrl = "https://downloads.rclone.org/v1.74.4",
            Assets = new Dictionary<string, RcloneAsset>
            {
                [rid] = new() { Filename = filename, Sha256 = Convert.ToHexString(sha).ToLowerInvariant() },
            },
        };
    }

    private static string TopDir(string filename) => Path.GetFileNameWithoutExtension(filename);

    private static byte[] MakeZipWithManyEntries(int count, byte[] rcloneBytes)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: false))
        {
            for (var i = 0; i < count - 1; i++)
            {
                var entry = zip.CreateEntry($"file{i}.txt", CompressionLevel.Fastest);
                using var es = entry.Open();
                es.Write(Encoding.UTF8.GetBytes("x"), 0, 1);
            }

            var rclone = zip.CreateEntry("rclone", CompressionLevel.Fastest);
            using var rs = rclone.Open();
            rs.Write(rcloneBytes, 0, rcloneBytes.Length);
        }

        return ms.ToArray();
    }

    private static byte[] MakeZipBomb(string entryName, long uncompressedSize, byte[] compressedData)
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, Encoding.UTF8, true))
        {
            var name = Encoding.UTF8.GetBytes(entryName);
            var localOffset = ms.Position;

            writer.Write((uint)0x04034b50);
            writer.Write((ushort)20);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write((uint)compressedData.Length);
            writer.Write((uint)uncompressedSize);
            writer.Write((ushort)name.Length);
            writer.Write((ushort)0);
            writer.Write(name);
            writer.Write(compressedData);

            var cdOffset = ms.Position;
            writer.Write((uint)0x02014b50);
            writer.Write((ushort)20);
            writer.Write((ushort)20);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write((uint)compressedData.Length);
            writer.Write((uint)uncompressedSize);
            writer.Write((ushort)name.Length);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write(0u);
            writer.Write((int)localOffset);
            writer.Write(name);

            var eocdOffset = ms.Position;
            writer.Write((uint)0x06054b50);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)1);
            writer.Write((int)(eocdOffset - cdOffset));
            writer.Write((int)cdOffset);
            writer.Write((ushort)0);
        }

        ms.Position = 0;
        return ms.ToArray();
    }

    [Fact]
    public async Task Ensure_installed_extracts_rclone_executable_and_writes_receipt()
    {
        var rid = "linux-x64";
        var filename = "rclone-v1.74.4-linux-amd64.zip";
        var top = TopDir(filename);
        var rcloneBytes = Encoding.UTF8.GetBytes("#!/bin/sh\necho rclone\n");
        var archive = MakeZip(
            ($"{top}/rclone", rcloneBytes, true),
            ($"{top}/README.txt", Encoding.UTF8.GetBytes("docs"), false));

        var tmp = Path.Combine(Path.GetTempPath(), "amd-rt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var downloader = new FakeAssetDownloader(_ => new MemoryStream(archive));
            var manager = new RcloneRuntimeManager(tmp, ManifestFor(rid, archive, filename), downloader, rid);

            var exe = await manager.EnsureInstalledAsync(CancellationToken.None);

            Assert.True(File.Exists(exe));
            Assert.Equal(rcloneBytes, File.ReadAllBytes(exe));
            var receipt = Path.Combine(tmp, "rclone-1.74.4", "receipt.json");
            Assert.True(File.Exists(receipt));
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public async Task Checksum_mismatch_leaves_prior_runtime_intact()
    {
        var rid = "linux-x64";
        var filename = "rclone-v1.74.4-linux-amd64.zip";
        var top = TopDir(filename);
        var goodBytes = Encoding.UTF8.GetBytes("good");
        var goodArchive = MakeZip(($"{top}/rclone", goodBytes, true));
        var badArchive = MakeZip(($"{top}/rclone", Encoding.UTF8.GetBytes("bad"), true));

        var tmp = Path.Combine(Path.GetTempPath(), "amd-rt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var callCount = 0;
            var downloader = new FakeAssetDownloader(_ =>
            {
                callCount++;
                return new MemoryStream(callCount == 1 ? goodArchive : badArchive);
            });
            var goodManifest = ManifestFor(rid, goodArchive, filename);
            var manager = new RcloneRuntimeManager(tmp, goodManifest, downloader, rid);

            var exe = await manager.EnsureInstalledAsync(CancellationToken.None);
            var firstContent = File.ReadAllBytes(exe);

            await Assert.ThrowsAsync<RcloneRuntimeException>(() => manager.RepairAsync(CancellationToken.None));

            Assert.Equal(firstContent, File.ReadAllBytes(exe));
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public async Task Traversal_entry_in_archive_is_rejected()
    {
        var rid = "linux-x64";
        var filename = "rclone-v1.74.4-linux-amd64.zip";
        var top = TopDir(filename);
        var archive = MakeZip(
            ($"{top}/rclone", Encoding.UTF8.GetBytes("ok"), true),
            ("../evil", Encoding.UTF8.GetBytes("escape"), false));

        var tmp = Path.Combine(Path.GetTempPath(), "amd-rt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var downloader = new FakeAssetDownloader(_ => new MemoryStream(archive));
            var manager = new RcloneRuntimeManager(tmp, ManifestFor(rid, archive, filename), downloader, rid);

            await Assert.ThrowsAsync<RcloneRuntimeException>(() => manager.EnsureInstalledAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public async Task Unsupported_architecture_error_names_missing_package()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "amd-rt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var manifest = new RcloneManifest
            {
                Schema = 1,
                Version = "1.74.4",
                Assets = new Dictionary<string, RcloneAsset>(),
            };
            var manager = new RcloneRuntimeManager(tmp, manifest, new FakeAssetDownloader(_ => Stream.Null), "linux-mips");

            var ex = await Assert.ThrowsAsync<RcloneRuntimeException>(() => manager.EnsureInstalledAsync(CancellationToken.None));
            Assert.Contains("linux-mips", ex.Message);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public async Task Ensure_installed_is_idempotent()
    {
        var rid = "linux-x64";
        var filename = "rclone-v1.74.4-linux-amd64.zip";
        var top = TopDir(filename);
        var rcloneBytes = Encoding.UTF8.GetBytes("rclone");
        var archive = MakeZip(($"{top}/rclone", rcloneBytes, true));

        var tmp = Path.Combine(Path.GetTempPath(), "amd-rt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var downloader = new FakeAssetDownloader(_ => new MemoryStream(archive));
            var manager = new RcloneRuntimeManager(tmp, ManifestFor(rid, archive, filename), downloader, rid);

            var first = await manager.EnsureInstalledAsync(CancellationToken.None);
            var second = await manager.EnsureInstalledAsync(CancellationToken.None);

            Assert.Equal(first, second);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    private sealed class AsyncFakeAssetDownloader : IAssetDownloader
    {
        private int _callCount;
        public Func<string, CancellationToken, Task<Stream>> Open { get; set; } = (_, _) => Task.FromResult(Stream.Null);
        public int CallCount => _callCount;

        public async Task<Stream> OpenAsync(string filename, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return await Open(filename, cancellationToken);
        }
    }

    [Fact]
    public async Task Ensure_installed_repairs_when_existing_executable_is_corrupted()
    {
        var rid = "linux-x64";
        var filename = "rclone-v1.74.4-linux-amd64.zip";
        var top = TopDir(filename);
        var goodBytes = Encoding.UTF8.GetBytes("rclone-good");
        var archive = MakeZip(($"{top}/rclone", goodBytes, true));

        var tmp = Path.Combine(Path.GetTempPath(), "amd-rt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var downloader = new FakeAssetDownloader(_ => new MemoryStream(archive));
            var manager = new RcloneRuntimeManager(tmp, ManifestFor(rid, archive, filename), downloader, rid);

            var exe = await manager.EnsureInstalledAsync(CancellationToken.None);
            Assert.Equal(goodBytes, File.ReadAllBytes(exe));

            File.WriteAllBytes(exe, Encoding.UTF8.GetBytes("corrupted"));

            var repaired = await manager.EnsureInstalledAsync(CancellationToken.None);

            Assert.Equal(exe, repaired);
            Assert.Equal(goodBytes, File.ReadAllBytes(repaired));
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public async Task Concurrent_install_runs_once_and_returns_same_path()
    {
        var rid = "linux-x64";
        var filename = "rclone-v1.74.4-linux-amd64.zip";
        var top = TopDir(filename);
        var rcloneBytes = Encoding.UTF8.GetBytes("rclone");
        var archive = MakeZip(($"{top}/rclone", rcloneBytes, true));

        var tmp = Path.Combine(Path.GetTempPath(), "amd-rt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var downloader = new AsyncFakeAssetDownloader
            {
                Open = async (_, ct) =>
                {
                    await Task.Delay(50, ct);
                    return new MemoryStream(archive);
                },
            };
            var manager = new RcloneRuntimeManager(tmp, ManifestFor(rid, archive, filename), downloader, rid);

            var t1 = Task.Run(() => manager.EnsureInstalledAsync(CancellationToken.None));
            var t2 = Task.Run(() => manager.EnsureInstalledAsync(CancellationToken.None));

            var results = await Task.WhenAll(t1, t2);

            Assert.Equal(results[0], results[1]);
            Assert.Equal(1, downloader.CallCount);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public async Task Corrupt_receipt_is_rejected_and_repaired()
    {
        var rid = "linux-x64";
        var filename = "rclone-v1.74.4-linux-amd64.zip";
        var top = TopDir(filename);
        var rcloneBytes = Encoding.UTF8.GetBytes("rclone");
        var archive = MakeZip(($"{top}/rclone", rcloneBytes, true));

        var tmp = Path.Combine(Path.GetTempPath(), "amd-rt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var callCount = 0;
            var downloader = new FakeAssetDownloader(_ =>
            {
                callCount++;
                return new MemoryStream(archive);
            });
            var manager = new RcloneRuntimeManager(tmp, ManifestFor(rid, archive, filename), downloader, rid);

            var exe = await manager.EnsureInstalledAsync(CancellationToken.None);
            Assert.Equal(1, callCount);

            File.WriteAllText(Path.Combine(Path.GetDirectoryName(exe)!, "receipt.json"), "not json");

            await Assert.ThrowsAsync<RcloneRuntimeException>(() => manager.VerifyAsync(CancellationToken.None));

            var repaired = await manager.EnsureInstalledAsync(CancellationToken.None);

            Assert.Equal(exe, repaired);
            Assert.Equal(rcloneBytes, File.ReadAllBytes(repaired));
            Assert.Equal(2, callCount);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public async Task Corrupt_executable_is_detected_by_VerifyAsync()
    {
        var rid = "linux-x64";
        var filename = "rclone-v1.74.4-linux-amd64.zip";
        var top = TopDir(filename);
        var rcloneBytes = Encoding.UTF8.GetBytes("rclone");
        var archive = MakeZip(($"{top}/rclone", rcloneBytes, true));

        var tmp = Path.Combine(Path.GetTempPath(), "amd-rt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var downloader = new FakeAssetDownloader(_ => new MemoryStream(archive));
            var manager = new RcloneRuntimeManager(tmp, ManifestFor(rid, archive, filename), downloader, rid);

            var exe = await manager.EnsureInstalledAsync(CancellationToken.None);
            File.WriteAllBytes(exe, Encoding.UTF8.GetBytes("corrupted"));

            await Assert.ThrowsAsync<RcloneRuntimeException>(() => manager.VerifyAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public async Task VerifyAsync_detects_archive_checksum_mismatch_in_receipt()
    {
        var rid = "linux-x64";
        var filename = "rclone-v1.74.4-linux-amd64.zip";
        var top = TopDir(filename);
        var rcloneBytes = Encoding.UTF8.GetBytes("rclone");
        var archive = MakeZip(($"{top}/rclone", rcloneBytes, true));

        var tmp = Path.Combine(Path.GetTempPath(), "amd-rt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var downloader = new FakeAssetDownloader(_ => new MemoryStream(archive));
            var manager = new RcloneRuntimeManager(tmp, ManifestFor(rid, archive, filename), downloader, rid);

            var exe = await manager.EnsureInstalledAsync(CancellationToken.None);
            var receiptPath = Path.Combine(Path.GetDirectoryName(exe)!, "receipt.json");
            var receiptJson = File.ReadAllText(receiptPath);
            var receipt = JsonSerializer.Deserialize<RcloneReceipt>(receiptJson, ArchiveMediaDriveJson.Options)!;
            receipt.ArchiveSha256 = new string('a', 64);
            File.WriteAllText(receiptPath, JsonSerializer.Serialize(receipt, ArchiveMediaDriveJson.Options));

            await Assert.ThrowsAsync<RcloneRuntimeException>(() => manager.VerifyAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public async Task VerifyAsync_detects_executable_checksum_mismatch_in_receipt()
    {
        var rid = "linux-x64";
        var filename = "rclone-v1.74.4-linux-amd64.zip";
        var top = TopDir(filename);
        var rcloneBytes = Encoding.UTF8.GetBytes("rclone");
        var archive = MakeZip(($"{top}/rclone", rcloneBytes, true));

        var tmp = Path.Combine(Path.GetTempPath(), "amd-rt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var downloader = new FakeAssetDownloader(_ => new MemoryStream(archive));
            var manager = new RcloneRuntimeManager(tmp, ManifestFor(rid, archive, filename), downloader, rid);

            var exe = await manager.EnsureInstalledAsync(CancellationToken.None);
            var receiptPath = Path.Combine(Path.GetDirectoryName(exe)!, "receipt.json");
            var receiptJson = File.ReadAllText(receiptPath);
            var receipt = JsonSerializer.Deserialize<RcloneReceipt>(receiptJson, ArchiveMediaDriveJson.Options)!;
            receipt.ExecutableSha256 = new string('a', 64);
            File.WriteAllText(receiptPath, JsonSerializer.Serialize(receipt, ArchiveMediaDriveJson.Options));

            await Assert.ThrowsAsync<RcloneRuntimeException>(() => manager.VerifyAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public async Task Zip_bomb_with_huge_declared_size_is_rejected()
    {
        var rid = "linux-x64";
        var filename = "rclone-v1.74.4-linux-amd64.zip";
        var archive = MakeZipBomb("rclone", 256L * 1024 * 1024 + 1, new byte[] { 0 });

        var tmp = Path.Combine(Path.GetTempPath(), "amd-rt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var downloader = new FakeAssetDownloader(_ => new MemoryStream(archive));
            var manager = new RcloneRuntimeManager(tmp, ManifestFor(rid, archive, filename), downloader, rid);

            var ex = await Assert.ThrowsAsync<RcloneRuntimeException>(() => manager.EnsureInstalledAsync(CancellationToken.None));
            Assert.Contains("exceeds", ex.Message);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }

    [Fact]
    public async Task Zip_archive_with_too_many_entries_is_rejected()
    {
        var rid = "linux-x64";
        var filename = "rclone-v1.74.4-linux-amd64.zip";
        var rcloneBytes = Encoding.UTF8.GetBytes("rclone");
        var archive = MakeZipWithManyEntries(1025, rcloneBytes);

        var tmp = Path.Combine(Path.GetTempPath(), "amd-rt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var downloader = new FakeAssetDownloader(_ => new MemoryStream(archive));
            var manager = new RcloneRuntimeManager(tmp, ManifestFor(rid, archive, filename), downloader, rid);

            var ex = await Assert.ThrowsAsync<RcloneRuntimeException>(() => manager.EnsureInstalledAsync(CancellationToken.None));
            Assert.Contains("1024", ex.Message);
        }
        finally
        {
            Directory.Delete(tmp, true);
        }
    }
}
