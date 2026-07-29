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
    private readonly RcloneEnvironment _rcloneEnvironment;

    private static readonly HttpClient SharedHttpClient = new();

    public ArchiveMediaDriveChannel()
    {
        var plugin = Plugin.Instance;
        var config = plugin?.Configuration ?? new PluginConfiguration();
        var sources = LoadSources(config.SourcesJson);

        var dataDir = GetDataDir(plugin);
        Directory.CreateDirectory(dataDir);

        var manifestPath = Path.Combine(dataDir, "runtime", "rclone", "manifest.json");
        var manifest = File.Exists(manifestPath)
            ? RcloneManifestLoader.Load(manifestPath)
            : RcloneManifestLoader.Load(Path.Combine("runtime", "rclone", "manifest.json"));
        var downloader = new HttpAssetDownloader(SharedHttpClient, manifest.ReleaseBaseUrl);
        var rid = RcloneEnvironment.DetectRid();
        var runtimeManager = new RcloneRuntimeManager(dataDir, manifest, downloader, rid);
        _rcloneEnvironment = new RcloneEnvironment(runtimeManager, Path.Combine(dataDir, "rclone"));

        var resolver = new IaSourceResolver(SharedHttpClient);
        var gateway = new RcloneLoopbackGateway(runtimeManager, _rcloneEnvironment.ConfigPath);
        _mapping = new ChannelMappingService(resolver, gateway, sources);
    }

    public string Name => "ArchiveMediaDrive";
    public string Description => "Internet Archive items, Collections, Favorites, and searches.";
    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

    public async Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        if (!config.ChannelEnabled)
            return new ChannelItemResult { Items = new List<ChannelItemInfo>(), TotalRecordCount = 0 };

        await _rcloneEnvironment.EnsureReadyAsync(cancellationToken);
        var page = await _mapping.GetItemsAsync(query.FolderId ?? "", cancellationToken);
        var items = page.Items.Select(MapToChannelItemInfo).ToList();
        return new ChannelItemResult { Items = items, TotalRecordCount = items.Count };
    }

    public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        => Task.FromResult(new DynamicImageResponse());

    public IEnumerable<ImageType> GetSupportedChannelImages() => Array.Empty<ImageType>();

    private static string GetDataDir(Plugin? plugin)
    {
        if (plugin is null)
            return AppContext.BaseDirectory;
        return Path.Combine(plugin.ApplicationPaths.ProgramDataPath, "plugins", "ArchiveMediaDrive");
    }

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
