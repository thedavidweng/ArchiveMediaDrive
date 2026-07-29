using System.Diagnostics;
using ArchiveMediaDrive.Core;
using Xunit;

namespace ArchiveMediaDrive.Core.Tests;

[Trait("Category", "Integration")]
public sealed class RcloneLoopbackGatewayIntegrationTests
{
    private static readonly string RcloneBinary = FindRclone();
    private static readonly string ConfigPath = WriteTempConfig();

    private static string FindRclone()
    {
        var path = Environment.GetEnvironmentVariable("AMD_TEST_RCLONE_BINARY") ?? "rclone";
        try
        {
            var psi = new ProcessStartInfo(path, "version")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
            return p is { ExitCode: 0 } ? path : "";
        }
        catch
        {
            return "";
        }
    }

    private static string WriteTempConfig()
    {
        var path = Path.Combine(Path.GetTempPath(), $"amd-test-rclone-{Guid.NewGuid():N}.conf");
        File.WriteAllText(path, "[archive-media-drive-ia]\ntype = internetarchive\n");
        return path;
    }

    [Fact]
    public async Task Probe_returns_rclone_version_when_binary_available()
    {
        if (string.IsNullOrEmpty(RcloneBinary))
        {
            return;
        }

        var process = new RcloneProcess(RcloneBinary, ConfigPath, "archive-media-drive-ia");
        var gateway = new RcloneLoopbackGateway(process);

        var probe = await gateway.ProbeAsync(CancellationToken.None);

        Assert.False(string.IsNullOrEmpty(probe.Version));
        Assert.False(string.IsNullOrEmpty(probe.Platform));
    }

    [Fact]
    public async Task List_returns_files_for_known_public_item()
    {
        if (string.IsNullOrEmpty(RcloneBinary))
        {
            return;
        }

        var process = new RcloneProcess(RcloneBinary, ConfigPath, "archive-media-drive-ia");
        var gateway = new RcloneLoopbackGateway(process);

        var nodes = await gateway.ListAsync("TripDown1905", "", CancellationToken.None);

        Assert.NotEmpty(nodes);
        var mp4 = nodes.FirstOrDefault(n => n.Name == "TripDown1905.mp4");
        Assert.NotNull(mp4);
        Assert.Equal(RawNodeKind.File, mp4!.Kind);
        Assert.True(mp4.Size > 0);
    }

    [Fact]
    public async Task List_with_subpath_returns_nested_files()
    {
        if (string.IsNullOrEmpty(RcloneBinary))
        {
            return;
        }

        var process = new RcloneProcess(RcloneBinary, ConfigPath, "archive-media-drive-ia");
        var gateway = new RcloneLoopbackGateway(process);

        var nodes = await gateway.ListAsync("TripDown1905", "TripDown1905.thumbs", CancellationToken.None);

        Assert.NotEmpty(nodes);
        Assert.All(nodes, n => Assert.Equal(RawNodeKind.File, n.Kind));
    }

    [Fact]
    public async Task PublicLink_returns_download_url_for_known_file()
    {
        if (string.IsNullOrEmpty(RcloneBinary))
        {
            return;
        }

        var process = new RcloneProcess(RcloneBinary, ConfigPath, "archive-media-drive-ia");
        var gateway = new RcloneLoopbackGateway(process);

        var uri = await gateway.GetPublicLinkAsync("TripDown1905", "TripDown1905.mp4", CancellationToken.None);

        Assert.Contains("archive.org", uri.Host);
        Assert.Contains("TripDown1905", uri.ToString());
    }
}
