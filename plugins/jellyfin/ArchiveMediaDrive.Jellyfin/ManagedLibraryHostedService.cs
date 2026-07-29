using ArchiveMediaDrive.Core;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArchiveMediaDrive.Jellyfin;

public sealed class ManagedLibraryHostedService : IHostedService, IDisposable
{
    private readonly ManagedLibraryService _managedLibrary;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<ManagedLibraryHostedService> _logger;
    private bool _disposed;

    public ManagedLibraryHostedService(
        ManagedLibraryService managedLibrary,
        ILibraryManager libraryManager,
        ILogger<ManagedLibraryHostedService> logger)
    {
        _managedLibrary = managedLibrary;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        if (!config.ManagedLibraryEnabled)
        {
            _logger.LogInformation("ArchiveMediaDrive Managed Library is disabled, skipping start");
            return;
        }

        try
        {
            await _managedLibrary.StartAsync(cancellationToken);
            if (!_managedLibrary.IsRunning)
            {
                _logger.LogError("ArchiveMediaDrive mount did not start or is not healthy");
                return;
            }

            await ReconcileLibraryAsync(config.ManagedLibraryName, _managedLibrary.MountPoint, cancellationToken);
            _logger.LogInformation("ArchiveMediaDrive Managed Library started at {MountPoint}", _managedLibrary.MountPoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start ArchiveMediaDrive Managed Library");
        }
    }

    private async Task ReconcileLibraryAsync(string name, string mountPoint, CancellationToken cancellationToken)
    {
        var folders = _libraryManager.GetVirtualFolders(true);
        var existing = folders.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            var options = new LibraryOptions
            {
                PathInfos = new[] { new MediaPathInfo(mountPoint) },
            };
            await _libraryManager.AddVirtualFolder(name, CollectionTypeOptions.mixed, options, true).ConfigureAwait(false);
            return;
        }

        var currentPath = existing.Locations.FirstOrDefault();
        if (!string.Equals(currentPath, mountPoint, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(currentPath))
                _libraryManager.RemoveMediaPath(name, currentPath);

            _libraryManager.AddMediaPath(name, new MediaPathInfo(mountPoint));
        }

        var folder = _libraryManager.GetUserRootFolder()
            .Children
            .OfType<CollectionFolder>()
            .FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

        if (folder is not null)
        {
            await folder.ValidateChildren(new Progress<double>(), cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            if (!string.IsNullOrWhiteSpace(config.ManagedLibraryName))
            {
                var existing = _libraryManager.GetVirtualFolders(true)
                    .FirstOrDefault(f => string.Equals(f.Name, config.ManagedLibraryName, StringComparison.OrdinalIgnoreCase));

                if (existing is not null)
                {
                    await _libraryManager.RemoveVirtualFolder(config.ManagedLibraryName, false).ConfigureAwait(false);
                }
            }

            await _managedLibrary.StopAsync(cancellationToken);
            _logger.LogInformation("ArchiveMediaDrive Managed Library stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop ArchiveMediaDrive Managed Library");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _managedLibrary.Dispose();
    }
}
