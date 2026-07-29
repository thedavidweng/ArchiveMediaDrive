using ArchiveMediaDrive.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArchiveMediaDrive.Jellyfin;

public sealed class ManagedLibraryHostedService : IHostedService, IDisposable
{
    private readonly ManagedLibraryService _managedLibrary;
    private readonly ILogger<ManagedLibraryHostedService> _logger;
    private bool _disposed;

    public ManagedLibraryHostedService(ManagedLibraryService managedLibrary, ILogger<ManagedLibraryHostedService> logger)
    {
        _managedLibrary = managedLibrary;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _managedLibrary.StartAsync(cancellationToken);
            _logger.LogInformation("ArchiveMediaDrive Managed Library started at {MountPoint}", _managedLibrary.MountPoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start ArchiveMediaDrive Managed Library");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
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
