using System.Text.Json;
using ArchiveMediaDrive.Core;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ArchiveMediaDrive.Jellyfin;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        var plugin = Plugin.Instance
            ?? throw new InvalidOperationException("ArchiveMediaDrive plugin instance is not initialized");
        var dataDir = Path.Combine(plugin.ApplicationPaths.ProgramDataPath, "plugins", "ArchiveMediaDrive");
        Directory.CreateDirectory(dataDir);

        serviceCollection.AddSingleton<HttpClient>();

        serviceCollection.AddSingleton<IRcloneRuntimeManager>(sp =>
        {
            var http = sp.GetRequiredService<HttpClient>();
            var manifest = RcloneManifestLoader.LoadFromPluginData(dataDir);
            var downloader = new HttpAssetDownloader(http, manifest.ReleaseBaseUrl);
            var rid = RcloneEnvironment.DetectRid();
            return new RcloneRuntimeManager(dataDir, manifest, downloader, rid);
        });

        serviceCollection.AddSingleton<RcloneEnvironment>(sp =>
        {
            var runtime = sp.GetRequiredService<IRcloneRuntimeManager>();
            var configDir = Path.Combine(dataDir, "rclone");
            return new RcloneEnvironment(runtime, configDir);
        });

        serviceCollection.AddSingleton<IIaSourceResolver, IaSourceResolver>();
        serviceCollection.AddSingleton<ISourceSnapshotStore>(_ =>
            new FileSystemSourceSnapshotStore(Path.Combine(dataDir, "sources")));

        serviceCollection.AddSingleton<IRcloneGateway>(sp =>
        {
            var env = sp.GetRequiredService<RcloneEnvironment>();
            return new RcloneLoopbackGateway(env);
        });

        serviceCollection.AddSingleton<IReadOnlyList<SourceDefinition>>(_ =>
        {
            var plugin = Plugin.Instance;
            var config = plugin?.Configuration ?? new PluginConfiguration();
            return LoadSources(config.SourcesJson);
        });

        serviceCollection.AddSingleton<ChannelService>();
        serviceCollection.AddSingleton<ArchiveMediaDriveChannel>();

        serviceCollection.AddSingleton<ManagedLibraryService>(sp =>
        {
            var env = sp.GetRequiredService<RcloneEnvironment>();
            var resolver = sp.GetRequiredService<IIaSourceResolver>();
            var sources = sp.GetRequiredService<IReadOnlyList<SourceDefinition>>();
            var plugin = Plugin.Instance;
            var config = plugin?.Configuration ?? new PluginConfiguration();
            var mountPoint = Path.Combine(dataDir, "mount");
            return new ManagedLibraryService(
                new ProcessMountProcessFactory(),
                env,
                sources,
                resolver,
                mountPoint,
                config.ManagedLibraryName);
        });
        serviceCollection.AddHostedService<ManagedLibraryHostedService>();
    }

    private static IReadOnlyList<SourceDefinition> LoadSources(string sourcesJson)
    {
        if (string.IsNullOrEmpty(sourcesJson))
            return Array.Empty<SourceDefinition>();

        try
        {
            var doc = JsonDocument.Parse(sourcesJson);
            var sources = new List<SourceDefinition>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var kind = element.GetProperty("kind").GetString()!;
                sources.Add(new SourceDefinition
                {
                    Id = element.GetProperty("id").GetString()!,
                    Name = element.GetProperty("name").GetString()!,
                    Kind = (SourceKind)Enum.Parse(typeof(SourceKind), kind, true),
                    Value = element.GetProperty("value").GetString()!,
                    Enabled = element.GetProperty("enabled").GetBoolean(),
                    RefreshMinutes = element.GetProperty("refreshMinutes").GetInt32(),
                });
            }
            return sources;
        }
        catch
        {
            return Array.Empty<SourceDefinition>();
        }
    }
}
