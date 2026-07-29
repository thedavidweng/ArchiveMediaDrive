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
    private readonly Func<IReadOnlyList<SourceDefinition>> _getSources;
    private readonly ILogger<ChannelService> _logger;

    public ChannelService(
        ChannelMappingService mapping,
        Func<IReadOnlyList<SourceDefinition>> getSources,
        ILogger<ChannelService> logger)
    {
        _mapping = mapping;
        _getSources = getSources;
        _logger = logger;
    }

    public async Task<ChannelItemResult> GetChannelItemsAsync(InternalChannelItemQuery query, CancellationToken cancellationToken)
    {
        try
        {
            var sources = _getSources();
            var page = await _mapping.GetItemsAsync(query.FolderId ?? "", sources, cancellationToken);
            var all = page.Items
                .Where(i => i.Kind != ChannelItemKind.NonPlayable)
                .Select(MapToChannelItemInfo)
                .ToList();

            var start = query.StartIndex ?? 0;
            var limit = query.Limit is > 0 ? query.Limit.Value : all.Count;
            var items = all.Skip(start).Take(limit).ToList();

            return new ChannelItemResult { Items = items, TotalRecordCount = all.Count };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to get channel items for folder {FolderId}", query.FolderId);
            return new ChannelItemResult { Items = new List<ChannelItemInfo>(), TotalRecordCount = 0 };
        }
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
