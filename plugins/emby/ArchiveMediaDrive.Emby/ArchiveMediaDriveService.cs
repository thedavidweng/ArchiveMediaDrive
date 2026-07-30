using System.Text.Json;
using ArchiveMediaDrive.Core;
using MediaBrowser.Model.Services;

namespace ArchiveMediaDrive.Emby;

public sealed class ArchiveMediaDriveStatus
{
    public string Version { get; set; } = string.Empty;
    public string HostVersion { get; set; } = string.Empty;
    public string RuntimeStatus { get; set; } = string.Empty;
    public string? RcloneVersion { get; set; }
    public string? RcloneHash { get; set; }
    public string MountStatus { get; set; } = string.Empty;
    public string MountPath { get; set; } = string.Empty;
    public long CacheUsage { get; set; }
    public DateTimeOffset? LastRefresh { get; set; }
    public int SourceCount { get; set; }
    public int ItemCount { get; set; }
    public string? LastError { get; set; }
}

[Route("/ArchiveMediaDrive/Status", "GET")]
public sealed class GetArchiveMediaDriveStatus : IReturn<ArchiveMediaDriveStatus>
{
}

[Route("/ArchiveMediaDrive/Diagnostics", "GET")]
public sealed class GetArchiveMediaDriveDiagnostics
{
}

public sealed class ArchiveMediaDriveService : IService
{
    public ArchiveMediaDriveStatus Get(GetArchiveMediaDriveStatus request)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
            return new ArchiveMediaDriveStatus();

        var context = BuildContext(plugin).GetAwaiter().GetResult();

        return new ArchiveMediaDriveStatus
        {
            Version = context.PluginVersion,
            HostVersion = context.HostVersion,
            RuntimeStatus = context.Probe is null ? "not available" : "ok",
            RcloneVersion = context.Probe?.Version,
            RcloneHash = context.Receipt?.ExecutableSha256,
            MountStatus = context.MountRunning ? "running" : "stopped",
            MountPath = context.MountPath ?? string.Empty,
            CacheUsage = context.CacheUsageBytes,
            LastRefresh = context.LastRefresh,
            SourceCount = context.SourceCount,
            ItemCount = context.ItemCount,
            LastError = context.LastError,
        };
    }

    public object Get(GetArchiveMediaDriveDiagnostics request)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
            return Array.Empty<byte>();

        var context = BuildContext(plugin).GetAwaiter().GetResult();
        using var zip = new MemoryStream();
        plugin.DiagnosticsPackageBuilder.BuildAsync(context, zip, default).GetAwaiter().GetResult();

        return new MemoryStream(zip.ToArray());
    }

    private static async Task<DiagnosticsPackageContext> BuildContext(Plugin plugin)
    {
        var sources = plugin.GetSourceDefinitions();
        var dataDir = plugin.DataFolderPath;
        var gateway = new RcloneLoopbackGateway(plugin.RcloneEnvironment);
        var store = plugin.SourceStore;
        var snapshots = await GetSourceSummariesAsync(sources, store).ConfigureAwait(false);

        RcloneProbe? probe = null;
        try
        {
            probe = await gateway.ProbeAsync(default).ConfigureAwait(false);
        }
        catch
        {
        }

        RcloneReceipt? receipt = null;
        try
        {
            var receiptPath = plugin.RcloneEnvironment.RuntimeManager.ReceiptPath;
            if (File.Exists(receiptPath))
            {
                var json = File.ReadAllText(receiptPath);
                receipt = JsonSerializer.Deserialize<RcloneReceipt>(json, ArchiveMediaDriveJson.Options);
            }
        }
        catch
        {
        }

        var lastRefresh = snapshots.Count == 0 ? null : snapshots.Max(s => (DateTimeOffset?)s.LastAttempt);
        var lastError = snapshots
            .Where(s => !string.IsNullOrWhiteSpace(s.LastError))
            .OrderByDescending(s => s.LastAttempt)
            .FirstOrDefault()?.LastError;

        return new DiagnosticsPackageContext
        {
            Sources = sources,
            Receipt = receipt,
            Probe = probe,
            MountRunning = plugin.ManagedLibrary.IsRunning,
            MountPath = plugin.ManagedLibrary.MountPoint,
            CacheUsageBytes = await GetDirectorySizeAsync(dataDir, default).ConfigureAwait(false),
            LastRefresh = lastRefresh,
            SourceCount = sources.Count,
            ItemCount = snapshots.Sum(s => s.Count),
            LastError = lastError,
            SourceSummaries = snapshots,
            PluginVersion = GetPluginVersion(),
            HostVersion = plugin.HostVersion,
            RecentLogs = null,
        };
    }

    private static async Task<IReadOnlyList<SourceSnapshot>> GetSourceSummariesAsync(IReadOnlyList<SourceDefinition> sources, ISourceSnapshotStore store)
    {
        var tasks = sources.Select(s => store.GetAsync(s.Id, default)).ToList();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.Where(s => s is not null).Cast<SourceSnapshot>().ToList();
    }

    private static async Task<long> GetDirectorySizeAsync(string path, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            if (!Directory.Exists(path))
                return 0L;

            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .AsParallel()
                .Sum(f => new FileInfo(f).Length);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string GetPluginVersion()
    {
        var version = typeof(Plugin).Assembly.GetName().Version;
        return version?.ToString() ?? "0.1.0";
    }
}
