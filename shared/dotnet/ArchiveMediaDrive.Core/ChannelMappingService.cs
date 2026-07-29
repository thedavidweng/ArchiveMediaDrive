namespace ArchiveMediaDrive.Core;

public enum ChannelItemKind { Folder, Media, NonPlayable }

public sealed class ChannelItemDto
{
    public string Name { get; init; } = "";
    public string Id { get; init; } = "";
    public ChannelItemKind Kind { get; init; }
    public string? MediaType { get; init; }
    public string? MediaUrl { get; init; }
    public long? Size { get; init; }
    public string? Format { get; init; }
}

public sealed class ChannelPageResult
{
    public IReadOnlyList<ChannelItemDto> Items { get; init; } = Array.Empty<ChannelItemDto>();
}

public sealed class ChannelMappingService
{
    private static readonly HashSet<string> PlayableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".ogv", ".mpeg", ".mpg", ".m4v",
        ".mp3", ".flac", ".ogg", ".oga", ".wav", ".m4a", ".aac", ".opus", ".weba",
    };

    private static readonly HashSet<string> PlayableFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "MPEG4", "Matroska", "h.264", "AVI", "QuickTime", "WebM", "Ogg Video",
        "MP3", "Flac", "Ogg Audio", "Vorbis", "WAV", "AAC",
    };

    private readonly IIaSourceResolver _resolver;
    private readonly IRcloneGateway _gateway;
    private readonly IReadOnlyList<SourceDefinition> _sources;

    public ChannelMappingService(
        IIaSourceResolver resolver,
        IRcloneGateway gateway,
        IReadOnlyList<SourceDefinition> sources)
    {
        _resolver = resolver;
        _gateway = gateway;
        _sources = sources;
    }

    public async Task<ChannelPageResult> GetItemsAsync(string folderId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(folderId))
            return ListSources();

        if (folderId.StartsWith("source/", StringComparison.Ordinal))
            return await ListItemsInSourceAsync(folderId.Substring("source/".Length), cancellationToken);

        if (folderId.StartsWith("item/", StringComparison.Ordinal))
            return await ListFilesInItemAsync(folderId.Substring("item/".Length), cancellationToken);

        return new ChannelPageResult();
    }

    private ChannelPageResult ListSources()
    {
        var items = _sources
            .Where(s => s.Enabled)
            .Select(s => new ChannelItemDto
            {
                Name = s.Name,
                Id = $"source/{s.Id}",
                Kind = ChannelItemKind.Folder,
            })
            .ToList();

        return new ChannelPageResult { Items = items };
    }

    private async Task<ChannelPageResult> ListItemsInSourceAsync(string sourceId, CancellationToken cancellationToken)
    {
        var source = _sources.FirstOrDefault(s => s.Id == sourceId);
        if (source is null)
            return new ChannelPageResult();

        var identifiers = await _resolver.ResolveAsync(source, cancellationToken);
        var items = identifiers
            .Select(id => new ChannelItemDto
            {
                Name = id,
                Id = $"item/{id}",
                Kind = ChannelItemKind.Folder,
            })
            .ToList();

        return new ChannelPageResult { Items = items };
    }

    private async Task<ChannelPageResult> ListFilesInItemAsync(string itemPath, CancellationToken cancellationToken)
    {
        var parts = itemPath.Split(new[] { '/' }, 2);
        var identifier = parts[0];
        var relativePath = parts.Length > 1 ? parts[1] : "";

        var nodes = await _gateway.ListAsync(identifier, relativePath, cancellationToken);
        var items = new List<ChannelItemDto>();

        foreach (var node in nodes)
        {
            if (node.Kind == RawNodeKind.Directory)
            {
                items.Add(new ChannelItemDto
                {
                    Name = node.Name,
                    Id = $"item/{identifier}/{node.Path}",
                    Kind = ChannelItemKind.Folder,
                });
            }
            else if (IsPlayable(node))
            {
                var url = await _gateway.GetPublicLinkAsync(identifier, node.Path, cancellationToken);
                items.Add(new ChannelItemDto
                {
                    Name = node.Name,
                    Kind = ChannelItemKind.Media,
                    MediaType = GetMediaType(node.Name),
                    MediaUrl = url.ToString(),
                    Size = node.Size,
                    Format = node.Format,
                });
            }
            else
            {
                items.Add(new ChannelItemDto
                {
                    Name = node.Name,
                    Kind = ChannelItemKind.NonPlayable,
                    Size = node.Size,
                    Format = node.Format,
                });
            }
        }

        return new ChannelPageResult { Items = items };
    }

    private static bool IsPlayable(RawNode node)
    {
        if (!string.IsNullOrEmpty(node.Format) && PlayableFormats.Contains(node.Format!))
            return true;
        var ext = Path.GetExtension(node.Name);
        return PlayableExtensions.Contains(ext ?? "");
    }

    private static string GetMediaType(string filename)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        return ext switch
        {
            ".mp3" or ".flac" or ".ogg" or ".oga" or ".wav" or ".m4a" or ".aac" or ".opus" or ".weba" => "Audio",
            _ => "Video",
        };
    }
}
