using System.Reflection;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;

namespace ArchiveMediaDrive.Emby;

public sealed class PluginConfigurationPage : IPluginConfigurationPage
{
    public string Name => "ArchiveMediaDrive";

    public ConfigurationPageType ConfigurationPageType => ConfigurationPageType.PluginConfiguration;

    public IPlugin Plugin => ArchiveMediaDrive.Emby.Plugin.Instance!;

    public Stream GetHtmlStream()
    {
        var assembly = GetType().GetTypeInfo().Assembly;
        return assembly.GetManifestResourceStream($"{GetType().Namespace}.config.html")
            ?? throw new InvalidOperationException("ArchiveMediaDrive config.html embedded resource is missing");
    }
}
