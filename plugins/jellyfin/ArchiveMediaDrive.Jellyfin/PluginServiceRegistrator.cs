using ArchiveMediaDrive.Core;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArchiveMediaDrive.Jellyfin;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IIaSourceResolver, IaSourceResolver>();
        serviceCollection.AddSingleton<IRcloneGateway>(sp =>
        {
            var runtime = sp.GetRequiredService<IRcloneRuntimeManager>();
            return new RcloneLoopbackGateway(runtime);
        });
        serviceCollection.AddSingleton<ChannelService>();
        serviceCollection.AddSingleton<ArchiveMediaDriveChannel>();
    }
}
