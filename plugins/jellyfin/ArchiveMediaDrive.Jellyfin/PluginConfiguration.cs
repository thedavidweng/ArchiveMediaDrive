using MediaBrowser.Model.Plugins;

namespace ArchiveMediaDrive.Jellyfin;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public string SourcesJson { get; set; } = "[]";
    public bool ChannelEnabled { get; set; } = true;
    public bool ManagedLibraryEnabled { get; set; }
    public string ManagedLibraryName { get; set; } = "Internet Archive";
}
