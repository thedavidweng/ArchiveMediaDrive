using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;

namespace ArchiveMediaDrive.Emby;

public sealed class Plugin : BasePlugin<PluginConfiguration>
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer) => Instance = this;

    public static Plugin? Instance { get; private set; }
    public override Guid Id => Guid.Parse("22b4b6cb-f3c0-44bd-a0ce-8c10e5263402");
    public override string Name => "ArchiveMediaDrive";
    public override string Description => "Use Internet Archive as an Emby media source.";
}
