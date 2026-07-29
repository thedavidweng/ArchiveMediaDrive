using ArchiveMediaDrive.Core;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace ArchiveMediaDrive.Emby;

public sealed class ArchiveMediaDriveChannel : IChannel
{
    public string Name => "ArchiveMediaDrive";
    public string Description => "Internet Archive items, Collections, Favorites, and searches.";
    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

    public async Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
    {
        var plugin = Plugin.Instance;
        var options = plugin?.GetCurrentOptions();
        if (options is null || !options.ChannelEnabled)
            return new ChannelItemResult { Items = new List<ChannelItemInfo>(), TotalRecordCount = 0 };

        var sources = EmbySourceMapper.Map(options);
        var page = await plugin!.ChannelMapping.GetItemsAsync(query.FolderId ?? "", sources, cancellationToken);
        var all = page.Items
            .Where(i => i.Kind != ChannelItemKind.NonPlayable)
            .Select(MapToChannelItemInfo)
            .ToList();

        var start = query.StartIndex ?? 0;
        var limit = query.Limit is > 0 ? query.Limit.Value : all.Count;
        var items = all.Skip(start).Take(limit).ToList();

        return new ChannelItemResult { Items = items, TotalRecordCount = all.Count };
    }

    public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        => Task.FromResult(new DynamicImageResponse());

    public IEnumerable<ImageType> GetSupportedChannelImages() => Array.Empty<ImageType>();

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
            Id = dto.Id ?? "",
        },
    };
}
