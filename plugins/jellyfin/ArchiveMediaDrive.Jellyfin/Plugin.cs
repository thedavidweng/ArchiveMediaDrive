using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;

namespace ArchiveMediaDrive.Jellyfin;

public sealed class Plugin : BasePlugin<PluginConfiguration>
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer) => Instance = this;

    public static Plugin? Instance { get; private set; }
    public override Guid Id => Guid.Parse("14c1491f-2509-4ea6-9226-613ca9971ed8");
    public override string Name => "ArchiveMediaDrive";
    public override string Description => "Use Internet Archive as a Jellyfin media source.";
}
