using System.Globalization;
using ArchiveMediaDrive.Core;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;

namespace ArchiveMediaDrive.Emby;

public sealed class EmbyManagedLibraryService : IDisposable
{
    private readonly RcloneEnvironment _rcloneEnvironment;
    private readonly IIaSourceResolver _resolver;
    private readonly string _dataDir;
    private ManagedLibraryService? _managedLibrary;
    private bool _disposed;

    public EmbyManagedLibraryService(RcloneEnvironment rcloneEnvironment, IIaSourceResolver resolver, string dataDir)
    {
        _rcloneEnvironment = rcloneEnvironment;
        _resolver = resolver;
        _dataDir = dataDir;
    }

    public async Task ReconcileAsync(PluginOptions options, ILibraryManager libraryManager, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        var sources = EmbySourceMapper.Map(options);
        var mountPoint = Path.Combine(_dataDir, "mount");
        var libraryName = options.ManagedLibraryName;

        if (!options.ManagedLibraryEnabled || sources.Count == 0)
        {
            await RemoveLibraryAsync(libraryManager, cancellationToken).ConfigureAwait(false);
            await StopMountAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        _managedLibrary?.Dispose();
        _managedLibrary = new ManagedLibraryService(
            new ProcessMountProcessFactory(),
            _rcloneEnvironment,
            sources,
            _resolver,
            mountPoint,
            libraryName);

        await _managedLibrary.StartAsync(cancellationToken).ConfigureAwait(false);

        if (!_managedLibrary.IsRunning)
            return;

        await ReconcileLibraryAsync(libraryManager, libraryName, _managedLibrary.MountPoint, cancellationToken).ConfigureAwait(false);
    }

    private async Task StopMountAsync(CancellationToken cancellationToken)
    {
        if (_managedLibrary is null)
            return;

        try
        {
            await _managedLibrary.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _managedLibrary.Dispose();
            _managedLibrary = null;
        }
    }

    private async Task RemoveLibraryAsync(ILibraryManager libraryManager, CancellationToken cancellationToken)
    {
        var folders = libraryManager.GetVirtualFolders();
        foreach (var folder in folders)
        {
            if (TryParseId(folder.Id, out var id))
            {
                try
                {
                    libraryManager.RemoveVirtualFolder(id, false);
                }
                catch
                {
                    // Library may already be removed.
                }
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task ReconcileLibraryAsync(ILibraryManager libraryManager, string name, string mountPoint, CancellationToken cancellationToken)
    {
        var folders = libraryManager.GetVirtualFolders();
        var existing = folders.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            var options = new LibraryOptions
            {
                PathInfos = new[] { new MediaPathInfo { Path = mountPoint } },
            };
            libraryManager.AddVirtualFolder(name, options, true);
            return;
        }

        if (!TryParseId(existing.Id, out var id))
            return;

        var currentPath = existing.Locations?.FirstOrDefault();
        if (!string.Equals(currentPath, mountPoint, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(currentPath))
                libraryManager.RemoveMediaPath(id, currentPath);

            var folder = FindCollectionFolder(libraryManager, name);
            if (folder is not null)
                libraryManager.AddMediaPaths(folder, new[] { new MediaPathInfo { Path = mountPoint } }, true);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static CollectionFolder? FindCollectionFolder(ILibraryManager libraryManager, string name)
    {
        var root = libraryManager.GetUserRootFolder();
        return root
            .GetChildren(new InternalItemsQuery())
            .OfType<CollectionFolder>()
            .FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryParseId(string? value, out long id)
    {
        id = 0;
        if (string.IsNullOrEmpty(value))
            return false;

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
            return true;

        if (long.TryParse(value, out id))
            return true;

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _managedLibrary?.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(EmbyManagedLibraryService));
    }
}
