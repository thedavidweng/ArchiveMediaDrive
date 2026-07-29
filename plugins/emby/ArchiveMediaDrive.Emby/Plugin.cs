using ArchiveMediaDrive.Core;
using MediaBrowser.Common;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;

namespace ArchiveMediaDrive.Emby;

public sealed class Plugin : BasePluginSimpleUI<PluginOptions>
{
    private readonly IApplicationHost _applicationHost;
    private readonly object _initLock = new();
    private RcloneEnvironment? _rcloneEnvironment;
    private EmbyManagedLibraryService? _managedLibrary;
    private ChannelMappingService? _channelMapping;
    private SourceRefreshService? _sourceRefresh;
    private static readonly HttpClient SharedHttpClient = new();

    public Plugin(IApplicationHost applicationHost)
        : base(applicationHost)
    {
        _applicationHost = applicationHost;
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override Guid Id => Guid.Parse("22b4b6cb-f3c0-44bd-a0ce-8c10e5263402");
    public override string Name => "ArchiveMediaDrive";
    public override string Description => "Use Internet Archive as an Emby media source.";

    public RcloneEnvironment RcloneEnvironment
    {
        get
        {
            EnsureServices();
            return _rcloneEnvironment!;
        }
    }

    public ChannelMappingService ChannelMapping
    {
        get
        {
            EnsureServices();
            return _channelMapping!;
        }
    }

    public PluginOptions GetCurrentOptions()
        => GetOptions();

    public IReadOnlyList<SourceDefinition> GetSourceDefinitions()
        => EmbySourceMapper.Map(GetOptions());

    protected override bool OnOptionsSaving(PluginOptions options)
        => EmbySourceMapper.TryValidate(options, out _);

    protected override void OnOptionsSaved(PluginOptions options)
    {
        _ = ReconcileManagedLibraryAsync(options);
    }

    public override void OnUninstalling()
    {
        try
        {
            _managedLibrary?.Dispose();
        }
        finally
        {
            base.OnUninstalling();
        }
    }

    private async Task ReconcileManagedLibraryAsync(PluginOptions options)
    {
        try
        {
            EnsureServices();
            var libraryManager = _applicationHost.Resolve<MediaBrowser.Controller.Library.ILibraryManager>();
            if (libraryManager is null)
                return;

            await _managedLibrary!.ReconcileAsync(options, libraryManager, default).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private void EnsureServices()
    {
        if (_rcloneEnvironment is not null)
            return;

        lock (_initLock)
        {
            if (_rcloneEnvironment is not null)
                return;

            var dataDir = DataFolderPath;
            Directory.CreateDirectory(dataDir);

            var manifest = RcloneManifestLoader.LoadFromPluginData(dataDir);
            var downloader = new HttpAssetDownloader(SharedHttpClient, manifest.ReleaseBaseUrl);
            var rid = RcloneEnvironment.DetectRid();
            var runtimeManager = new RcloneRuntimeManager(dataDir, manifest, downloader, rid);
            _rcloneEnvironment = new RcloneEnvironment(runtimeManager, Path.Combine(dataDir, "rclone"));

            var resolver = new IaSourceResolver(SharedHttpClient);
            var store = new FileSystemSourceSnapshotStore(Path.Combine(dataDir, "sources"));
            _sourceRefresh = new SourceRefreshService(resolver, store);
            _channelMapping = new ChannelMappingService(_sourceRefresh, store, new RcloneLoopbackGateway(_rcloneEnvironment));

            _managedLibrary = new EmbyManagedLibraryService(
                _rcloneEnvironment,
                resolver,
                dataDir);
        }
    }
}
