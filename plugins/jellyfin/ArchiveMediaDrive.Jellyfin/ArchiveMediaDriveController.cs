using System.Text.Json;
using ArchiveMediaDrive.Core;
using MediaBrowser.Controller;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArchiveMediaDrive.Jellyfin;

[ApiController]
[Authorize]
[Route("ArchiveMediaDrive")]
public sealed class ArchiveMediaDriveController : ControllerBase
{
    private readonly RcloneEnvironment _rcloneEnvironment;
    private readonly IRcloneGateway _rcloneGateway;
    private readonly ISourceSnapshotStore _sourceStore;
    private readonly ManagedLibraryService _managedLibrary;
    private readonly IDiagnosticsPackageBuilder _diagnosticsBuilder;
    private readonly IConfigurationCoordinator _coordinator;
    private readonly IServerApplicationHost _applicationHost;

    public ArchiveMediaDriveController(
        RcloneEnvironment rcloneEnvironment,
        IRcloneGateway rcloneGateway,
        ISourceSnapshotStore sourceStore,
        ManagedLibraryService managedLibrary,
        IDiagnosticsPackageBuilder diagnosticsBuilder,
        IConfigurationCoordinator coordinator,
        IServerApplicationHost applicationHost)
    {
        _rcloneEnvironment = rcloneEnvironment;
        _rcloneGateway = rcloneGateway;
        _sourceStore = sourceStore;
        _managedLibrary = managedLibrary;
        _diagnosticsBuilder = diagnosticsBuilder;
        _coordinator = coordinator;
        _applicationHost = applicationHost;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        var status = await BuildStatusAsync(cancellationToken);
        return Ok(status);
    }

    [HttpGet("diagnostics")]
    public async Task<IActionResult> GetDiagnostics(CancellationToken cancellationToken)
    {
        var context = await BuildContextAsync(cancellationToken);
        var stream = new MemoryStream();
        await _diagnosticsBuilder.BuildAsync(context, stream, cancellationToken);
        stream.Position = 0;
        return File(stream, "application/zip", $"archive-mediadrive-diagnostics-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip");
    }

    [HttpPost("apply")]
    public async Task<IActionResult> Apply(CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var sources = LoadSources(config.SourcesJson);

        var result = await _coordinator.ApplyAsync(sources, cancellationToken);

        if (!result.Succeeded)
            return BadRequest(result.Errors);

        return Ok(result);
    }

    private static IReadOnlyList<SourceDefinition> LoadSources(string sourcesJson)
    {
        if (string.IsNullOrWhiteSpace(sourcesJson))
            return Array.Empty<SourceDefinition>();

        try
        {
            var doc = JsonDocument.Parse(sourcesJson);
            return doc.RootElement.EnumerateArray()
                .Select(element => new SourceDefinition
                {
                    Id = element.GetProperty("id").GetString()!,
                    Name = element.GetProperty("name").GetString()!,
                    Kind = (SourceKind)Enum.Parse(typeof(SourceKind), element.GetProperty("kind").GetString()!, true),
                    Value = element.GetProperty("value").GetString()!,
                    Enabled = element.GetProperty("enabled").GetBoolean(),
                    RefreshMinutes = element.GetProperty("refreshMinutes").GetInt32(),
                })
                .ToList();
        }
        catch
        {
            return Array.Empty<SourceDefinition>();
        }
    }

    private async Task<object> BuildStatusAsync(CancellationToken cancellationToken)
    {
        var context = await BuildContextAsync(cancellationToken);
        return new
        {
            version = context.PluginVersion,
            hostVersion = context.HostVersion,
            runtimeStatus = context.Probe is null ? "not available" : "ok",
            rcloneVersion = context.Probe?.Version,
            rcloneHash = context.Receipt?.ExecutableSha256,
            mountStatus = context.MountRunning ? "running" : "stopped",
            mountPath = context.MountPath,
            cacheUsage = context.CacheUsageBytes,
            lastRefresh = context.LastRefresh,
            sourceCount = context.SourceCount,
            itemCount = context.ItemCount,
            lastError = context.LastError,
        };
    }

    private async Task<DiagnosticsPackageContext> BuildContextAsync(CancellationToken cancellationToken)
    {
        var sources = await GetSourcesAsync(cancellationToken);
        var dataDir = DataDirectory;
        var snapshots = await GetSourceSummariesAsync(sources, cancellationToken);

        RcloneProbe? probe = null;
        try
        {
            probe = await _rcloneGateway.ProbeAsync(cancellationToken);
        }
        catch
        {
        }

        RcloneReceipt? receipt = null;
        try
        {
            var receiptPath = _rcloneEnvironment.RuntimeManager.ReceiptPath;
            if (System.IO.File.Exists(receiptPath))
            {
                var json = System.IO.File.ReadAllText(receiptPath);
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
            MountRunning = _managedLibrary?.IsRunning ?? false,
            MountPath = _managedLibrary?.MountPoint ?? Path.Combine(dataDir, "mount"),
            CacheUsageBytes = await GetDirectorySizeAsync(dataDir, cancellationToken),
            LastRefresh = lastRefresh,
            SourceCount = sources.Count,
            ItemCount = snapshots.Sum(s => s.Count),
            LastError = lastError,
            SourceSummaries = snapshots,
            PluginVersion = GetPluginVersion(),
            HostVersion = _applicationHost.ApplicationVersion?.ToString() ?? string.Empty,
            RecentLogs = null,
        };
    }

    private async Task<IReadOnlyList<SourceDefinition>> GetSourcesAsync(CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null)
            return Array.Empty<SourceDefinition>();

        return await Task.Run(() => LoadSources(config.SourcesJson), cancellationToken);
    }

    private async Task<IReadOnlyList<SourceSnapshot>> GetSourceSummariesAsync(IReadOnlyList<SourceDefinition> sources, CancellationToken cancellationToken)
    {
        var tasks = sources.Select(s => _sourceStore.GetAsync(s.Id, cancellationToken)).ToList();
        var results = await Task.WhenAll(tasks);
        return results.Where(s => s is not null).Cast<SourceSnapshot>().ToList();
    }

    private static string GetPluginVersion()
    {
        var version = typeof(Plugin).Assembly.GetName().Version;
        return version?.ToString() ?? "0.1.0";
    }

    private static Task<long> GetDirectorySizeAsync(string path, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            if (!Directory.Exists(path))
                return 0L;

            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .AsParallel()
                .Sum(f => new FileInfo(f).Length);
        }, cancellationToken);
    }

    private static string DataDirectory
    {
        get
        {
            var plugin = Plugin.Instance;
            var programData = plugin?.ApplicationPaths.ProgramDataPath;
            if (string.IsNullOrWhiteSpace(programData))
                return Path.GetTempPath();

            return Path.Combine(programData, "plugins", "ArchiveMediaDrive");
        }
    }
}
