using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace ArchiveMediaDrive.Emby;

public sealed class ArchiveMediaDriveChannel : IChannel
{
    public string Name => "ArchiveMediaDrive";
    public string Description => "Internet Archive items, Collections, Favorites, and searches.";
    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

    public Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
        => throw new NotImplementedException("Map raw provider nodes into Emby Channel items as specified.");

    public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        => Task.FromResult(new DynamicImageResponse());

    public IEnumerable<ImageType> GetSupportedChannelImages() => Array.Empty<ImageType>();
}
