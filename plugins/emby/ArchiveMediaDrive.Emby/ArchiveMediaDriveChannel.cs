using System.Text.Json;
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
    private readonly ChannelMappingService _mapping;

    public ArchiveMediaDriveChannel()
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var sources = LoadSources(config.SourcesJson);
        var resolver = new IaSourceResolver(new HttpClient());
        var gateway = CreateGateway();
        _mapping = new ChannelMappingService(resolver, gateway, sources);
    }

    public string Name => "ArchiveMediaDrive";
    public string Description => "Internet Archive items, Collections, Favorites, and searches.";
    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

    public async Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
    {
        var page = await _mapping.GetItemsAsync(query.FolderId ?? "", cancellationToken);
        var items = page.Items.Select(MapToChannelItemInfo).ToList();
        return new ChannelItemResult { Items = items, TotalRecordCount = items.Count };
    }

    public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        => Task.FromResult(new DynamicImageResponse());

    public IEnumerable<ImageType> GetSupportedChannelImages() => Array.Empty<ImageType>();

    private static IReadOnlyList<SourceDefinition> LoadSources(string sourcesJson)
    {
        if (string.IsNullOrEmpty(sourcesJson))
            return Array.Empty<SourceDefinition>();

        try
        {
            var doc = JsonDocument.Parse(sourcesJson);
            var sources = new List<SourceDefinition>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var kind = element.GetProperty("kind").GetString()!;
                sources.Add(new SourceDefinition
                {
                    Id = element.GetProperty("id").GetString()!,
                    Name = element.GetProperty("name").GetString()!,
                    Kind = (SourceKind)Enum.Parse(typeof(SourceKind), kind, true),
                    Value = element.GetProperty("value").GetString()!,
                    Enabled = element.GetProperty("enabled").GetBoolean(),
                    RefreshMinutes = element.GetProperty("refreshMinutes").GetInt32(),
                });
            }
            return sources;
        }
        catch
        {
            return Array.Empty<SourceDefinition>();
        }
    }

    private static IRcloneGateway CreateGateway()
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
            return new NullRcloneGateway();

        var pluginDir = GetPluginDataPath(plugin);
        var rcloneBinary = Path.Combine(pluginDir, "rclone", "rclone");
        var configPath = Path.Combine(pluginDir, "rclone", "rclone.conf");
        var process = new RcloneProcess(rcloneBinary, configPath, "archive-media-drive-ia");
        return new RcloneLoopbackGateway(process);
    }

    private static string GetPluginDataPath(Plugin plugin)
    {
        var path = plugin.GetType().GetProperty("DataPath")?.GetValue(plugin) as string;
        if (!string.IsNullOrEmpty(path)) return path!;
        path = plugin.GetType().GetProperty("DataFolderPath")?.GetValue(plugin) as string;
        if (!string.IsNullOrEmpty(path)) return path!;
        return AppContext.BaseDirectory;
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
        },
    };

    private sealed class NullRcloneGateway : IRcloneGateway
    {
        public Task<IReadOnlyList<RawNode>> ListAsync(string identifier, string relativePath, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<RawNode>>(Array.Empty<RawNode>());

        public Task<Uri> GetPublicLinkAsync(string identifier, string relativePath, CancellationToken cancellationToken)
            => Task.FromResult(new Uri($"https://archive.org/download/{identifier}/{relativePath}"));

        public Task<RcloneProbe> ProbeAsync(CancellationToken cancellationToken)
            => Task.FromResult(new RcloneProbe());
    }
}
