using ArchiveMediaDrive.Core;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace ArchiveMediaDrive.Jellyfin;

public sealed class ChannelService
{
    private readonly ChannelMappingService _mapping;
    private readonly RcloneEnvironment _rcloneEnvironment;
    private readonly ILogger<ChannelService> _logger;
    private int _runtimeEnsured;

    public ChannelService(
        IIaSourceResolver resolver,
        IRcloneGateway gateway,
        RcloneEnvironment rcloneEnvironment,
        IReadOnlyList<SourceDefinition> sources,
        ILogger<ChannelService> logger)
    {
        _mapping = new ChannelMappingService(resolver, gateway, sources);
        _rcloneEnvironment = rcloneEnvironment;
        _logger = logger;
    }

    public async Task<ChannelItemResult> GetChannelItemsAsync(InternalChannelItemQuery query, CancellationToken cancellationToken)
    {
        try
        {
            await EnsureRuntimeReadyAsync(cancellationToken);
            var page = await _mapping.GetItemsAsync(query.FolderId ?? "", cancellationToken);
            var items = page.Items.Select(MapToChannelItemInfo).ToList();
            return new ChannelItemResult { Items = items, TotalRecordCount = items.Count };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to get channel items for folder {FolderId}", query.FolderId);
            return new ChannelItemResult { Items = new List<ChannelItemInfo>(), TotalRecordCount = 0 };
        }
    }

    private async Task EnsureRuntimeReadyAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _runtimeEnsured, 1, 0) == 0)
            await _rcloneEnvironment.EnsureReadyAsync(cancellationToken);
    }

    private static ChannelItemInfo MapToChannelItemInfo(ChannelItemDto dto) => dto.Kind switch
    {
        ChannelItemKind.Folder => new ChannelItemInfo
        {
            Name = dto.Name,
            Type = ChannelItemType.Folder,
            FolderType = ChannelFolderType.Container,
            Id = dto.Id,
        },
        ChannelItemKind.Media => new ChannelItemInfo
        {
            Name = dto.Name,
            Type = ChannelItemType.Media,
            MediaType = dto.MediaType == "Audio" ? ChannelMediaType.Audio : ChannelMediaType.Video,
            MediaSources =
            {
                new MediaSourceInfo
                {
                    Path = dto.MediaUrl ?? "",
                    Protocol = MediaProtocol.Http,
                },
            },
        },
        _ => new ChannelItemInfo
        {
            Name = dto.Name,
            Type = ChannelItemType.Folder,
            FolderType = ChannelFolderType.Container,
            Id = dto.Id,
        },
    };
}
