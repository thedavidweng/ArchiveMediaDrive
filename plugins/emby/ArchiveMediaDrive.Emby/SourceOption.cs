using System.ComponentModel;
using ArchiveMediaDrive.Core;
using Emby.Web.GenericEdit;

namespace ArchiveMediaDrive.Emby;

public sealed class SourceOption : EditableOptionsBase
{
    public override string EditorTitle => "Internet Archive Source";

    [DisplayName("Id")]
    [Description("A unique, URL-safe identifier for this source.")]
    public string Id { get; set; } = string.Empty;

    [DisplayName("Name")]
    [Description("The display name for this source.")]
    public string Name { get; set; } = string.Empty;

    [DisplayName("Kind")]
    [Description("The Internet Archive source type.")]
    public SourceKind Kind { get; set; }

    [DisplayName("Value")]
    [Description("The Archive.org identifier, collection name, username, or search expression.")]
    public string Value { get; set; } = string.Empty;

    [DisplayName("Enabled")]
    [Description("Enable this source in the channel and managed library.")]
    public bool Enabled { get; set; } = true;

    [DisplayName("Refresh interval (minutes)")]
    [Description("How often to refresh this source from Archive.org.")]
    public int RefreshMinutes { get; set; } = 360;
}
