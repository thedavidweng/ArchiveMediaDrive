using ArchiveMediaDrive.Core;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace ArchiveMediaDrive.Jellyfin;

public sealed class ArchiveMediaDriveChannel : IChannel
{
    private readonly ChannelService _channelService;

    public ArchiveMediaDriveChannel(ChannelService channelService)
        => _channelService = channelService;

    public string Name => "ArchiveMediaDrive";
    public string Description => "Internet Archive items, Collections, Favorites, and searches.";
    public string DataVersion => "1";
    public string HomePageUrl => "https://archive.org";
    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;
    public bool IsEnabledFor(string userId) => true;
    public InternalChannelFeatures GetChannelFeatures() => new();

    public Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
        => _channelService.GetChannelItemsAsync(query, cancellationToken);

    public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        => Task.FromResult(new DynamicImageResponse { HasImage = false });
    public IEnumerable<ImageType> GetSupportedChannelImages() => Array.Empty<ImageType>();
}
