namespace ArchiveMediaDrive.Core;

public enum SourceKind { Item, Collection, Favorites, Search }
public enum RawNodeKind { Source, Item, Directory, File }

public sealed class SourceDefinition
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SourceKind Kind { get; set; }
    public string Value { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int RefreshMinutes { get; set; } = 360;
    public string? AuthenticationRef { get; set; }
}

public sealed class RawNode
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public RawNodeKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? SourceId { get; set; }
    public string? Identifier { get; set; }
    public long? Size { get; set; }
    public string? Format { get; set; }
    public string? IaSource { get; set; }
    public string? Revision { get; set; }
    public Uri? PublicUrl { get; set; }
}

public sealed class RcloneProbe
{
    public string Version { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
}
