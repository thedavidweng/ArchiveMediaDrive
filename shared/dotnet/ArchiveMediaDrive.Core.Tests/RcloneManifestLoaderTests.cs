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
}
