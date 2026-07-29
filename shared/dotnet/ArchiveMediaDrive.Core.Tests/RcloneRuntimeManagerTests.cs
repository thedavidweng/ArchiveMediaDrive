using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
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
}
