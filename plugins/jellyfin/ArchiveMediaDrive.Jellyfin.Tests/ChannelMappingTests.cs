using ArchiveMediaDrive.Core;
using ArchiveMediaDrive.Jellyfin;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ArchiveMediaDrive.Jellyfin.Tests;

public sealed class ChannelMappingTests
{
    private sealed class FakeResolver : IIaSourceResolver
    {
        public Task<IReadOnlyList<string>> ResolveAsync(SourceDefinition source, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>(new[] { "alpha", "beta" });
    }

    private sealed class FakeGateway : IRcloneGateway
    {
        public Task<IReadOnlyList<RawNode>> ListAsync(string identifier, string relativePath, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<RawNode>>(new[]
            {
                new RawNode { Kind = RawNodeKind.File, Name = "alpha.mp4", Path = "alpha.mp4", Identifier = identifier, Size = 1000, Format = "MPEG4" },
                new RawNode { Kind = RawNodeKind.Directory, Name = "thumbs", Path = "thumbs", Identifier = identifier },
                new RawNode { Kind = RawNodeKind.File, Name = "alpha.srt", Path = "alpha.srt", Identifier = identifier, Size = 200, Format = "SubRip" },
            });

        public Task<Uri> GetPublicLinkAsync(string identifier, string relativePath, CancellationToken cancellationToken)
            => Task.FromResult(new Uri($"https://archive.org/download/{identifier}/{relativePath}"));

        public Task<RcloneProbe> ProbeAsync(CancellationToken cancellationToken)
            => Task.FromResult(new RcloneProbe { Version = "v1.74.4", Platform = "linux", Architecture = "arm64" });
    }

    private sealed class FakeRuntimeManager : IRcloneRuntimeManager
    {
        public string ExecutablePath => "/tmp/amd-fake-rclone";
        public string RuntimeDirectory => "/tmp/amd-fake-rclone-dir";
        public string ReceiptPath => "/tmp/amd-fake-rclone-dir/receipt.json";
        public Task<string> EnsureInstalledAsync(CancellationToken cancellationToken) => Task.FromResult(ExecutablePath);
        public Task VerifyAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RepairAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RemoveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static SourceDefinition[] Sources => new[]
    {
        new SourceDefinition { Id = "prelinger", Name = "Prelinger", Kind = SourceKind.Collection, Value = "prelinger" },
        new SourceDefinition { Id = "tripdown", Name = "Trip Down", Kind = SourceKind.Item, Value = "TripDown1905" },
    };

    [Fact]
    public async Task Root_folder_lists_configured_sources_as_folders()
    {
        var service = new ChannelService(new FakeResolver(), new FakeGateway(), Sources, NullLogger<ChannelService>.Instance);

        var result = await service.GetChannelItemsAsync(new InternalChannelItemQuery { FolderId = "" }, CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal(ChannelItemType.Folder, item.Type));
        Assert.Contains(result.Items, i => i.Name == "Prelinger");
        Assert.Contains(result.Items, i => i.Name == "Trip Down");
    }

    [Fact]
    public async Task Source_folder_lists_resolved_item_identifiers_as_folders()
    {
        var service = new ChannelService(new FakeResolver(), new FakeGateway(), Sources, NullLogger<ChannelService>.Instance);

        var result = await service.GetChannelItemsAsync(new InternalChannelItemQuery { FolderId = "source/prelinger" }, CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal(ChannelItemType.Folder, item.Type));
        Assert.Contains(result.Items, i => i.Name == "alpha");
    }

    [Fact]
    public async Task Item_folder_lists_files_and_directories_from_rclone()
    {
        var service = new ChannelService(new FakeResolver(), new FakeGateway(), Sources, NullLogger<ChannelService>.Instance);

        var result = await service.GetChannelItemsAsync(new InternalChannelItemQuery { FolderId = "item/alpha" }, CancellationToken.None);

        Assert.Equal(3, result.Items.Count);
        var mp4 = result.Items.FirstOrDefault(i => i.Name == "alpha.mp4");
        Assert.NotNull(mp4);
        Assert.Equal(ChannelItemType.Media, mp4!.Type);

        var thumbs = result.Items.FirstOrDefault(i => i.Name == "thumbs");
        Assert.NotNull(thumbs);
        Assert.Equal(ChannelItemType.Folder, thumbs!.Type);
    }

    [Fact]
    public async Task Media_item_has_public_url_as_media_source()
    {
        var service = new ChannelService(new FakeResolver(), new FakeGateway(), Sources, NullLogger<ChannelService>.Instance);

        var result = await service.GetChannelItemsAsync(new InternalChannelItemQuery { FolderId = "item/alpha" }, CancellationToken.None);

        var mp4 = result.Items.First(i => i.Name == "alpha.mp4");
        Assert.NotEmpty(mp4.MediaSources);
        var source = mp4.MediaSources[0];
        Assert.Contains("archive.org/download/alpha/alpha.mp4", source.Path);
    }

    [Fact]
    public async Task Subdirectory_navigation_uses_relative_path()
    {
        var service = new ChannelService(new FakeResolver(), new FakeGateway(), Sources, NullLogger<ChannelService>.Instance);

        var result = await service.GetChannelItemsAsync(new InternalChannelItemQuery { FolderId = "item/alpha/thumbs" }, CancellationToken.None);

        Assert.NotEmpty(result.Items);
    }
}
