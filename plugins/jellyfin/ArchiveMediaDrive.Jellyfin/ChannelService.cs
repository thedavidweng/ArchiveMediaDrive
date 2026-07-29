using System.Text.Json;
using ArchiveMediaDrive.Core;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace ArchiveMediaDrive.Jellyfin;

public sealed class ChannelService
{
    private static readonly string[] PlayableFormats =
    {
        "MPEG4", "Matroska", "h.264", "AVI", "QuickTime", "WebM", "Ogg Video",
        "MP3", "Flac", "Ogg Audio", "Vorbis", "WAV", "AAC",
    };

    private static readonly HashSet<string> PlayableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".ogv", ".mpeg", ".mpg", ".m4v",
        ".mp3", ".flac", ".ogg", ".oga", ".wav", ".m4a", ".aac", ".opus", ".weba",
    };

    private readonly IIaSourceResolver _resolver;
    private readonly IRcloneGateway _gateway;
    private readonly IReadOnlyList<SourceDefinition> _sources;
    private readonly ILogger<ChannelService> _logger;

    public ChannelService(
        IIaSourceResolver resolver,
        IRcloneGateway gateway,
        IReadOnlyList<SourceDefinition> sources,
        ILogger<ChannelService> logger)
    {
        _resolver = resolver;
        _gateway = gateway;
        _sources = sources;
        _logger = logger;
    }

    public async Task<ChannelItemResult> GetChannelItemsAsync(InternalChannelItemQuery query, CancellationToken cancellationToken)
    {
        var folderId = query.FolderId ?? "";

        if (string.IsNullOrEmpty(folderId))
            return ListSources();

        if (folderId.StartsWith("source/", StringComparison.Ordinal))
            return await ListItemsInSourceAsync(folderId["source/".Length..], cancellationToken);

        if (folderId.StartsWith("item/", StringComparison.Ordinal))
            return await ListFilesInItemAsync(folderId["item/".Length..], cancellationToken);

        return new ChannelItemResult { Items = new List<ChannelItemInfo>(), TotalRecordCount = 0 };
    }

    private ChannelItemResult ListSources()
    {
        var items = new List<ChannelItemInfo>();
        foreach (var source in _sources)
        {
            if (!source.Enabled) continue;
            items.Add(new ChannelItemInfo
            {
                Name = source.Name,
                Type = ChannelItemType.Folder,
                FolderType = ChannelFolderType.Container,
                Id = $"source/{source.Id}",
            });
        }
        return new ChannelItemResult { Items = items, TotalRecordCount = items.Count };
    }

    private async Task<ChannelItemResult> ListItemsInSourceAsync(string sourceId, CancellationToken cancellationToken)
    {
        var source = _sources.FirstOrDefault(s => s.Id == sourceId);
        if (source is null)
            return new ChannelItemResult { Items = new List<ChannelItemInfo>(), TotalRecordCount = 0 };

        var identifiers = await _resolver.ResolveAsync(source, cancellationToken);
        var items = new List<ChannelItemInfo>();
        foreach (var identifier in identifiers)
        {
            items.Add(new ChannelItemInfo
            {
                Name = identifier,
                Type = ChannelItemType.Folder,
                FolderType = ChannelFolderType.Container,
                Id = $"item/{identifier}",
            });
        }
        return new ChannelItemResult { Items = items, TotalRecordCount = items.Count };
    }

    private async Task<ChannelItemResult> ListFilesInItemAsync(string itemPath, CancellationToken cancellationToken)
    {
        var parts = itemPath.Split('/', 2);
        var identifier = parts[0];
        var relativePath = parts.Length > 1 ? parts[1] : "";

        var nodes = await _gateway.ListAsync(identifier, relativePath, cancellationToken);
        var items = new List<ChannelItemInfo>();
        foreach (var node in nodes)
        {
            if (node.Kind == RawNodeKind.Directory)
            {
                items.Add(new ChannelItemInfo
                {
                    Name = node.Name,
                    Type = ChannelItemType.Folder,
                    FolderType = ChannelFolderType.Container,
                    Id = $"item/{identifier}/{node.Path}",
                });
            }
            else if (IsPlayable(node))
            {
                var url = await _gateway.GetPublicLinkAsync(identifier, node.Path, cancellationToken);
                items.Add(new ChannelItemInfo
                {
                    Name = node.Name,
                    Type = ChannelItemType.Media,
                    MediaType = GetMediaType(node),
                    MediaSources =
                    {
                        new MediaSourceInfo
                        {
                            Path = url.ToString(),
                            Protocol = MediaProtocol.Http,
                        },
                    },
                });
            }
            else
            {
                items.Add(new ChannelItemInfo
                {
                    Name = node.Name,
                    Type = ChannelItemType.Folder,
                });
            }
        }
        return new ChannelItemResult { Items = items, TotalRecordCount = items.Count };
    }

    private static bool IsPlayable(RawNode node)
    {
        if (!string.IsNullOrEmpty(node.Format) && PlayableFormats.Contains(node.Format, StringComparer.OrdinalIgnoreCase))
            return true;
        var ext = Path.GetExtension(node.Name);
        return PlayableExtensions.Contains(ext);
    }

    private static ChannelMediaType GetMediaType(RawNode node)
    {
        var ext = Path.GetExtension(node.Name).ToLowerInvariant();
        return ext switch
        {
            ".mp3" or ".flac" or ".ogg" or ".oga" or ".wav" or ".m4a" or ".aac" or ".opus" or ".weba" => ChannelMediaType.Audio,
            _ => ChannelMediaType.Video,
        };
    }
}
