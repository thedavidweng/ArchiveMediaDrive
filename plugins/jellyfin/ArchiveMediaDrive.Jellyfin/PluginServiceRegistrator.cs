using MediaBrowser.Common.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace ArchiveMediaDrive.Jellyfin;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection)
    {
        // TODO: register IIaSourceResolver, IRcloneRuntimeManager, IRcloneGateway,
        // ArchiveMediaDriveChannel, scheduled refresh, and managed mount supervisor.
    }
}
