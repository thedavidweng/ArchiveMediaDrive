using ArchiveMediaDrive.Core;
using Xunit;

namespace ArchiveMediaDrive.Core.Tests;

public sealed class RcloneManifestLoaderTests
{
    private static readonly string ManifestPath =
        Path.Combine(AppContext.BaseDirectory, "runtime", "rclone", "manifest.json");

    [Fact]
    public void Loads_committed_manifest_with_pinned_version_and_assets()
    {
        var manifest = RcloneManifestLoader.Load(ManifestPath);

        Assert.Equal("1.74.4", manifest.Version);
        Assert.True(manifest.Assets.Count >= 5);
        foreach (var asset in manifest.Assets.Values)
        {
            Assert.False(string.IsNullOrWhiteSpace(asset.Filename));
            Assert.False(string.IsNullOrWhiteSpace(asset.Sha256));
            Assert.Matches("^[0-9a-f]{64}$", asset.Sha256);
        }
    }

    [Fact]
    public void Load_throws_for_invalid_json()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "not json");
            Assert.Throws<RcloneRuntimeException>(() => RcloneManifestLoader.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_throws_for_corrupt_manifest()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{\"schema\":1}");
            Assert.Throws<RcloneRuntimeException>(() => RcloneManifestLoader.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Manifest_rejects_unsupported_schema_version()
    {
        var manifest = new RcloneManifest
        {
            Schema = 2,
            Version = "1.74.4",
            ReleaseBaseUrl = "https://downloads.rclone.org/v1.74.4",
            Assets = new Dictionary<string, RcloneAsset>
            {
                ["linux-x64"] = new() { Filename = "rclone.zip", Sha256 = new string('0', 64) },
            },
        };

        Assert.Throws<RcloneRuntimeException>(() => manifest.Validate());
    }
}
