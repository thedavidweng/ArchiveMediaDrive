using System.Diagnostics;
using System.Text.Json;
using ArchiveMediaDrive.Core;
using Xunit;

namespace ArchiveMediaDrive.Core.Tests;

public sealed class RcloneLoopbackGatewayTests
{
    private sealed class FakeRcloneProcess : IRcloneProcess
    {
        private readonly Func<string, string, CancellationToken, Task<string>> _behavior;

        public FakeRcloneProcess(Func<string, string, CancellationToken, Task<string>> behavior)
            => _behavior = behavior;

        public Task<string> ExecuteAsync(string command, string jsonInput, CancellationToken cancellationToken)
            => _behavior(command, jsonInput, cancellationToken);
    }

    private static string ListResponse(params (string name, bool isDir, long size, string format)[] entries)
    {
        var docs = entries.Select(e => new
        {
            Name = e.name,
            IsDir = e.isDir,
            Size = e.size,
            MimeType = e.isDir ? "inode/directory" : "application/octet-stream",
            Formatted = e.format,
        }).ToArray();
        return JsonSerializer.Serialize(new { list = docs });
    }

    [Fact]
    public async Task ListAsync_returns_directory_and_file_nodes_preserving_order()
    {
        var fake = new FakeRcloneProcess((cmd, input, _) =>
        {
            Assert.Equal("operations/list", cmd);
            var doc = JsonDocument.Parse(input);
            Assert.Equal("archive-media-drive-ia:TripDown1905", doc.RootElement.GetProperty("fs").GetString());
            Assert.Equal("", doc.RootElement.GetProperty("remote").GetString());
            return Task.FromResult(ListResponse(
                ("TripDown1905.mp4", false, 1000, "MPEG4"),
                ("thumbs", true, 0, ""),
                ("TripDown1905.srt", false, 200, "SubRip")
            ));
        });
        var gateway = new RcloneLoopbackGateway(fake);

        var nodes = await gateway.ListAsync("TripDown1905", "", CancellationToken.None);

        Assert.Equal(3, nodes.Count);
        Assert.Equal(RawNodeKind.File, nodes[0].Kind);
        Assert.Equal("TripDown1905.mp4", nodes[0].Name);
        Assert.Equal(1000, nodes[0].Size);
        Assert.Equal("MPEG4", nodes[0].Format);
        Assert.Equal(RawNodeKind.Directory, nodes[1].Kind);
        Assert.Equal("thumbs", nodes[1].Name);
        Assert.Equal(RawNodeKind.File, nodes[2].Kind);
    }

    [Fact]
    public async Task ListAsync_with_relative_path_passes_remote_subpath()
    {
        var capturedRemote = "";
        var fake = new FakeRcloneProcess((_, input, _) =>
        {
            var doc = JsonDocument.Parse(input);
            capturedRemote = doc.RootElement.GetProperty("remote").GetString()!;
            return Task.FromResult(ListResponse(("t1.jpg", false, 200, "Thumbnail")));
        });
        var gateway = new RcloneLoopbackGateway(fake);

        await gateway.ListAsync("TripDown1905", "thumbs", CancellationToken.None);

        Assert.Equal("thumbs", capturedRemote);
    }

    [Fact]
    public async Task ListAsync_rejects_path_traversal_in_relative_path()
    {
        var fake = new FakeRcloneProcess((_, _, _) => Task.FromResult(ListResponse()));
        var gateway = new RcloneLoopbackGateway(fake);

        await Assert.ThrowsAsync<RcloneGatewayException>(() =>
            gateway.ListAsync("TripDown1905", "../escape", CancellationToken.None));
    }

    [Fact]
    public async Task GetPublicLinkAsync_returns_uri_from_rclone_response()
    {
        var fake = new FakeRcloneProcess((cmd, input, _) =>
        {
            Assert.Equal("operations/publiclink", cmd);
            var doc = JsonDocument.Parse(input);
            Assert.Equal("archive-media-drive-ia:TripDown1905", doc.RootElement.GetProperty("fs").GetString());
            Assert.Equal("TripDown1905.mp4", doc.RootElement.GetProperty("remote").GetString());
            return Task.FromResult(JsonSerializer.Serialize(new { url = "https://archive.org/download/TripDown1905/TripDown1905.mp4" }));
        });
        var gateway = new RcloneLoopbackGateway(fake);

        var uri = await gateway.GetPublicLinkAsync("TripDown1905", "TripDown1905.mp4", CancellationToken.None);

        Assert.Equal("https://archive.org/download/TripDown1905/TripDown1905.mp4", uri.ToString());
    }

    [Fact]
    public async Task ProbeAsync_parses_version_platform_architecture()
    {
        var fake = new FakeRcloneProcess((cmd, _, _) =>
        {
            Assert.Equal("core/version", cmd);
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                version = "v1.74.4",
                os = "darwin",
                arch = "arm64",
            }));
        });
        var gateway = new RcloneLoopbackGateway(fake);

        var probe = await gateway.ProbeAsync(CancellationToken.None);

        Assert.Equal("v1.74.4", probe.Version);
        Assert.Equal("darwin", probe.Platform);
        Assert.Equal("arm64", probe.Architecture);
    }

    [Fact]
    public async Task Rclone_nonzero_exit_raises_gateway_exception()
    {
        var fake = new FakeRcloneProcess((_, _, _) =>
            throw new RcloneProcessException("rclone exited with code 1: error: not found"));
        var gateway = new RcloneLoopbackGateway(fake);

        var ex = await Assert.ThrowsAsync<RcloneGatewayException>(() =>
            gateway.ListAsync("TripDown1905", "", CancellationToken.None));
        Assert.Contains("rclone exited", ex.Message);
    }
}
